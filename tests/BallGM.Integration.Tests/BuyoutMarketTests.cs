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
/// (<c>docs/franchise-realism.md</c> → F4): buyouts stay rare, they are of veterans outside a team's
/// best players, and no bought-out player is back with the team that released him the next season.
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
            var conclusion = session.ConcludeSeason();
            Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));

            var buyouts = conclusion.Value.Buyouts;
            total += buyouts.Count;
            Assert.True(buyouts.Count <= 5, $"{buyouts.Count} buyouts in one summer across thirty teams.");

            var teamOf = session.Overview().Value.Teams
                .SelectMany(team => team.Roster.Select(player => (player.PlayerId, team.TeamId)))
                .ToDictionary(entry => entry.PlayerId, entry => entry.TeamId);
            Assert.All(buyouts, buyout => Assert.NotEqual(buyout.TeamId, teamOf.GetValueOrDefault(buyout.PlayerId)));
        }

        Assert.InRange(total, 0, 12);
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
