using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Rules.Seasons;

/// <summary>One player the allocator may use: who they are, where they play, and how good they are.</summary>
public sealed record AvailablePlayer(PlayerId PlayerId, Position Position, int Overall);

/// <summary>
/// A built rotation, with everything the rules had to say about building it — a position nobody on
/// the roster plays, a squad too short to cover a game inside the minutes bound.
/// </summary>
public sealed record DepthChartBuild(
    DepthChart Chart,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes);

/// <summary>
/// Decides who plays and for how long.
/// <para>
/// Positional, and by the same positional concept the Milestone 6b free-agency board reads a market
/// against — one notion of "our depth at the three", used both to decide who takes the floor and to
/// advise whether to sign there. Two notions would be two answers to one question, and the market
/// screen would be recommending against a squad the engine does not field.
/// </para>
/// <para>
/// Entirely deterministic: no random source, no clock. The same roster produces the same rotation
/// every time, so a difference between two runs of a season is always a difference in the games and
/// never in who was picked to play them.
/// </para>
/// </summary>
public sealed class DepthChartBuilder
{
    private const string NoAvailablePlayersCode = "depth_chart.no_available_players";
    private const string ShortHandedCode = "depth_chart.minutes_above_normal_maximum";
    private const string OutOfPositionCode = "depth_chart.position_covered_out_of_position";
    private const string BelowRosterMinimumCode = "depth_chart.roster_below_league_minimum";

    public DomainOperationResult<DepthChartBuild> Build(
        TeamId teamId,
        IEnumerable<AvailablePlayer> available,
        RosterSizeLimits rosterLimits,
        int rosterCount)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(rosterLimits);

        var warnings = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        // Ordinal identifier under the rating, so two equally rated players always fall the same way
        // round. Without it the rotation would depend on enumeration order and two runs of a season
        // could field different fives.
        var pool = available
            .OrderByDescending(player => player.Overall)
            .ThenBy(player => player.PlayerId.Value, StringComparer.Ordinal)
            .ToList();

        if (rosterCount < rosterLimits.MinimumPlayers)
        {
            notes.Add(new RuleFinding(
                BelowRosterMinimumCode,
                $"Team '{teamId.Value}' has {rosterCount} players against a league minimum of {rosterLimits.MinimumPlayers}. A short roster is an obligation to fill, not a bar on playing, so the team is fielded from who it has.",
                teamId));
        }

        if (pool.Count == 0)
        {
            warnings.Add(new RuleFinding(
                NoAvailablePlayersCode,
                $"Team '{teamId.Value}' has nobody available to play. It fields no rotation at all.",
                teamId));

            return DomainOperationResult<DepthChartBuild>.Success(
                new DepthChartBuild(DepthChart.Empty(teamId), warnings, notes));
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var assignments = new List<(AvailablePlayer Player, Position UsedAt)>();

        // Cover the five positions first. A team that has no natural centre still has to put someone
        // at centre, and that is a fact worth reporting rather than a hole to leave in the chart.
        foreach (var position in Enum.GetValues<Position>())
        {
            var natural = pool.FirstOrDefault(player => player.Position == position && !used.Contains(player.PlayerId.Value));

            if (natural is not null)
            {
                used.Add(natural.PlayerId.Value);
                assignments.Add((natural, position));
                continue;
            }

            var substitute = pool.FirstOrDefault(player => !used.Contains(player.PlayerId.Value));
            if (substitute is null)
            {
                continue;
            }

            used.Add(substitute.PlayerId.Value);
            assignments.Add((substitute, position));

            notes.Add(new RuleFinding(
                OutOfPositionCode,
                $"Team '{teamId.Value}' has nobody who plays {position}, so {substitute.PlayerId.Value} covers it out of position.",
                teamId));
        }

        // Then fill the bench with the best of what is left, at their own positions.
        var bench = pool.Where(player => !used.Contains(player.PlayerId.Value)).ToList();

        foreach (var player in bench)
        {
            if (assignments.Count >= MinutesAllocationBounds.MaximumRotationSize)
            {
                break;
            }

            used.Add(player.PlayerId.Value);
            assignments.Add((player, player.Position));
        }

        var minutes = AllocateMinutes(teamId, assignments, warnings);
        var slots = BuildSlots(assignments, minutes);

        var chartResult = DepthChart.Create(teamId, slots);

        return chartResult.IsFailure
            ? DomainOperationResult<DepthChartBuild>.Failure(chartResult.Errors.ToArray())
            : DomainOperationResult<DepthChartBuild>.Success(new DepthChartBuild(chartResult.Value, warnings, notes));
    }

