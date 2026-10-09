using BallGM.Domain.Common;
using BallGM.Domain.Seasons;
using BallGM.Rules.Seasons;

namespace BallGM.Simulation.Seasons;

/// <summary>
/// Turns a rotation's planned minutes into who is on the floor for every possession of regulation —
/// deterministically, with no randomness, so a seed replays the same lineups and the same game.
/// <para>
/// <b>Possession debt.</b> Each player's share of the game's possessions is apportioned from their
/// depth-chart minutes (largest remainder, so the targets sum to exactly five per possession). At each
/// checkpoint the five on the floor are those still owed most possessions; anyone owed as many
/// possessions as are left is forced on. A segment runs until the next checkpoint, until someone on
/// the floor has played their share, or until someone off it would be owed more than is left —
/// whichever comes first — so every debt reaches exactly zero by the final possession. The starters
/// take the opening tip.
/// </para>
/// <para>
/// A rotation of five or fewer plays every possession: there is nobody to rotate in.
/// </para>
/// </summary>
public static class StintPlanner
{
    /// <summary>The longest a five plays before the rotation is looked at again, in possessions.</summary>
    public const int CheckpointPossessions = 6;

    /// <summary>
    /// The lineup for each possession, as indices into the rotation. Possessions sharing a lineup
    /// share the same array, so a whole game is a handful of allocations.
    /// </summary>
    public static int[][] Plan(IReadOnlyList<RotationPlayer> rotation, int possessions)
    {
        ArgumentNullException.ThrowIfNull(rotation);
        ArgumentOutOfRangeException.ThrowIfNegative(possessions);

        var count = rotation.Count;
        var plan = new int[possessions][];

        if (count <= MinutesAllocationBounds.PlayersOnFloor)
        {
            var everyone = Enumerable.Range(0, count).ToArray();
            Array.Fill(plan, everyone);
            return plan;
        }

        var debt = Targets(rotation, possessions);
        var onFloor = new int[MinutesAllocationBounds.PlayersOnFloor];
        var isOn = new bool[count];

        // The tie-break keys, read once into arrays — the identifier as its ordinal rank, so the last
        // tie-break compares integers rather than strings at every checkpoint.
        var starter = new bool[count];
        var depth = new int[count];
        var rank = new int[count];
        for (var index = 0; index < count; index++)
        {
            starter[index] = rotation[index].IsStarter;
            depth[index] = rotation[index].DepthRank;
        }

        var byKey = Enumerable.Range(0, count).OrderBy(index => rotation[index].Key, StringComparer.Ordinal).ToArray();
        for (var position = 0; position < count; position++)
        {
            rank[byKey[position]] = position;
        }

        var keys = new TieBreaks(starter, depth, rank);

        for (var possession = 0; possession < possessions;)
        {
            var remaining = possessions - possession;
            Choose(keys, debt, remaining, opening: possession == 0, onFloor, isOn);

            var length = Math.Min(CheckpointPossessions, remaining);
            var maxOff = 0;
            for (var index = 0; index < count; index++)
            {
                if (isOn[index])
                {
                    length = Math.Min(length, debt[index]);
                }
                else
                {
                    maxOff = Math.Max(maxOff, debt[index]);
                }
            }

            length = Math.Max(1, Math.Min(length, remaining - maxOff));

            var lineup = (int[])onFloor.Clone();
            for (var step = 0; step < length; step++)
            {
                plan[possession + step] = lineup;
            }

            foreach (var index in lineup)
            {
                debt[index] -= length;
            }

            possession += length;
        }

        return plan;
    }

    /// <summary>
    /// The minutes each player is credited with: regulation possessions on the floor scaled to the
    /// 48-minute game, plus overtime possessions scaled to the 5-minute period, apportioned by largest
    /// remainder so the team's minutes are exactly 240 plus 25 per overtime.
    /// </summary>
    public static int[] Minutes(int[] regulationPossessionsOn, int regulationPossessions, int[] overtimePossessionsOn, int overtimePeriods)
    {
        ArgumentNullException.ThrowIfNull(regulationPossessionsOn);
        ArgumentNullException.ThrowIfNull(overtimePossessionsOn);

        var count = regulationPossessionsOn.Length;
        var perOvertime = MatchModelBounds.OvertimePossessions;
        var denominator = (long)perOvertime * Math.Max(1, regulationPossessions);
        var numerators = new long[count];

        for (var index = 0; index < count; index++)
        {
            numerators[index] =
                ((long)MinutesAllocationBounds.RegulationMinutes * perOvertime * regulationPossessionsOn[index]) +
                ((long)MatchModelBounds.OvertimeMinutes * Math.Max(1, regulationPossessions) * overtimePossessionsOn[index]);
        }

        return Apportion.LargestRemainder(numerators, denominator, BoxScore.GameMinutes(count, overtimePeriods));
    }

