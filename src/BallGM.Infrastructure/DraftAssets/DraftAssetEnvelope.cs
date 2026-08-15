namespace BallGM.Infrastructure.DraftAssets;

/// <summary>
/// Flat, primitive-only serialization shape for a league's draft assets — the save and data-pack
/// surface for everything Milestone 4 added. Versioned from the first commit, like
/// <c>ContractEnvelope</c>: a pick owed three drafts out is exactly the kind of state a player would
/// lose a franchise's plan to, so a schema change has to be a visible, migratable event.
/// <para>
/// Carries no validation of its own. <see cref="DraftAssetSerializer"/> is the trust boundary — it
/// maps this DTO onto the domain types, where the invariants live.
/// </para>
/// </summary>
public sealed record DraftAssetBookEnvelope(
    int SchemaVersion,
    string LeagueId,
    IReadOnlyList<DraftPickEnvelope> Picks)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// One pick: its immutable identity, then its mutable ownership state. The two halves stay visibly
/// separate in the file for the same reason they are separate types — the original franchise is not
/// a stale copy of the current owner.
/// </summary>
public sealed record DraftPickEnvelope(
    string PickId,
    int DraftSeasonYear,
    int Round,
    string OriginalFranchiseId,
    string CurrentOwnerFranchiseId,
    PickObligationEnvelope? Obligation,
    SwapRightEnvelope? SwapRight);

/// <summary>
/// A promise to convey, with its protection schedule written out as levels plus a named fallback.
/// <see cref="ScheduleIndex"/> is what makes a half-rolled-over obligation survive a save: without
/// it, a pick that has already used up one year of its protection would reload as a fresh one.
/// </summary>
public sealed record PickObligationEnvelope(
    string EncumbranceId,
    string BeneficiaryFranchiseId,
    IReadOnlyList<int> ProtectedSelections,
    string FallbackKind,
    int? ConvertsToRound,
    int ScheduleIndex);

public sealed record SwapRightEnvelope(
    string EncumbranceId,
    string HolderFranchiseId,
    string CounterpartPickId);
