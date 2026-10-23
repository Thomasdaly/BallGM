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
/// The competitor's buyout market, measured on the thirty-team calibration league over chained summers
/// (<c>docs/franchise-realism.md</c> → F4): buyouts stay a fringe-veteran affair — never one of the
/// league's sixty best players — and no bought-out player is back with the team that released him.
/// </summary>
public sealed class BuyoutMarketTests
{
    [Fact]
    public void BuyoutsStayRare_AndNoBoughtOutPlayerReturnsToTheTeamThatReleasedHim()
    {
        var session = NewSession();
        var total = 0;
        for (var seed = 1; seed <= 4; seed++)
        {
            Assert.True(session.StartSeason(seed).IsSuccess);
            Assert.True(session.AdvanceToEndOfSeason().IsSuccess);
            var topSixty = session.Overview().Value.Teams
                .SelectMany(team => team.Roster)
                .OrderByDescending(player => player.Overall)
                .Take(60)
                .Select(player => player.PlayerId)
                .ToHashSet();
            var conclusion = session.ConcludeSeason();
            Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));

            var buyouts = conclusion.Value.Buyouts;
            total += buyouts.Count;
            // A real league buys out a dozen or so fringe veterans a season; what must never happen is a
            // good player bought out and moving on.
            Assert.True(buyouts.Count <= 10, $"{buyouts.Count} buyouts in one summer across thirty teams.");
            Assert.All(buyouts, buyout => Assert.DoesNotContain(buyout.PlayerId, topSixty));

            var teamOf = session.Overview().Value.Teams
                .SelectMany(team => team.Roster.Select(player => (player.PlayerId, team.TeamId)))
                .ToDictionary(entry => entry.PlayerId, entry => entry.TeamId);
            Assert.All(buyouts, buyout => Assert.NotEqual(buyout.TeamId, teamOf.GetValueOrDefault(buyout.PlayerId)));
        }

        Assert.InRange(total, 0, 30);
    }

    private static LeagueSession NewSession()
    {
        var session = new LeagueSession(
            new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 1),
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
