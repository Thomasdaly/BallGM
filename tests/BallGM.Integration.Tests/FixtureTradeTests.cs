using BallGM.Application.Leagues;
using BallGM.Application.Trades;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// Trades against the shipped fixture, through the real session, the real rules, and the real
/// ruleset file — the same path the client takes when a human presses the button.
/// </summary>
public sealed class FixtureTradeTests
{
    /// <summary>The two lowest-payroll teams in the shipped spread, so an even swap has room to be legal.</summary>
    private const string UnderCapTeam = "Old Foundry Bellringers";
    private const string AtCapTeam = "Northreach Aurora";

    /// <summary>The team above the second apron, which the ruleset bars from taking on net salary.</summary>
    private const string AboveApronTeam = "Harbourline Tidewatch";

    [Fact]
    public void EvenSwap_BetweenTeamsWithRoomIsLegalAndExecutes()
    {
        var session = NewSession(out var overview);
        var sending = TeamNamed(overview, UnderCapTeam);
        var receiving = TeamNamed(overview, AtCapTeam);

        var outgoing = CheapestPlayer(sending);
        var incoming = CheapestPlayer(receiving);

        var request = Swap(sending, outgoing, receiving, incoming);
        var assessment = session.AssessTrade(request);

        Assert.True(assessment.IsSuccess, string.Join("; ", assessment.Errors.Select(error => error.Message)));
        Assert.True(assessment.Value.IsLegal, string.Join("; ", assessment.Value.Violations.Select(v => v.Explanation)));

        var submission = session.SubmitTrade(request);
        Assert.True(submission.IsSuccess, string.Join("; ", submission.Errors.Select(error => error.Message)));

        // Both rosters moved, and the read model says so without anything being reloaded from disk.
        var after = submission.Value.Overview;
        Assert.Contains(TeamNamed(after, AtCapTeam).Roster, spot => spot.PlayerId == outgoing.PlayerId);
        Assert.Contains(TeamNamed(after, UnderCapTeam).Roster, spot => spot.PlayerId == incoming.PlayerId);
        Assert.DoesNotContain(TeamNamed(after, UnderCapTeam).Roster, spot => spot.PlayerId == outgoing.PlayerId);

        // The salary went with the players: each team's payroll moved by the difference between them.
        var payrollBefore = TeamNamed(overview, UnderCapTeam).CapSheet.TotalPayroll;
        var payrollAfter = TeamNamed(after, UnderCapTeam).CapSheet.TotalPayroll;
        Assert.Equal(payrollBefore - outgoing.CapCharge + incoming.CapCharge, payrollAfter);

        // Four ledger lines: one per player from each side.
        Assert.Equal(4, submission.Value.LedgerEntryCount);
        Assert.Contains(
            TeamNamed(after, UnderCapTeam).CapSheet.Transactions,
            line => line.Kind == "Player traded");
    }

    [Fact]
    public void PickTrade_MovesTheAssetOntoTheOtherFranchisesBoard()
    {
        var session = NewSession(out var overview);
        var sending = TeamNamed(overview, UnderCapTeam);
        var receiving = TeamNamed(overview, AtCapTeam);

        // A second-round pick, so the retained-first-round restriction is not what is under test.
        var pick = PicksOwnedBy(overview, sending.FranchiseName).First(asset => asset.Round == 2);

        var request = new TradeRequest(
            [sending.TeamId, receiving.TeamId],
            [new TradeAssetRequest(TradeAssetRequest.PickKind, pick.PickId, sending.TeamId, receiving.TeamId)]);

        var submission = session.SubmitTrade(request);

        Assert.True(submission.IsSuccess, string.Join("; ", submission.Errors.Select(error => error.Message)));

        var after = submission.Value.Overview;
        var acquired = PicksOwnedBy(after, receiving.FranchiseName).Single(asset => asset.PickId == pick.PickId);

        Assert.Equal("Acquired", acquired.State);
        Assert.Contains(acquired.History, line => line.Kind == "Pick traded");
    }

