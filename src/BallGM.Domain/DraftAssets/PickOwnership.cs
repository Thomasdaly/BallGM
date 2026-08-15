using BallGM.Domain.Common;
using BallGM.Domain.Franchises;

namespace BallGM.Domain.DraftAssets;

/// <summary>
/// The mutable half of a draft asset: who controls one pick right now, and what is riding on it.
/// Paired one-to-one with a <see cref="DraftPick"/> identity that never changes.
/// <para>
/// Duplicate current ownership is impossible by construction rather than by convention:
/// <see cref="CurrentOwnerFranchiseId"/> is a single field on a single instance, and
/// <see cref="DraftAssetBook"/> holds exactly one instance per pick. There is no collection of
/// owners for two entries to appear in, so "both teams think they own it" is not a state this model
/// can represent.
/// </para>
/// </summary>
public sealed class PickOwnership
{
    private const string SameOwnerCode = "pick_ownership.already_owned_by_franchise";
    private const string DuplicateEncumbranceCode = "pick_ownership.duplicate_encumbrance";
    private const string ConflictingObligationCode = "pick_ownership.conflicting_obligation";
    private const string ConflictingSwapCode = "pick_ownership.conflicting_swap_right";
    private const string UnknownEncumbranceCode = "pick_ownership.unknown_encumbrance";

    private readonly List<PickEncumbrance> _encumbrances = [];

    private PickOwnership(DraftPickId pickId, FranchiseId currentOwnerFranchiseId)
    {
        PickId = pickId;
        CurrentOwnerFranchiseId = currentOwnerFranchiseId;
    }

    /// <summary>
    /// Starts an asset's ownership record. A pick begins under the control of whichever franchise
    /// the league assigns it to — normally the original franchise, though a data pack describing a
    /// league mid-history can legitimately start it somewhere else.
    /// </summary>
    public static DomainOperationResult<PickOwnership> Create(DraftPickId pickId, FranchiseId currentOwnerFranchiseId)
    {
        ArgumentNullException.ThrowIfNull(pickId);
        ArgumentNullException.ThrowIfNull(currentOwnerFranchiseId);

        return DomainOperationResult<PickOwnership>.Success(new PickOwnership(pickId, currentOwnerFranchiseId));
    }

    public DraftPickId PickId { get; }

    public FranchiseId CurrentOwnerFranchiseId { get; private set; }

    public IReadOnlyList<PickEncumbrance> Encumbrances => _encumbrances.AsReadOnly();

    /// <summary>The pending promise to hand this pick over, if there is one. There is never more than one.</summary>
    public PickObligation? Obligation => _encumbrances.OfType<PickObligation>().SingleOrDefault();

    public SwapRight? PendingSwap => _encumbrances.OfType<SwapRight>().SingleOrDefault();

    public bool IsEncumbered => _encumbrances.Count > 0;

    public DomainOperationResult TransferTo(FranchiseId franchiseId)
    {
        ArgumentNullException.ThrowIfNull(franchiseId);

        if (franchiseId == CurrentOwnerFranchiseId)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    SameOwnerCode,
                    $"Pick '{PickId.Value}' is already controlled by franchise '{franchiseId.Value}'."));
        }

        CurrentOwnerFranchiseId = franchiseId;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Attaches an encumbrance. A pick carries at most one conveyance obligation and at most one
    /// swap right: a second of either is a conflicting claim on the same asset, which is a rule
    /// failure rather than a silently appended row. An obligation and a swap right may coexist —
    /// that pairing is real, and it is exactly what makes the resolution order a decision.
    /// </summary>
    public DomainOperationResult Encumber(PickEncumbrance encumbrance)
    {
        ArgumentNullException.ThrowIfNull(encumbrance);

        if (_encumbrances.Any(existing => existing.Id == encumbrance.Id))
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    DuplicateEncumbranceCode,
                    $"Encumbrance '{encumbrance.Id.Value}' is already recorded against pick '{PickId.Value}'."));
        }

        switch (encumbrance)
        {
            case PickObligation when Obligation is not null:
                return DomainOperationResult.Failure(
                    new DomainError(
                        ConflictingObligationCode,
                        $"Pick '{PickId.Value}' already carries a conveyance obligation and cannot be promised to a second franchise."));

            case SwapRight when PendingSwap is not null:
                return DomainOperationResult.Failure(
                    new DomainError(
                        ConflictingSwapCode,
                        $"Pick '{PickId.Value}' already carries a swap right and cannot carry a second one."));
        }

        _encumbrances.Add(encumbrance);
        return DomainOperationResult.Success;
    }

    public DomainOperationResult Release(PickEncumbranceId encumbranceId)
    {
        ArgumentNullException.ThrowIfNull(encumbranceId);

        var removed = _encumbrances.RemoveAll(encumbrance => encumbrance.Id == encumbranceId);
        return removed == 0
            ? DomainOperationResult.Failure(
                new DomainError(
                    UnknownEncumbranceCode,
                    $"Encumbrance '{encumbranceId.Value}' is not recorded against pick '{PickId.Value}'."))
            : DomainOperationResult.Success;
    }
}
