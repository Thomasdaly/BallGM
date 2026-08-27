using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Transactions;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

/// <summary>
/// Execution. Either the player is signed, the roster moved, and the ledger written — or nothing
/// happened at all and the league is byte-for-byte where it started.
/// </summary>
public sealed class SigningExecutorTests
{
    private static readonly SigningExecutor Executor = new();

    [Fact]
    public void ALegalSigningProducesAContractARosterSpotAndOneLedgerLine()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);
        var rosterBefore = league.Team.RosterCount;

        var result = Executor.Execute(league.Offer(20_000_000, seasons: 3), league.Context());

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var execution = result.Value;
        Assert.Equal(rosterBefore + 1, league.Team.RosterCount);
        Assert.Contains(league.FreeAgent.Id, league.Team.PlayerIds);
        Assert.Equal(league.FreeAgent.Id, execution.Contract.PlayerId);
        Assert.Equal(league.Team.Id, execution.Contract.TeamId);
        Assert.Equal(3, execution.Contract.Terms.Count);
        Assert.Equal(1, execution.LedgerEntryCount);
    }

    /// <summary>
    /// The route is recorded against the transaction. A ledger that says a contract was signed but
    /// not what permitted it cannot answer the question a GM asks next — how much allowance is left —
    /// and that figure is derived by reading these entries back rather than from stored state.
    /// </summary>
    [Fact]
    public void TheLedgerRecordsWhichRoutePaidForTheSigning()
    {
        var league = SigningTestLeague.Build([80_000_000, 30_000_000, 15_000_000]);

        var execution = Executor.Execute(league.Offer(10_000_000), league.Context()).Value;
        var entry = Assert.Single(league.Ledger.EntriesForTeam(league.Team.Id));

        Assert.Equal(TransactionKind.ContractSigned, entry.Kind);
        Assert.Equal(SigningRouteKind.StandardOverCapAllowance, entry.SigningRoute);
        Assert.Equal(SigningRouteKind.StandardOverCapAllowance, execution.Route);
        Assert.Equal(10_000_000, entry.Amount!.SmallestUnits);
        Assert.Equal(league.FreeAgent.Id, entry.PlayerId);
    }

    /// <summary>
    /// A refused signing leaves nothing behind — no roster spot, no contract, and above all no ledger
    /// line, because an entry describing something that did not happen is worse than no entry.
    /// </summary>
    [Fact]
    public void ARefusedSigningChangesNothingAndWritesNoLedgerLine()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);
        var rosterBefore = league.Team.PlayerIds.ToArray();

        var result = Executor.Execute(league.Offer(45_000_000), league.Context());

        Assert.True(result.IsFailure);
        Assert.Equal(SigningExecutor.RejectedCode, result.Errors[0].Code);
        Assert.Equal(rosterBefore, league.Team.PlayerIds);
        Assert.Equal(0, league.Ledger.Count);
    }

    /// <summary>
    /// Execution re-validates rather than trusting anything it was handed. Signing the same player
    /// twice is the cheapest demonstration: the second attempt meets a player who is now under
    /// contract, and is refused for exactly that.
    /// </summary>
    [Fact]
    public void ExecutingTheSameOfferTwiceIsRefusedTheSecondTimeRatherThanDoneTwice()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);
        var offer = league.Offer(20_000_000);

        var first = Executor.Execute(offer, league.Context());
        Assert.True(first.IsSuccess, string.Join("; ", first.Errors.Select(error => error.Message)));

        // The session adds the new contract to the league it holds; the rules layer sees it next time.
        var afterSigning = league.Context() with { Contracts = [.. league.Contracts, first.Value.Contract] };

        var second = Executor.Execute(offer, afterSigning);

        Assert.True(second.IsFailure);
        Assert.Contains(second.Errors, error => error.Code == SigningValidator.AlreadyUnderContractCode);
        Assert.Equal(1, league.Ledger.Count);
    }

    /// <summary>
    /// A signing that takes a short-handed team up to the roster minimum has to be undoable, which is
    /// why the undo restores the roster wholesale rather than calling the rule-checked remove — that
    /// operation would refuse to take the team back below the line it just reached.
    /// </summary>
    [Fact]
    public void ASigningThatReachesTheRosterMinimumStillLeavesTheLeagueUntouchedWhenItIsRefused()
    {
        var league = SigningTestLeague.Build([10_000_000, 10_000_000], minimumRoster: 3);
        var rosterBefore = league.Team.PlayerIds.ToArray();

        // Above this player's ceiling, so the offer is refused after the roster arithmetic has run.
        var result = Executor.Execute(league.Offer(40_000_000), league.Context());

        Assert.True(result.IsFailure);
        Assert.Equal(rosterBefore, league.Team.PlayerIds);
        Assert.Equal(2, league.Team.RosterCount);
        Assert.Equal(0, league.Ledger.Count);
    }

    /// <summary>
    /// Filling a roster spot releases the hold that was reserving it, so the payroll rises by the
    /// contract less the hold rather than by the whole contract.
    /// </summary>
    [Fact]
    public void SigningIntoAnUnfilledSpotReleasesTheHoldThatWasReservingIt()
    {
        var league = SigningTestLeague.Build([10_000_000, 10_000_000], minimumRoster: 4);

        var assessment = new SigningValidator().Validate(league.Offer(5_000_000), league.Context()).Value;

        // Two contracts of 10m plus two 1m holds is 22m. After the signing it is 25m of contracts
        // plus one remaining 1m hold — 26m, not the 27m that charging for both would produce.
        Assert.Equal(22_000_000, assessment.PayrollBefore.SmallestUnits);
        Assert.Equal(26_000_000, assessment.PayrollAfter.SmallestUnits);
    }
}