    [Fact]
    public void SalaryDump_OntoTheTeamAboveTheSecondApronIsRejectedWithItsRuleCode()
    {
        var session = NewSession(out var overview);
        var apronTeam = TeamNamed(overview, AboveApronTeam);
        var otherTeam = TeamNamed(overview, UnderCapTeam);

        // The apron team sends its cheapest player and takes back the other team's most expensive.
        var outgoing = CheapestPlayer(apronTeam);
        var incoming = otherTeam.Roster.MaxBy(spot => spot.CapCharge)!;

        var request = Swap(apronTeam, outgoing, otherTeam, incoming);
        var assessment = session.AssessTrade(request);

        Assert.True(assessment.IsSuccess);
        Assert.False(assessment.Value.IsLegal);
        Assert.Contains(
            assessment.Value.Violations,
            violation => violation.RuleCode is "trade.second_apron_salary_increase" or "trade.salary_not_matched");

        // Every violation names the team it belongs to, so the screen can put it in the right column.
        Assert.All(assessment.Value.Violations, violation => Assert.NotNull(violation.TeamName));
    }

    [Fact]
    public void RejectedTrade_LeavesEveryRosterAndPayrollExactlyWhereItWas()
    {
        var session = NewSession(out var overview);
        var apronTeam = TeamNamed(overview, AboveApronTeam);
        var otherTeam = TeamNamed(overview, UnderCapTeam);

        var request = Swap(
            apronTeam,
            CheapestPlayer(apronTeam),
            otherTeam,
            otherTeam.Roster.MaxBy(spot => spot.CapCharge)!);

        var submission = session.SubmitTrade(request);
        Assert.True(submission.IsFailure);

        var after = session.Overview().Value;

        Assert.Equal(
            overview.Teams.Select(team => $"{team.TeamName}:{team.RosterCount}:{team.CapSheet.TotalPayroll}"),
            after.Teams.Select(team => $"{team.TeamName}:{team.RosterCount}:{team.CapSheet.TotalPayroll}"));
    }

    [Fact]
    public void AssessingATradeManyTimesOverChangesNothing()
    {
        var session = NewSession(out var overview);
        var sending = TeamNamed(overview, UnderCapTeam);
        var receiving = TeamNamed(overview, AtCapTeam);

        var request = Swap(sending, CheapestPlayer(sending), receiving, CheapestPlayer(receiving));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(session.AssessTrade(request).IsSuccess);
        }

        var after = session.Overview().Value;
        Assert.Equal(
            overview.Teams.Select(team => $"{team.TeamName}:{team.RosterCount}:{team.CapSheet.TotalPayroll}"),
            after.Teams.Select(team => $"{team.TeamName}:{team.RosterCount}:{team.CapSheet.TotalPayroll}"));
    }

    [Fact]
    public void ResubmittingTheSameTradeIsRefusedAsStaleRatherThanDoneTwice()
    {
        var session = NewSession(out var overview);
        var sending = TeamNamed(overview, UnderCapTeam);
        var receiving = TeamNamed(overview, AtCapTeam);

        var request = Swap(sending, CheapestPlayer(sending), receiving, CheapestPlayer(receiving));

        Assert.True(session.SubmitTrade(request).IsSuccess);

        // The identifiers in the request are now on the other teams, so the second attempt cannot
        // pass ownership — the trade does not silently happen a second time.
        var second = session.SubmitTrade(request);

        Assert.True(second.IsFailure);
        Assert.Contains(second.Errors, error => error.Code == "trade.player_not_on_team");
    }

    private static TradeRequest Swap(
        TeamSummary sending,
        RosterSpot outgoing,
        TeamSummary receiving,
        RosterSpot incoming) =>
        new(
            [sending.TeamId, receiving.TeamId],
            [
                new TradeAssetRequest(TradeAssetRequest.PlayerKind, outgoing.PlayerId, sending.TeamId, receiving.TeamId),
                new TradeAssetRequest(TradeAssetRequest.PlayerKind, incoming.PlayerId, receiving.TeamId, sending.TeamId),
            ]);

    private static RosterSpot CheapestPlayer(TeamSummary team) =>
        team.Roster.Where(spot => spot.CapCharge > 0).MinBy(spot => spot.CapCharge)!;

    private static IReadOnlyList<PickAssetSummary> PicksOwnedBy(LeagueOverview overview, string franchiseName) =>
        overview.PickBoard.Franchises
            .Single(row => row.FranchiseName == franchiseName)
            .Drafts
            .SelectMany(cell => cell.Assets)
            .Where(asset => asset.State is "Own" or "Acquired")
            .ToList();

    private static TeamSummary TeamNamed(LeagueOverview overview, string teamName) =>
        overview.Teams.Single(team => team.TeamName == teamName);

    private static LeagueSession NewSession(out LeagueOverview overview)
    {
        var session = new LeagueSession(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine());

        var result = session.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        overview = result.Value;
        return session;
    }
}
