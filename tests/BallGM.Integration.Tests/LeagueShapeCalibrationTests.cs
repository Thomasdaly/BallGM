using BallGM.Application.Leagues;
using BallGM.Domain.Seasons;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// The thirty-team calibration league's per-player shape (sim-audit battery 8), locked by seed: a league
/// has a thirty-point scorer, a double-digit rebounder and passer, and stars who play the late thirties —
/// and nobody plays a whole game. These are the tails a flat model loses first, so a tuning change that
/// flattens them again fails here rather than shipping. Bands are the real league's own, from
/// <c>.claude/skills/sim-audit/targets.json</c> → <c>league_shape</c>, over the average of the seasons.
/// </summary>
public sealed class LeagueShapeCalibrationTests
{
    private const int Seasons = 6;
    private const int MinimumGames = 40;

    [Fact]
    public void TheLeaguesLeadersLookLikeARealLeaguesLeaders()
    {
        var leaders = new Dictionary<string, double>();
        var maximumMinutes = 0.0;

        for (var seed = 1; seed <= Seasons; seed++)
        {
            var session = NewSession(seed);
            session.StartSeason(seed);
            session.AdvanceToEndOfSeason();

            var players = session.LeagueStats(SeasonPhase.RegularSeason).Players
                .Where(player => player.Stats.Games >= MinimumGames)
                .Select(player => player.Stats)
                .ToList();

            void Lead(string stat, Func<CountingStats, int> pick) =>
                leaders[stat] = leaders.GetValueOrDefault(stat) + (players.Max(stats => (double)pick(stats) / stats.Games) / Seasons);

            Lead("ppg", stats => stats.Points);
            Lead("rpg", stats => stats.Rebounds);
            Lead("apg", stats => stats.Assists);
            Lead("mpg", stats => stats.Minutes);
            Lead("fta", stats => stats.FreeThrowsAttempted);
            maximumMinutes = Math.Max(maximumMinutes, players.Max(stats => (double)stats.Minutes / stats.Games));
        }

        Assert.InRange(leaders["ppg"], 28.88, 35.55);
        Assert.InRange(leaders["rpg"], 11.8, 15.4);
        Assert.InRange(leaders["apg"], 10.08, 12.16);
        Assert.InRange(leaders["mpg"], 35.45, 39.92);
        Assert.InRange(leaders["fta"], 9.47, 12.87);
        Assert.True(maximumMinutes < 42, $"Somebody averaged {maximumMinutes:F1} minutes a game.");
    }

    private static LeagueSession NewSession(int seed)
    {
        var session = new LeagueSession(
            new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, seed),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));

        var result = session.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return session;
    }
}