    /// <summary>
    /// Divides the game's minutes across the rotation in proportion to rating, inside the stated
    /// bounds, and hands any rounding remainder to the best player who still has room for it.
    /// </summary>
    private static int[] AllocateMinutes(
        TeamId teamId,
        IReadOnlyList<(AvailablePlayer Player, Position UsedAt)> assignments,
        List<RuleFinding> warnings)
    {
        var count = assignments.Count;
        var minutes = new int[count];

        if (count < MinutesAllocationBounds.MinimumRotationWithinBounds)
        {
            // Too few players to cover the game without somebody going past the normal maximum. The
            // bound is not quietly relaxed: the game still has to be played, so the minutes are
            // divided evenly and the breach is reported with the figure.
            var each = MinutesAllocationBounds.TeamMinutesPerGame / count;
            var spare = MinutesAllocationBounds.TeamMinutesPerGame % count;

            for (var index = 0; index < count; index++)
            {
                minutes[index] = each + (index < spare ? 1 : 0);
            }

            warnings.Add(new RuleFinding(
                ShortHandedCode,
                $"Team '{teamId.Value}' has {count} available player(s), fewer than the {MinutesAllocationBounds.MinimumRotationWithinBounds} needed to cover {MinutesAllocationBounds.TeamMinutesPerGame} minutes inside the {MinutesAllocationBounds.MaximumMinutesPerPlayer}-minute per-player maximum. Everyone plays {minutes.Max()} minutes.",
                teamId));

            return minutes;
        }

        var weights = assignments.Select(assignment => (long)Math.Max(1, assignment.Player.Overall)).ToArray();
        var totalWeight = weights.Sum();
        var allocated = 0;

        for (var index = 0; index < count; index++)
        {
            var share = (int)(weights[index] * MinutesAllocationBounds.TeamMinutesPerGame / totalWeight);

            minutes[index] = Math.Clamp(
                share,
                MinutesAllocationBounds.MinimumRotationMinutes,
                MinutesAllocationBounds.MaximumMinutesPerPlayer);

            allocated += minutes[index];
        }

        // Clamping and integer division both leave the total off the game's minutes. The difference
        // is settled by walking the rotation best-first, which is deterministic and keeps the
        // adjustment where a coach would put it.
        var difference = MinutesAllocationBounds.TeamMinutesPerGame - allocated;

        while (difference != 0)
        {
            var moved = false;

            for (var index = 0; index < count && difference != 0; index++)
            {
                var target = difference > 0 ? index : count - 1 - index;

                if (difference > 0 && minutes[target] < MinutesAllocationBounds.MaximumMinutesPerPlayer)
                {
                    minutes[target]++;
                    difference--;
                    moved = true;
                }
                else if (difference < 0 && minutes[target] > MinutesAllocationBounds.MinimumRotationMinutes)
                {
                    minutes[target]--;
                    difference++;
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return minutes;
    }

    private static List<DepthChartSlot> BuildSlots(
        IReadOnlyList<(AvailablePlayer Player, Position UsedAt)> assignments,
        IReadOnlyList<int> minutes)
    {
        var slots = new List<DepthChartSlot>(assignments.Count);
        var ranks = new Dictionary<Position, int>();

        // Depth rank is assigned in the order players were picked, which is rating order within a
        // position — so rank 1 at a position really is the best player the team has there.
        for (var index = 0; index < assignments.Count; index++)
        {
            var (player, usedAt) = assignments[index];
            var rank = ranks.GetValueOrDefault(usedAt) + 1;
            ranks[usedAt] = rank;

            slots.Add(new DepthChartSlot(player.PlayerId, usedAt, rank, minutes[index]));
        }

        return slots;
    }
}
