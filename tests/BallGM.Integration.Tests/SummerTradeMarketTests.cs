using System.Text.RegularExpressions;
using BallGM.Application.Leagues;
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
/// The competitor's trade carousel — half the league's rosters unrecognisable after one summer, the same
/// player flipped twice — measured on the thirty-team calibration league (<c>docs/franchise-realism.md</c>
/// → F5): each team is part of at most one trade a summer, and nobody moves twice.
/// </summary>
public sealed partial class SummerTradeMarketTests
{
    [Fact]
    public void EachTeamTradesAtMostOnceASummer_AndNobodyIsFlippedTwice()
    {
        var session = NewSession();
        for (var seed = 1; seed <= 3; seed++)
        {
            Assert.True(session.StartSeason(seed).IsSuccess);
            Assert.True(session.AdvanceToEndOfSeason().IsSuccess);
            var conclusion = session.ConcludeSeason();
            Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));

            var trades = conclusion.Value.Notes.Where(note => note.RuleCode == "ai_turn.trade_executed").ToList();
            Assert.True(trades.Count <= 15, $"{trades.Count} trades in one summer across thirty teams.");

            var teams = new List<string>();
            var players = new List<string>();
            foreach (var trade in trades)
            {
                var match = TradeLine().Match(trade.Explanation);
                Assert.True(match.Success, trade.Explanation);
                teams.Add(match.Groups["from"].Value);
                teams.Add(match.Groups["to"].Value);
                players.Add(match.Groups["out"].Value);
                players.Add(match.Groups["in"].Value);
            }

            Assert.Equal(teams.Count, teams.Distinct().Count());
            Assert.Equal(players.Count, players.Distinct().Count());
        }
    }

    [GeneratedRegex("^(?<from>.+) traded (?<out>.+) to (?<to>.+) for (?<in>.+)\\.$")]
    private static partial Regex TradeLine();

    private static LeagueSession NewSession()
    {
        var session = new LeagueSession(
            new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 2),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));
        Assert.True(session.Load().IsSuccess);
        return session;
    }
}
