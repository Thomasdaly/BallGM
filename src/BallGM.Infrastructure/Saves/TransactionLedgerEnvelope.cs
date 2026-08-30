namespace BallGM.Infrastructure.Saves;

/// <summary>
/// Serialization shape for one <c>TransactionEntry</c>. Every enum travels as a name rather than a
/// number, matching every other envelope in this codebase, so the file stays readable — and stays
/// valid — if the underlying enum gains a member in a later milestone.
/// </summary>
public sealed record TransactionEntryEnvelope(
    string TransactionId,
    long Sequence,
    string RecordedAt,
    string Kind,
    int SeasonYear,
    string? TeamId,
    string? PlayerId,
    string? ContractId,
    long? Amount,
    string Reason,
    string? FranchiseId,
    string? CounterpartyFranchiseId,
    string? DraftPickId,
    string? SigningRoute);

/// <summary>
/// Serialization shape for the whole <c>TransactionLedger</c>: every entry, in the order they were
/// appended. Loading goes through <c>TransactionLedger.Rehydrate</c>, which is the trust boundary — a
/// save whose sequence numbers are not exactly <c>0..N-1</c> in order is refused there, the same way
/// a save asserting an impossible history is refused everywhere else in this codebase.
/// </summary>
public sealed record TransactionLedgerEnvelope(IReadOnlyList<TransactionEntryEnvelope> Entries);
