using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Seasons;

namespace BallGM.Application.Leagues;

public sealed partial class LeagueSession
{
    /// <summary>
    /// Every player who has played this phase of the season in progress, with his team, totals and
    /// average rating, and the season's length so far — the league's stats table and the source of
    /// its leaderboards. Empty before a season starts. Built once per league and results version: a
    /// profile reads it for percentiles, and opening one must not rescan the season.
    /// </summary>
    public LeagueStatsSummary LeagueStats(SeasonPhase phase)
    {
        var qualifiers = _snapshot?.Configuration.LeaderQualifiers ?? LeaderQualifiersConfiguration.Default;
        if (_snapshot is null || _seasonRun is null)
        {
            return new LeagueStatsSummary([], qualifiers, 0);
        }

        var version = (_snapshot, _seasonRun, _seasonRun.Results.Count, phase);
        if (_leagueStatsCache is { } cached && cached.Version == version)
        {
            return cached.Stats;
        }

        var seasonGames = _seasonRun.Results
            .Where(result => result.Phase == phase)
            .SelectMany(result => new[] { result.HomeTeamId, result.AwayTeamId })
            .GroupBy(teamId => teamId)
            .Select(games => games.Count())
            .DefaultIfEmpty(0)
            .Max();
        var teamNameOf = TeamNameByPlayer(_snapshot);
        var totals = PlayerSeasonTotals(phase);

        var lines = _snapshot.Players
            .Where(player => totals.ContainsKey(player.Id.Value))
            .Select(player => new LeaguePlayerLine(
                player.Id.Value,
                player.FullName,
                teamNameOf.GetValueOrDefault(player.Id.Value),
                GetLeagueOverviewQuery.DescribePosition(player.Position),
                totals[player.Id.Value].Stats,
                totals[player.Id.Value].AverageRating))
            .ToList();

        var stats = new LeagueStatsSummary(lines, qualifiers, seasonGames);
        _leagueStatsCache = (version, stats);
        return stats;
    }

    private ((LeagueSnapshot Snapshot, SeasonRun Run, int Results, SeasonPhase Phase) Version, LeagueStatsSummary Stats)? _leagueStatsCache;

    /// <summary>
    /// The stats a profile ranks, in the order it shows them — families of three, so a three-wide
    /// wrap reads one family a row — each per game where it is a count, and whether lower is better.
    /// </summary>
    private static readonly (string Stat, Func<CountingStats, double?> Value, bool LowerIsBetter)[] RankedStats =
    [
        ("PTS", stats => PerGame(stats, stats.Points), false),
        ("TS%", StatFormulas.TrueShooting, false),
        ("eFG%", StatFormulas.EffectiveFieldGoal, false),
        ("REB", stats => PerGame(stats, stats.Rebounds), false),
        ("OREB", stats => PerGame(stats, stats.OffensiveRebounds), false),
        ("DREB", stats => PerGame(stats, stats.DefensiveRebounds), false),
        ("AST", stats => PerGame(stats, stats.Assists), false),
        ("TOV", stats => PerGame(stats, stats.Turnovers), true),
        ("+/-", stats => PerGame(stats, stats.PlusMinus), false),
        ("STL", stats => PerGame(stats, stats.Steals), false),
        ("BLK", stats => PerGame(stats, stats.Blocks), false),
        ("PF", stats => PerGame(stats, stats.PersonalFouls), true),
        ("FG%", StatFormulas.FieldGoalPercent, false),
        ("3P%", StatFormulas.ThreePointPercent, false),
        ("FT%", StatFormulas.FreeThrowPercent, false),
    ];

    private static double? PerGame(CountingStats stats, int total) => StatFormulas.Scaled(total, stats, StatBasis.PerGame);

    /// <summary>
    /// Where a player stands in the league this regular season, stat by stat, as a 0–100 percentile
    /// among the players who qualify for that stat (<see cref="LeagueStatsSummary.Qualifies"/>, per
    /// game). Empty when he does not qualify on games himself: a percentile of a 3-game sample is noise.
    /// </summary>
    private IReadOnlyList<StatPercentile> Percentiles(string playerId)
    {
        var league = LeagueStats(SeasonPhase.RegularSeason);
        var player = league.Players.FirstOrDefault(line => line.PlayerId == playerId);
        if (player is null || !league.QualifiesOnGames(player))
        {
            return [];
        }

        var percentiles = new List<StatPercentile>();
        foreach (var (stat, value, lowerIsBetter) in RankedStats)
        {
            if (league.Qualifies(stat, player, StatBasis.PerGame) && value(player.Stats) is { } mine)
            {
                var population = league.Players
                    .Where(line => league.Qualifies(stat, line, StatBasis.PerGame))
                    .Select(line => value(line.Stats))
                    .OfType<double>()
                    .ToList();
                percentiles.Add(new StatPercentile(stat, StatFormulas.Percentile(mine, population, lowerIsBetter), lowerIsBetter));
            }
        }

        return percentiles;
    }
}
