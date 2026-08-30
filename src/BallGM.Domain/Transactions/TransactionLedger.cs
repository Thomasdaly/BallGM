using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Transactions;

/// <summary>
/// The append-only record of every cap-affecting event. There is no update and no delete: a payroll
/// figure that changed without a line here is a bug, because the ledger is the only account of
/// <em>why</em> a team's books look the way they do.
/// <para>
/// Timestamps come from an injected <see cref="IClock"/> and identifiers from
/// <see cref="SortableId"/>, so a fixture or a test produces the same ledger every run.
/// </para>
/// </summary>
public sealed class TransactionLedger
{
    private const string OutOfSequenceCode = "transaction_ledger.entries_out_of_sequence";

    private readonly List<TransactionEntry> _entries = [];
    private readonly IClock _clock;

    public TransactionLedger(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>
    /// Rebuilds a ledger from entries a save already has fully formed — their own identifiers,
    /// sequence numbers, and timestamps intact, unlike every other way onto this type, which mints
    /// all three fresh. Refuses a file whose sequence is not exactly <c>0..N-1</c> in order, the same
    /// way a save asserting an impossible history is refused everywhere else in this codebase: a
    /// ledger is the audit trail everything else is explained against, so a corrupt one is refused at
    /// the boundary rather than trusted and read from later.
    /// <para>
    /// <paramref name="clock"/> is for whatever this session appends next, not for anything already
    /// in <paramref name="entries"/> — <see cref="TransactionEntry.Sequence"/>, not the timestamp, is
    /// what everything else in the ledger orders by.
    /// </para>
    /// </summary>
    public static DomainOperationResult<TransactionLedger> Rehydrate(IClock clock, IEnumerable<TransactionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries.ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Sequence != index)
            {
                return DomainOperationResult<TransactionLedger>.Failure(new DomainError(
                    OutOfSequenceCode,
                    $"Ledger entry at position {index} declares sequence {ordered[index].Sequence}. A ledger's entries must be numbered 0..{ordered.Count - 1} in order."));
            }
        }

        var ledger = new TransactionLedger(clock);
        ledger._entries.AddRange(ordered);
        return DomainOperationResult<TransactionLedger>.Success(ledger);
    }

    /// <summary>
    /// Entries in the order they were appended, as a read-only view — a caller who casts this back
    /// to a list still cannot edit or remove anything. Appending is the only way in.
    /// </summary>
    public IReadOnlyList<TransactionEntry> Entries => _entries.AsReadOnly();

    public int Count => _entries.Count;

    /// <summary>
    /// Appends an entry and returns it. Recording cannot fail on a business rule — the rule check
    /// happens before the event does — so this returns the entry rather than a result type.
    /// </summary>
    public TransactionEntry Record(
        TransactionKind kind,
        Season season,
        TeamId teamId,
        string reason,
        PlayerId? playerId = null,
        ContractId? contractId = null,
        Money? amount = null)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var timestamp = _clock.UtcNow;
        var entry = new TransactionEntry(
            new TransactionId(SortableId.NewId(timestamp)),
            _entries.Count,
            timestamp,
            kind,
            season,
            teamId,
            playerId,
            contractId,
            amount,
            reason);

        _entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Appends a signing, naming the route that paid for it. Separate from <see cref="Record"/> only
    /// because the route matters: how much of a fixed allowance a team has left this season is
    /// derived by reading these entries back, never by keeping a running total somewhere that a
    /// rolled-back transaction could leave wrong.
    /// </summary>
    public TransactionEntry RecordSigning(
        Season season,
        TeamId teamId,
        PlayerId playerId,
        ContractId contractId,
        Money amount,
        SigningRouteKind route,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(contractId);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var timestamp = _clock.UtcNow;
        var entry = new TransactionEntry(
            new TransactionId(SortableId.NewId(timestamp)),
            _entries.Count,
            timestamp,
            TransactionKind.ContractSigned,
            season,
            teamId,
            playerId,
            contractId,
            amount,
            reason,
            franchiseId: null,
            counterpartyFranchiseId: null,
            draftPickId: null,
            signingRoute: route);

        _entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Appends a draft-asset event. Same ledger, same sequence, new kinds: a pick's history and a
    /// payroll's history are the same audit trail, and splitting them would let a trade appear in
    /// one account and not the other.
    /// </summary>
    public TransactionEntry RecordPickEvent(
        TransactionKind kind,
        Season season,
        FranchiseId franchiseId,
        DraftPickId draftPickId,
        string reason,
        FranchiseId? counterpartyFranchiseId = null)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(franchiseId);
        ArgumentNullException.ThrowIfNull(draftPickId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var timestamp = _clock.UtcNow;
        var entry = new TransactionEntry(
            new TransactionId(SortableId.NewId(timestamp)),
            _entries.Count,
            timestamp,
            kind,
            season,
            teamId: null,
            playerId: null,
            contractId: null,
            amount: null,
            reason,
            franchiseId,
            counterpartyFranchiseId,
            draftPickId);

        _entries.Add(entry);
        return entry;
    }

    public IReadOnlyList<TransactionEntry> EntriesForTeam(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _entries.Where(entry => entry.TeamId == teamId).ToList();
    }

    /// <summary>
    /// Everything that happened to a franchise, including events where it was the other side of the
    /// trade — a pick leaving is as much a part of a franchise's history as one arriving.
    /// </summary>
    public IReadOnlyList<TransactionEntry> EntriesForFranchise(FranchiseId franchiseId)
    {
        ArgumentNullException.ThrowIfNull(franchiseId);
        return _entries
            .Where(entry => entry.FranchiseId == franchiseId || entry.CounterpartyFranchiseId == franchiseId)
            .ToList();
    }

    /// <summary>One asset's whole history — the drill-down behind a cell on the pick-ownership board.</summary>
    public IReadOnlyList<TransactionEntry> EntriesForPick(DraftPickId draftPickId)
    {
        ArgumentNullException.ThrowIfNull(draftPickId);
        return _entries.Where(entry => entry.DraftPickId == draftPickId).ToList();
    }
}
