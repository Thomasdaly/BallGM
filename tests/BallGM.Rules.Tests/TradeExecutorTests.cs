using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;
using BallGM.Rules.Trades;

namespace BallGM.Rules.Tests;

/// <summary>
/// Execution, and the promise that goes with it: either the whole trade happens or none of it does.
/// Several of these tests assert on a fingerprint of every roster, contract, pick, and ledger length,
/// because "nothing was changed" is only worth claiming if it is checked everywhere.
/// </summary>
public sealed class TradeExecutorTests
{
    [Fact]
    public void Execute_MovesPlayersContractsAndPicksAndWritesTheTradeToTheLedger()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000, 5_000_000)
            .WithTeam("B", 22_000_000, 8_000_000, 4_000_000);

        var movedPlayer = league.PlayerOf("A", 0);
        var movedPick = league.PickOf("B", 2033, 2);

        var result = Execute(
            league,
            league.SendPlayer("A", 0, "B"),
            league.SendPlayer("B", 0, "A"),
            league.SendPick("B", 2033, 2, "A"));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        // Rosters moved.
        Assert.DoesNotContain(movedPlayer, league.TeamOf("A").PlayerIds);
        Assert.Contains(movedPlayer, league.TeamOf("B").PlayerIds);

        // The salary went with the player, so both cap sheets are right afterwards.
        var contract = league.Contracts.Single(candidate => candidate.PlayerId == movedPlayer);
        Assert.Equal(league.TeamOf("B").Id, contract.TeamId);

        var payrollA = Money.Sum(CapChargeProjection
            .ForTeamSeason(league.Contracts, league.TeamOf("A").Id, TradeTestLeague.CurrentSeason)
            .Select(charge => charge.Amount));

        Assert.Equal(37_000_000, payrollA.SmallestUnits);

        // The pick changed franchises.
        Assert.Equal(league.FranchiseOf("A"), league.DraftAssets.Ownership(movedPick)!.CurrentOwnerFranchiseId);

        // Two ledger lines per player (one from each side) plus one per pick.
        Assert.Equal(5, result.Value.LedgerEntries.Count);
        Assert.Contains(league.Ledger.Entries, entry => entry.Kind == TransactionKind.PlayerTraded);
        Assert.Contains(league.Ledger.Entries, entry => entry.Kind == TransactionKind.DraftPickTransferred);
    }

    [Fact]
    public void Execute_LetsATeamOnItsRosterMinimumMakeAOneForOneTrade()
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 2)
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 21_000_000, 10_000_000);

        var result = Execute(league, league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));

        // Both teams are pinned at exactly two players, so this only works because the roster moves
        // as one net change rather than a remove followed by an add.
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(2, league.TeamOf("A").RosterCount);
        Assert.Equal(2, league.TeamOf("B").RosterCount);
    }

    [Fact]
    public void Execute_RefusesAnIllegalTradeAndChangesNothing()
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 5)
            .WithTeam("A", 10_000_000, 9_000_000)
            .WithTeam("B", 10_000_000, 9_000_000);

        var before = league.StateFingerprint();
        var result = Execute(league, league.SendPlayer("A", 0, "B"), league.SendPlayer("A", 1, "B"));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.rejected");
        Assert.Contains(result.Errors, error => error.Code == "trade.roster_minimum_not_met");
        Assert.Equal(before, league.StateFingerprint());
    }

    [Fact]
    public void Execute_RefusesAProposalBuiltAgainstAnOlderStateOfTheLeague()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000);

        var proposal = league.Proposal(league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));

        // Something else happened between agreeing the trade and submitting it.
        league.Ledger.Record(TransactionKind.ContractSigned, TradeTestLeague.CurrentSeason, league.TeamOf("A").Id, "Another signing.");
        var before = league.StateFingerprint();

        var result = new TradeExecutor().Execute(proposal, league.Context());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.stale_proposal");
        Assert.Equal(before, league.StateFingerprint());
    }

    /// <summary>
    /// The rollback case. The league is deliberately left inconsistent — a player on two rosters, as
    /// a corrupt save or a bad data pack could produce — so validation passes and the second roster
    /// change fails partway through. Nothing may survive that.
    /// </summary>
    [Fact]
    public void Execute_UnwindsEveryChangeWhenAStepFailsPartWayThrough()
    {
        var league = TradeTestLeague.Build(minimumRoster: 1, maximumRoster: 5)
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000);

        // The receiving team already has the player it is about to be given.
        var shared = league.PlayerOf("A", 0);
        Assert.True(league.TeamOf("B").ApplyTrade([], [shared]).IsSuccess);

        var before = league.StateFingerprint();
        var result = Execute(league, league.SendPlayer("A", 0, "B"), league.SendPick("A", 2034, 2, "B"));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.execution_rolled_back");
        Assert.Contains(result.Errors, error => error.Code == "roster.player_already_on_team");

        // The sending team's roster, the contract, the pick, and the ledger are all where they were.
        Assert.Equal(before, league.StateFingerprint());
        Assert.Contains(shared, league.TeamOf("A").PlayerIds);
        Assert.Equal(league.FranchiseOf("A"), league.DraftAssets.Ownership(league.PickOf("A", 2034, 2))!.CurrentOwnerFranchiseId);
        Assert.Empty(league.Ledger.Entries);
    }

    [Fact]
    public void Execute_AppliesAThreeTeamTradeInOnePiece()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 21_000_000, 10_000_000)
            .WithTeam("C", 22_000_000, 10_000_000);

        var fromA = league.PlayerOf("A", 0);
        var fromB = league.PlayerOf("B", 0);
        var fromC = league.PlayerOf("C", 0);

        var result = Execute(
            league,
            league.SendPlayer("A", 0, "B"),
            league.SendPlayer("B", 0, "C"),
            league.SendPlayer("C", 0, "A"));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains(fromA, league.TeamOf("B").PlayerIds);
        Assert.Contains(fromB, league.TeamOf("C").PlayerIds);
        Assert.Contains(fromC, league.TeamOf("A").PlayerIds);
        Assert.All(new[] { "A", "B", "C" }, key => Assert.Equal(2, league.TeamOf(key).RosterCount));
    }

    [Fact]
    public void Execute_LeavesTheSecondAttemptAtTheSameTradeStaleRatherThanDoingItTwice()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000);

        var proposal = league.Proposal(league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));
        var executor = new TradeExecutor();

        Assert.True(executor.Execute(proposal, league.Context()).IsSuccess);

        // The trade wrote itself to the ledger, so its own proposal is now out of date — which is
        // exactly what stops a double-click from executing a trade twice.
        var second = executor.Execute(proposal, league.Context());

        Assert.True(second.IsFailure);
        Assert.Contains(second.Errors, error => error.Code == "trade.stale_proposal");
        Assert.Equal(2, league.TeamOf("A").RosterCount);
    }

    private static DomainOperationResult<TradeExecution> Execute(
        TradeTestLeague league,
        params TradeAssetMovement[] movements) =>
        new TradeExecutor().Execute(league.Proposal(movements), league.Context());
}