    /// <summary>Each player's share of the game's possessions, from their depth-chart minutes, summing to five per possession.</summary>
    private static int[] Targets(IReadOnlyList<RotationPlayer> rotation, int possessions)
    {
        // Exact rational shares (minutes × possessions × 5 / planned minutes), so the floors can never
        // overshoot and largest remainder lands on exactly five places a possession — whatever the
        // rotation plans in total, though the depth chart always plans the full 240.
        var numerators = rotation.Select(player => (long)player.PlannedMinutes * possessions * MinutesAllocationBounds.PlayersOnFloor).ToArray();
        var plannedTotal = Math.Max(1, rotation.Sum(player => player.PlannedMinutes));

        var targets = Apportion.LargestRemainder(numerators, plannedTotal, MinutesAllocationBounds.PlayersOnFloor * possessions);

        // Nobody can play more possessions than the game has. The depth chart caps a player at 42
        // minutes, so this never binds for a built rotation; a hand-built one planning more than a
        // full game for someone has the excess handed, one possession at a time, to whoever has room.
        for (var index = 0; index < targets.Length; index++)
        {
            while (targets[index] > possessions)
            {
                targets[index]--;
                var roomiest = 0;
                for (var other = 1; other < targets.Length; other++)
                {
                    if (targets[other] < targets[roomiest])
                    {
                        roomiest = other;
                    }
                }

                targets[roomiest]++;
            }
        }

        return targets;
    }

    private static void Choose(TieBreaks keys, int[] debt, int remaining, bool opening, int[] onFloor, bool[] isOn)
    {
        Array.Fill(isOn, false);

        // An insertion sort over at most fifteen players, not a LINQ chain: this runs at every
        // checkpoint of every game, and the keys are cheap integer comparisons.
        var count = debt.Length;
        Span<int> order = stackalloc int[count];
        for (var index = 0; index < count; index++)
        {
            var at = index;
            while (at > 0 && Ahead(index, order[at - 1]))
            {
                order[at] = order[at - 1];
                at--;
            }

            order[at] = index;
        }

        var chosen = order[..MinutesAllocationBounds.PlayersOnFloor];
        chosen.Sort();

        for (var slot = 0; slot < chosen.Length; slot++)
        {
            onFloor[slot] = chosen[slot];
            isOn[chosen[slot]] = true;
        }

        // Whether player a goes on the floor ahead of player b: forced on first, then the starters at
        // the opening tip, then whoever is owed more, then starters, depth, and identifier.
        bool Ahead(int a, int b)
        {
            int Compare(bool x, bool y) => x == y ? 0 : x ? -1 : 1;

            var result = Compare(debt[a] >= remaining, debt[b] >= remaining);
            if (result == 0 && opening)
            {
                result = Compare(keys.Starter[a], keys.Starter[b]);
            }

            if (result == 0)
            {
                result = Compare(debt[a] > 0, debt[b] > 0);
            }

            if (result == 0)
            {
                result = debt[b].CompareTo(debt[a]);
            }

            if (result == 0)
            {
                result = Compare(keys.Starter[a], keys.Starter[b]);
            }

            if (result == 0)
            {
                result = keys.Depth[a].CompareTo(keys.Depth[b]);
            }

            if (result == 0)
            {
                result = keys.Rank[a].CompareTo(keys.Rank[b]);
            }

            return result < 0;
        }
    }
}

/// <summary>The planner's tie-break keys by rotation index: starter, depth rank, and identifier rank.</summary>
internal readonly record struct TieBreaks(bool[] Starter, int[] Depth, int[] Rank);

/// <summary>One player as the stint planner sees them: planned minutes, whether they start, and a stable key for tie-breaks.</summary>
public sealed record RotationPlayer(string Key, int PlannedMinutes, bool IsStarter, int DepthRank);
