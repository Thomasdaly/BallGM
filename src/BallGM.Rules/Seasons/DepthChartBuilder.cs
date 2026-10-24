using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Players;

namespace BallGM.Rules.Seasons;

/// <summary>
/// One player the allocator may use: who they are, where they play, and how good they are — the full
/// rating, because the match engine reads a player's shape (passing, quickness, size) as well as
/// their level. The rotation itself is still built from <see cref="Overall"/>.
/// </summary>
/// <param name="MinutesPriority">
/// Rating points a coach adds (or takes away) when choosing who plays — zero for a team playing to win;
/// a tanking team's young players carry a positive one and its veterans a negative one
/// (<c>BallGM.Rules.AI.DevelopmentMinutesPolicy</c>). It moves who starts and in what order the bench
/// plays; it never changes how well anyone plays.
/// </param>
public sealed record AvailablePlayer(PlayerId PlayerId, Position Position, PlayerRating Rating, int MinutesPriority = 0)
{
    public int Overall => Rating.Overall;
}

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
            .OrderByDescending(player => player.Overall + player.MinutesPriority)
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

        // Cover the five positions first, with the five who are best at them together (see
        // StartingFive). A team that has no natural centre still has to put someone at centre, and that
        // is a fact worth reporting rather than a hole to leave in the chart.
        foreach (var (starter, position) in StartingFive(pool))
        {
            used.Add(starter.PlayerId.Value);
            assignments.Add((starter, position));

            if (starter.Position != position && pool.All(player => player.Position != position))
            {
                notes.Add(new RuleFinding(
                    OutOfPositionCode,
                    $"Team '{teamId.Value}' has nobody who plays {position}, so {starter.PlayerId.Value} covers it out of position.",
                    teamId));
            }
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
    /// The starting five and where each plays: of the best players by Current Ability (the
    /// <see cref="MinutesAllocationBounds.StarterCandidates"/> best, plus the best natural at each
    /// position so a position someone plays is never left to somebody who does not), the five and the
    /// positions that together rate highest by <see cref="PositionRatings"/>. A coach starts his best
    /// players, but at the spots they can actually play: two fine point guards may both start, one of
    /// them at the two, while a third guard sits behind a worse natural centre.
    /// <para>
    /// Exact and deterministic: candidates are taken in the pool's order (rating, then identifier), a
    /// small dynamic programme over which of them are already placed finds the best five and
    /// arrangement together, and only a strictly better score replaces one held, so a tie always falls
    /// the same way.
    /// </para>
    /// </summary>
    private static List<(AvailablePlayer Player, Position UsedAt)> StartingFive(IReadOnlyList<AvailablePlayer> pool)
    {
        var positions = Enum.GetValues<Position>();
        var candidates = pool.Take(MinutesAllocationBounds.StarterCandidates).ToList();
        foreach (var position in positions)
        {
            var natural = pool.FirstOrDefault(player => player.Position == position);
            if (natural is not null && !candidates.Contains(natural))
            {
                candidates.Add(natural);
            }
        }

        var count = candidates.Count;
        var fit = new int[count, positions.Length];
        for (var index = 0; index < count; index++)
        {
            for (var slot = 0; slot < positions.Length; slot++)
            {
                // A coach starts his best players: each of the top few carries a bonus no fit
                // difference can outweigh, short of a position he cannot really play.
                fit[index, slot] = FitAt(candidates[index], positions[slot]) + (index < MinutesAllocationBounds.AlwaysStart ? AlwaysStartBonus : 0);
            }
        }

        var size = Math.Min(positions.Length, count);

        // An exact search over which candidates fill the first `size` positions, in position order:
        // score[mask] is the best total for placing exactly the candidates in `mask` at the first
        // popcount(mask) positions. Masks are walked in ascending order and only a strictly better total
        // replaces one held, so a tie always resolves the same way.
        var states = 1 << count;
        var score = new int[states];
        var last = new int[states];
        Array.Fill(score, int.MinValue);
        score[0] = 0;
        var bestMask = -1;
        for (var mask = 0; mask < states; mask++)
        {
            if (score[mask] == int.MinValue)
            {
                continue;
            }

            var slot = System.Numerics.BitOperations.PopCount((uint)mask);
            if (slot == size)
            {
                if (bestMask < 0 || score[mask] > score[bestMask])
                {
                    bestMask = mask;
                }

                continue;
            }

            for (var candidate = 0; candidate < count; candidate++)
            {
                if ((mask & (1 << candidate)) != 0)
                {
                    continue;
                }

                var next = mask | (1 << candidate);
                var total = score[mask] + fit[candidate, slot];
                if (total > score[next])
                {
                    score[next] = total;
                    last[next] = candidate;
                }
            }
        }

        var best = new int[size];
        for (int slot = size - 1, mask = bestMask; slot >= 0; slot--)
        {
            best[slot] = last[mask];
            mask &= ~(1 << last[mask]);
        }

        return Enumerable.Range(0, size).Select(slot => (candidates[best[slot]], positions[slot])).ToList();
    }

    /// <summary>
    /// How well a player fits a position: his position rating there, less his familiarity penalty. A
    /// position his sheet calls awkward — or, on a sheet that assesses no familiarity at all (a flat or
    /// legacy sheet), any position but his own — is a last resort, scored so far down that he is put
    /// there only when the roster leaves no other way to cover it.
    /// </summary>
    private static int FitAt(AvailablePlayer player, Position position)
    {
        var rating = PositionRatings.For(player.Rating, position) + player.MinutesPriority;
        var playable = player.Rating.Positions.TryGetValue(position, out var familiarity)
            ? familiarity > PositionFamiliarity.Awkward
            : player.Position == position;
        return playable ? rating : rating - LastResortPenalty;
    }

    /// <summary>What putting a player somewhere he cannot really play costs a starting five's score.</summary>
    private const int LastResortPenalty = 1_000;

    /// <summary>
    /// What one of a team's <see cref="MinutesAllocationBounds.AlwaysStart"/> best players adds to a
    /// starting five's score: more than any arrangement of fits can differ by, less than the
    /// last-resort penalty, so he starts wherever he can genuinely play.
    /// </summary>
    private const int AlwaysStartBonus = 500;

    /// <summary>
    /// Divides the game's minutes across the rotation the way a coach does: by role first — the
    /// starters' share by how they rank among the starters, the bench's by its order — then moved by
    /// how much better or worse each player is than the rotation's average and by how far his stamina
    /// stands out from his level, inside the stated bounds. Any rounding remainder goes to the best
    /// player who still has room for it. A star on a thin team plays well into the late thirties; a
    /// deep team spreads its minutes.
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
                $"Team '{teamId.Value}' has {ExplanationText.Count(count, "available player")}, fewer than the {MinutesAllocationBounds.MinimumRotationWithinBounds} needed to cover {MinutesAllocationBounds.TeamMinutesPerGame} minutes inside the {MinutesAllocationBounds.MaximumMinutesPerPlayer}-minute per-player maximum. Everyone plays {minutes.Max()} minutes.",
                teamId));

            return minutes;
        }

        // Role: the starters ranked among themselves by rating, then the bench in the order it was
        // picked (best first). The template is for a full rotation; a shorter one shares out the
        // missing places' minutes in proportion, so it still sums to the game.
        var starters = Math.Min(MinutesAllocationBounds.PlayersOnFloor, count);
        var starterOrder = Enumerable.Range(0, starters)
            .OrderByDescending(index => assignments[index].Player.Overall)
            .ThenBy(index => assignments[index].Player.PlayerId.Value, StringComparer.Ordinal)
            .ToArray();
        var role = new int[count];
        for (var rank = 0; rank < starters; rank++)
        {
            role[starterOrder[rank]] = MinutesAllocationBounds.RoleMinutes(rank);
        }

        for (var index = starters; index < count; index++)
        {
            role[index] = MinutesAllocationBounds.RoleMinutes(index);
        }

        var roleTotal = role.Sum();
        var meanOverall = assignments.Sum(assignment => assignment.Player.Overall) / count;
        var allocated = 0;

        for (var index = 0; index < count; index++)
        {
            var rating = assignments[index].Player.Rating;
            var tenths = (role[index] * MinutesAllocationBounds.TeamMinutesPerGame * 10 / roleTotal)
                + ((rating.Overall - meanOverall) * MinutesAllocationBounds.TenthsPerRatingPoint)
                + Math.Clamp(
                    (rating[PlayerAttribute.Stamina] - rating.Overall) * MinutesAllocationBounds.TenthsPerStaminaPoint,
                    -MinutesAllocationBounds.MaximumStaminaTenths,
                    MinutesAllocationBounds.MaximumStaminaTenths);

            minutes[index] = Math.Clamp(
                (tenths + 5) / 10,
                MinutesAllocationBounds.MinimumRotationMinutes,
                MinutesAllocationBounds.MaximumPlannedMinutes);

            allocated += minutes[index];
        }

        // Clamping and rounding both leave the total off the game's minutes. The difference is settled
        // a minute a player at a time, walking the rotation from the biggest role down to add and from
        // the smallest up to take away: deterministic, and spread rather than dumped on one player.
        var difference = MinutesAllocationBounds.TeamMinutesPerGame - allocated;
        var order = Enumerable.Range(0, count)
            .OrderByDescending(index => minutes[index])
            .ThenBy(index => assignments[index].Player.PlayerId.Value, StringComparer.Ordinal)
            .ToArray();

        while (difference != 0)
        {
            var moved = false;

            for (var index = 0; index < count && difference != 0; index++)
            {
                var target = difference > 0 ? order[index] : order[count - 1 - index];

                if (difference > 0 && minutes[target] < MinutesAllocationBounds.MaximumPlannedMinutes)
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
