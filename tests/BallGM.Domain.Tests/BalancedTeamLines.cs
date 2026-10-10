using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

/// <summary>
/// Team lines that make a hand-built set of player lines satisfy the box score's rebound identity, so
/// a test that only cares about points or usage need not invent team rebounds by hand: any live misses
/// the player rebounds do not cover go to the defending team, and any player rebounds beyond the misses
/// are covered by live free-throw misses.
/// </summary>
internal static class BalancedTeamLines
{
    public static (TeamStatLine Home, TeamStatLine Away) For(TeamId home, TeamId away, IReadOnlyCollection<PlayerStatLine> lines)
    {
        var (homeLive, awayTeamDefensive) = Balance(home, away, lines);
        var (awayLive, homeTeamDefensive) = Balance(away, home, lines);

        return (new TeamStatLine(home, 0, homeTeamDefensive, homeLive), new TeamStatLine(away, 0, awayTeamDefensive, awayLive));
    }

    private static (int LiveFreeThrowMisses, int OpponentTeamDefensiveRebounds) Balance(TeamId shooting, TeamId defending, IReadOnlyCollection<PlayerStatLine> lines)
    {
        var misses = lines.Where(line => line.TeamId == shooting).Sum(line => line.FieldGoalsAttempted - line.FieldGoalsMade);
        var boards = lines.Where(line => line.TeamId == shooting).Sum(line => line.OffensiveRebounds) +
            lines.Where(line => line.TeamId == defending).Sum(line => line.DefensiveRebounds);

        return boards >= misses ? (boards - misses, 0) : (0, misses - boards);
    }

    /// <summary>
    /// The same lines with minutes and plus-minus that fill the game: the first five of a side (or all
    /// of a short-handed one) play all 48 minutes and carry the side's margin; anyone else plays none.
    /// For a test that cares about something else, so it need not hand-balance the floor.
    /// </summary>
    public static IReadOnlyList<PlayerStatLine> Normalise(IReadOnlyCollection<PlayerStatLine> lines, TeamId home, int homePoints, int awayPoints)
    {
        var seen = new Dictionary<TeamId, int>();
        return lines.Select(line =>
        {
            var order = seen.TryGetValue(line.TeamId, out var count) ? count : 0;
            seen[line.TeamId] = order + 1;
            var onFloor = order < BoxScore.PlayersOnFloor;
            var margin = line.TeamId == home ? homePoints - awayPoints : awayPoints - homePoints;

            return new PlayerStatLine(
                line.PlayerId, line.TeamId, onFloor ? BoxScore.RegulationMinutes : 0, line.Points, line.OffensiveRebounds,
                line.DefensiveRebounds, line.Assists, line.FieldGoalsAttempted, line.FieldGoalsMade,
                line.ThreePointsAttempted, line.ThreePointsMade, line.FreeThrowsAttempted, line.FreeThrowsMade, line.Started,
                onFloor ? margin : 0, line.Steals, line.Turnovers, line.Blocks, line.PersonalFouls, line.FouledOut, line.Zones);
        }).ToList();
    }
}
