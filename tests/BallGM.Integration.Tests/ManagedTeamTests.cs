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
/// The franchise the person plays is theirs: the summer's AI re-signs, tenders, buys out, bids, dumps,
/// sells and trades for every other team, and never for — or with — this one.
/// </summary>
public sealed class ManagedTeamTests
{
    private const int Seasons = 3;

    [Fact]
    public void TheSummersAiNeverActsForOrWithTheManagedTeam()
    {
        var session = NewSession();
        var managed = session.Overview().Value.Teams[0].TeamId;
        Assert.True(session.SetManagedTeam(managed).IsSuccess);

        for (var season = 1; season <= Seasons; season++)
        {
            Assert.True(session.StartSeason(300 + season).IsSuccess);
            Assert.True(session.AdvanceToEndOfSeason().IsSuccess);

            var before = session.Overview().Value;
            var mine = before.Teams.Single(team => team.TeamId == managed).Roster;
            var known = before.Teams.SelectMany(team => team.Roster).Select(spot => spot.PlayerId)
                .Concat(before.FreeAgents.Players.Select(agent => agent.PlayerId))
                .ToHashSet();

            var conclusion = session.ConcludeSeason();
            Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));

            var after = session.Overview().Value;
            var now = after.Teams.Single(team => team.TeamId == managed).Roster.Select(spot => spot.PlayerId).ToHashSet();
            var elsewhere = after.Teams.Where(team => team.TeamId != managed).SelectMany(team => team.Roster).Select(spot => spot.PlayerId).ToHashSet();
            var floorFilled = now.Count <= after.MinimumRosterPlayers;

            // Nobody still under contract left by trade, dump, sale or buyout.
            foreach (var spot in mine.Where(spot => spot.ContractSeasonsRemaining >= 2))
            {
                Assert.False(elsewhere.Contains(spot.PlayerId), $"Season {season}: {spot.FullName}, under contract, was moved to another team.");
                Assert.False(after.FreeAgents.Players.Any(agent => agent.PlayerId == spot.PlayerId), $"Season {season}: {spot.FullName}, under contract, was released.");
            }

            // Nobody arrived except draft picks — unless the league had to fill a roster below its minimum.
            foreach (var arrival in now.Where(id => !mine.Any(spot => spot.PlayerId == id)))
            {
                Assert.True(!known.Contains(arrival) || floorFilled, $"Season {season}: player {arrival} joined the managed team without its say.");
            }

            // No expiring player was re-signed on its behalf, unless to fill a roster below its minimum.
            foreach (var spot in mine.Where(spot => spot.ContractSeasonsRemaining == 1))
            {
                Assert.True(!now.Contains(spot.PlayerId) || floorFilled, $"Season {season}: {spot.FullName} was re-signed on the managed team's behalf.");
            }
        }
    }

    [Fact]
    public void AManagedTeamMustBeATeamInTheLeague()
    {
        var session = NewSession();

        Assert.True(session.SetManagedTeam("NOT-A-TEAM").IsFailure);
        Assert.Null(session.ManagedTeamId);
        Assert.True(session.SetManagedTeam(null).IsSuccess);
    }

    private static LeagueSession NewSession()
    {
        var session = new LeagueSession(
            new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 5),
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

public sealed class TradeDeadlineTests
{
    [Fact]
    public void TheAiTradesAtTheDeadline_LeavesTheManagedTeamAlone_ThenNobodyTradesUntilTheSeasonEnds()
    {
        var deals = 0;
        var session = NewSession(1);
        var managedId = session.Overview().Value.Teams[0].TeamId;
        Assert.True(session.SetManagedTeam(managedId).IsSuccess);

        // Three seasons: a league's first has no contenders yet (no record to read), so its deadline
        // is quiet; the deals come once there are buyers and sellers.
        for (var season = 1; season <= 3; season++)
        {
            Assert.True(session.StartSeason(500 + season).IsSuccess);
            var managed = session.Overview().Value.Teams.Single(team => team.TeamId == managedId);
            var mine = managed.Roster.Select(spot => spot.PlayerId).ToHashSet();

            var advance = session.AdvanceToEndOfSeason();
            Assert.True(advance.IsSuccess);
            deals += advance.Value.DeadlineDeals.Count;
            Assert.All(advance.Value.DeadlineDeals, deal => Assert.DoesNotContain(managed.TeamName, deal.Explanation, StringComparison.Ordinal));
            var now = session.Overview().Value.Teams.Single(team => team.TeamId == managed.TeamId).Roster.Select(spot => spot.PlayerId).ToHashSet();
            Assert.True(mine.SetEquals(now), $"Season {season}: the managed team's roster changed through the deadline.");

            // Past the deadline: the person's own trade is refused until the season is over.
            var other = session.Overview().Value.Teams[1];
            var trade = session.AssessTrade(new BallGM.Application.Trades.TradeRequest(
                [managed.TeamId, other.TeamId],
                [
                    new BallGM.Application.Trades.TradeAssetRequest("player", now.First(), managed.TeamId, other.TeamId),
                    new BallGM.Application.Trades.TradeAssetRequest("player", other.Roster[^1].PlayerId, other.TeamId, managed.TeamId),
                ]));
            Assert.True(trade.IsSuccess);
            Assert.Contains(trade.Value.Violations, violation => violation.RuleCode == BallGM.Rules.Trades.TradeValidator.PastTradeDeadlineCode);
            Assert.True(session.ConcludeSeason().IsSuccess);
        }

        Assert.True(deals > 0, "No AI front office made a deadline deal in three seasons.");
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
        Assert.True(session.Load().IsSuccess);
        return session;
    }
}
