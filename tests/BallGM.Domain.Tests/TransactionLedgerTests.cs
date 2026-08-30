using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;

namespace BallGM.Domain.Tests;

public sealed class TransactionLedgerTests
{
    private static readonly Season Season2031 = new(2031);
    private static readonly DateTimeOffset Start = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ledger_KeepsEntriesInTheOrderTheyWereAppended()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.FromHours(1)));
        var team = new TeamId(SortableId.NewId());

        ledger.Record(TransactionKind.ContractSigned, Season2031, team, "First signing.");
        ledger.Record(TransactionKind.PlayerReleased, Season2031, team, "A release.");
        ledger.Record(TransactionKind.OptionExercised, Season2031, team, "An option taken up.");

        Assert.Equal([0, 1, 2], ledger.Entries.Select(entry => entry.Sequence));
        Assert.Equal(
            [TransactionKind.ContractSigned, TransactionKind.PlayerReleased, TransactionKind.OptionExercised],
            ledger.Entries.Select(entry => entry.Kind));
    }

    [Fact]
    public void Ledger_StampsEntriesFromTheInjectedClockRatherThanTheMachineClock()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.FromMinutes(30)));
        var team = new TeamId(SortableId.NewId());

        ledger.Record(TransactionKind.ContractSigned, Season2031, team, "First signing.");
        ledger.Record(TransactionKind.ContractSigned, Season2031, team, "Second signing.");

        Assert.Equal(Start, ledger.Entries[0].RecordedAt);
        Assert.Equal(Start.AddMinutes(30), ledger.Entries[1].RecordedAt);
    }

    [Fact]
    public void Ledger_ExposesEntriesThatCannotBeEditedOrRemovedThroughTheReadModel()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.Zero));
        var team = new TeamId(SortableId.NewId());
        ledger.Record(TransactionKind.ContractSigned, Season2031, team, "A signing.");

        // The exposed list is read-only: the only way to change the ledger is to append to it.
        var asList = (IList<TransactionEntry>)ledger.Entries;
        Assert.Throws<NotSupportedException>(() => asList.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => asList.Add(ledger.Entries[0]));
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void Ledger_SeparatesOneTeamsHistoryFromAnothers()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.FromMinutes(5)));
        var team = new TeamId(SortableId.NewId());
        var otherTeam = new TeamId(SortableId.NewId());

        ledger.Record(TransactionKind.ContractSigned, Season2031, team, "Our signing.");
        ledger.Record(TransactionKind.ContractSigned, Season2031, otherTeam, "Their signing.");

        var entry = Assert.Single(ledger.EntriesForTeam(team));
        Assert.Equal("Our signing.", entry.Reason);
    }

    [Fact]
    public void Ledger_RecordsTheMoneyAndThePartiesBehindACapAffectingEvent()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.Zero));
        var team = new TeamId(SortableId.NewId());
        var player = new PlayerId(SortableId.NewId());
        var contract = new ContractId(SortableId.NewId());

        var entry = ledger.Record(
            TransactionKind.PlayerReleased,
            Season2031,
            team,
            "Released; the guaranteed money stays on the books.",
            player,
            contract,
            new Money(6_200_000));

        Assert.Equal(player, entry.PlayerId);
        Assert.Equal(contract, entry.ContractId);
        Assert.Equal(6_200_000, entry.Amount!.SmallestUnits);
        Assert.Equal(Season2031, entry.Season);
    }

    [Fact]
    public void Ledger_KeepsDraftAssetEventsInTheSameSequenceAsCapEvents()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.FromMinutes(10)));
        var team = new TeamId(SortableId.NewId());
        var franchise = new FranchiseId(SortableId.NewId());
        var counterparty = new FranchiseId(SortableId.NewId());
        var pick = new DraftPickId(SortableId.NewId());

        ledger.Record(TransactionKind.ContractSigned, Season2031, team, "A signing.");
        var pickEntry = ledger.RecordPickEvent(
            TransactionKind.DraftPickTransferred,
            Season2031,
            franchise,
            pick,
            "The 2033 first-round pick changed hands.",
            counterparty);

        // One ledger, one sequence: an asset trail kept apart from the money trail is two accounts
        // of the same trade that can disagree.
        Assert.Equal([0, 1], ledger.Entries.Select(entry => entry.Sequence));
        Assert.Equal(franchise, pickEntry.FranchiseId);
        Assert.Equal(counterparty, pickEntry.CounterpartyFranchiseId);
        Assert.Equal(pick, pickEntry.DraftPickId);
        Assert.Null(pickEntry.TeamId);
    }

    [Fact]
    public void Ledger_ReadsBackOneAssetsHistoryAndBothSidesOfAFranchisesTrades()
    {
        var ledger = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.FromMinutes(10)));
        var seller = new FranchiseId(SortableId.NewId());
        var buyer = new FranchiseId(SortableId.NewId());
        var traded = new DraftPickId(SortableId.NewId());
        var untouched = new DraftPickId(SortableId.NewId());

        ledger.RecordPickEvent(TransactionKind.DraftPickTransferred, Season2031, seller, traded, "Traded away.", buyer);
        ledger.RecordPickEvent(TransactionKind.DraftPickEncumbered, Season2031, seller, untouched, "Protected.");

        Assert.Single(ledger.EntriesForPick(traded));
        Assert.Equal(2, ledger.EntriesForFranchise(seller).Count);

        // The receiving franchise sees the trade too: a pick arriving is as much its history as one leaving.
        Assert.Single(ledger.EntriesForFranchise(buyer));
    }

    [Fact]
    public void Entry_RefusesToRecordAnEventThatNamesNeitherATeamNorAFranchise()
    {
        Assert.Throws<ArgumentException>(() => new TransactionEntry(
            new TransactionId(SortableId.NewId()),
            0,
            Start,
            TransactionKind.DraftPickTransferred,
            Season2031,
            teamId: null,
            playerId: null,
            contractId: null,
            amount: null,
            "An event belonging to nobody."));
    }

    [Fact]
    public void Rehydrate_RestoresEntriesWithTheirOriginalIdentitiesAndOrder()
    {
        var original = new TransactionLedger(new SteppingTestClock(Start, TimeSpan.FromMinutes(5)));
        var team = new TeamId(SortableId.NewId());
        original.Record(TransactionKind.ContractSigned, Season2031, team, "First signing.");
        original.Record(TransactionKind.PlayerReleased, Season2031, team, "A release.");

        var rehydrated = TransactionLedger.Rehydrate(new SteppingTestClock(Start, TimeSpan.Zero), original.Entries);

        Assert.True(rehydrated.IsSuccess);
        Assert.Equal(original.Entries, rehydrated.Value.Entries);
    }

    [Fact]
    public void Rehydrate_RefusesEntriesWhoseSequenceIsNotContiguousFromZero()
    {
        var team = new TeamId(SortableId.NewId());
        var entries = new[]
        {
            new TransactionEntry(
                new TransactionId(SortableId.NewId()), 0, Start, TransactionKind.ContractSigned, Season2031, team, null, null, null, "First."),
            new TransactionEntry(
                new TransactionId(SortableId.NewId()), 2, Start, TransactionKind.PlayerReleased, Season2031, team, null, null, null, "Second, but numbered third."),
        };

        var result = TransactionLedger.Rehydrate(new SteppingTestClock(Start, TimeSpan.Zero), entries);

        Assert.True(result.IsFailure);
        Assert.Equal("transaction_ledger.entries_out_of_sequence", Assert.Single(result.Errors).Code);
    }

    private sealed class SteppingTestClock(DateTimeOffset start, TimeSpan step) : IClock
    {
        private long _reads;

        public DateTimeOffset UtcNow => start + (step * _reads++);
    }
}
