using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.DraftAssets;

/// <summary>
/// What a franchise may legally do with a draft asset. The trade engine (Milestone 5) calls this
/// before it moves anything, in the same way it will call the cap ledger before it moves salary —
/// this milestone builds the surface, not the execution.
/// <para>
/// Every answer is a rule code plus a sentence, because "you cannot trade that pick" is not a
/// usable explanation on its own.
/// </para>
/// </summary>
public sealed class PickOwnershipRules
{
    private const string UnknownPickCode = "pick_transfer.unknown_pick";
    private const string NotControlledCode = "pick_transfer.not_controlled";
    private const string SameOwnerCode = "pick_transfer.already_owned";
    private const string OutsideHorizonCode = "pick_transfer.outside_tradable_horizon";
    private const string EncumberedCode = "pick_transfer.conflicting_encumbrance";
    private const string RetentionCode = "pick_transfer.retained_round_restriction";

    /// <summary>
    /// Checks one franchise handing one pick to another. Deliberately does not execute the transfer:
    /// a validator that mutates is a validator nobody can call speculatively, and the trade engine
    /// needs to ask "would this be legal" while a proposal is still being assembled.
    /// </summary>
    public DomainOperationResult ValidateTransfer(
        DraftAssetBook book,
        DraftPickId pickId,
        FranchiseId fromFranchiseId,
        FranchiseId toFranchiseId,
        Season currentSeason,
        DraftRules draftRules)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(pickId);
        ArgumentNullException.ThrowIfNull(fromFranchiseId);
        ArgumentNullException.ThrowIfNull(toFranchiseId);
        ArgumentNullException.ThrowIfNull(currentSeason);
        ArgumentNullException.ThrowIfNull(draftRules);

        var pick = book.Pick(pickId);
        var ownership = book.Ownership(pickId);
        if (pick is null || ownership is null)
        {
            return Fail(UnknownPickCode, $"Pick '{pickId.Value}' is not a registered draft asset in this league.");
        }

        var errors = new List<DomainError>();

        if (ownership.CurrentOwnerFranchiseId != fromFranchiseId)
        {
            errors.Add(new DomainError(
                NotControlledCode,
                $"Franchise '{fromFranchiseId.Value}' cannot trade the {pick.DraftSeason.Year} round {pick.Round} pick originally belonging to '{pick.OriginalFranchiseId.Value}': it is controlled by '{ownership.CurrentOwnerFranchiseId.Value}'."));
        }
        else if (toFranchiseId == fromFranchiseId)
        {
            errors.Add(new DomainError(
                SameOwnerCode,
                $"The {pick.DraftSeason.Year} round {pick.Round} pick is already controlled by franchise '{toFranchiseId.Value}'."));
        }

        var lastTradableYear = currentSeason.Year + draftRules.TradableFutureDraftHorizon;
        if (pick.DraftSeason.Year < currentSeason.Year || pick.DraftSeason.Year > lastTradableYear)
        {
            errors.Add(new DomainError(
                OutsideHorizonCode,
                $"The {pick.DraftSeason.Year} draft is outside the tradable window, which runs from the {currentSeason.Year} draft through the {lastTradableYear} draft under this ruleset."));
        }

        // A pick already promised to a third party cannot be handed to a second one: the receiving
        // franchise would be taking an asset that may never arrive, and the board would show two
        // franchises with a claim on the same selection.
        if (ownership.Obligation is not null)
        {
            errors.Add(new DomainError(
                EncumberedCode,
                $"The {pick.DraftSeason.Year} round {pick.Round} pick already carries a conveyance obligation to franchise '{ownership.Obligation.BeneficiaryFranchiseId.Value}' and cannot be traded until that obligation resolves."));
        }

        errors.AddRange(RetentionViolations(book, pick, fromFranchiseId, currentSeason, draftRules));

        return errors.Count > 0
            ? DomainOperationResult.Failure(errors.ToArray())
            : DomainOperationResult.Success;
    }

    /// <summary>
    /// Checks attaching a protection or a swap right. Separate from
    /// <see cref="ValidateTransfer"/> because encumbering and transferring fail for different
    /// reasons: a franchise can legally promise a pick it could not legally hand over today.
    /// </summary>
    public DomainOperationResult ValidateEncumbrance(
        DraftAssetBook book,
        DraftPickId pickId,
        FranchiseId encumberingFranchiseId,
        PickEncumbrance encumbrance,
        Season currentSeason,
        DraftRules draftRules)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(pickId);
        ArgumentNullException.ThrowIfNull(encumberingFranchiseId);
        ArgumentNullException.ThrowIfNull(encumbrance);
        ArgumentNullException.ThrowIfNull(currentSeason);
        ArgumentNullException.ThrowIfNull(draftRules);

        var pick = book.Pick(pickId);
        var ownership = book.Ownership(pickId);
        if (pick is null || ownership is null)
        {
            return Fail(UnknownPickCode, $"Pick '{pickId.Value}' is not a registered draft asset in this league.");
        }

        var errors = new List<DomainError>();

        if (ownership.CurrentOwnerFranchiseId != encumberingFranchiseId)
        {
            errors.Add(new DomainError(
                NotControlledCode,
                $"Franchise '{encumberingFranchiseId.Value}' cannot encumber the {pick.DraftSeason.Year} round {pick.Round} pick: it is controlled by '{ownership.CurrentOwnerFranchiseId.Value}'."));
        }

        var lastTradableYear = currentSeason.Year + draftRules.TradableFutureDraftHorizon;
        if (pick.DraftSeason.Year < currentSeason.Year || pick.DraftSeason.Year > lastTradableYear)
        {
            errors.Add(new DomainError(
                OutsideHorizonCode,
                $"The {pick.DraftSeason.Year} draft is outside the tradable window, which runs from the {currentSeason.Year} draft through the {lastTradableYear} draft under this ruleset."));
        }

        switch (encumbrance)
        {
            case PickObligation when ownership.Obligation is not null:
                errors.Add(new DomainError(
                    EncumberedCode,
                    $"The {pick.DraftSeason.Year} round {pick.Round} pick is already promised to franchise '{ownership.Obligation.BeneficiaryFranchiseId.Value}'."));
                break;

            case SwapRight swap when ownership.PendingSwap is not null:
                errors.Add(new DomainError(
                    EncumberedCode,
                    $"The {pick.DraftSeason.Year} round {pick.Round} pick already carries a swap right held by franchise '{ownership.PendingSwap.HolderFranchiseId.Value}'; pick '{swap.CounterpartPickId.Value}' cannot be swapped against it as well."));
                break;

            case SwapRight swap when book.Pick(swap.CounterpartPickId) is null:
                errors.Add(new DomainError(
                    UnknownPickCode,
                    $"The swap right names counterpart pick '{swap.CounterpartPickId.Value}', which is not a registered draft asset."));
                break;
        }

        // A promise is a future transfer, so it is held to the same retention restriction. A
        // franchise cannot get around "keep a first" by owing every one of them away instead.
        if (encumbrance is PickObligation)
        {
            errors.AddRange(RetentionViolations(book, pick, encumberingFranchiseId, currentSeason, draftRules));
        }

        return errors.Count > 0
            ? DomainOperationResult.Failure(errors.ToArray())
            : DomainOperationResult.Success;
    }

    /// <summary>
    /// The configured consecutive-future-draft restriction: across every run of
    /// <see cref="DraftRules.RetainedRoundInterval"/> consecutive future drafts inside the tradable
    /// horizon, a franchise must still control its own pick in
    /// <see cref="DraftRules.RetainedRoundNumber"/> at least once.
    /// <para>
    /// A pick carrying a pending obligation does not count as retained. That is a decision worth
    /// stating: a protected pick <em>might</em> stay, but a rule that lets a franchise satisfy a
    /// retention requirement with an asset it may lose is not a retention requirement.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DomainError> RetentionViolations(
        DraftAssetBook book,
        DraftPick departingPick,
        FranchiseId franchiseId,
        Season currentSeason,
        DraftRules draftRules)
    {
        // Only a franchise's own pick in the retained round can breach the restriction. Picks
        // acquired from elsewhere are not what the rule protects, so trading those is always free.
        if (departingPick.Round != draftRules.RetainedRoundNumber ||
            departingPick.OriginalFranchiseId != franchiseId)
        {
            return [];
        }

        var firstYear = currentSeason.Year + 1;
        var lastYear = currentSeason.Year + draftRules.TradableFutureDraftHorizon;

        var retainedYears = new HashSet<int>();
        for (var year = firstYear; year <= lastYear; year++)
        {
            var pick = book.Find(new Season(year), draftRules.RetainedRoundNumber, franchiseId);
            if (pick is null || pick.Id == departingPick.Id)
            {
                continue;
            }

            var ownership = book.Ownership(pick.Id);
            if (ownership is not null &&
                ownership.CurrentOwnerFranchiseId == franchiseId &&
                ownership.Obligation is null)
            {
                retainedYears.Add(year);
            }
        }

        var interval = draftRules.RetainedRoundInterval;
        for (var windowStart = firstYear; windowStart + interval - 1 <= lastYear; windowStart++)
        {
            var windowEnd = windowStart + interval - 1;
            var retainedInWindow = Enumerable.Range(windowStart, interval).Any(retainedYears.Contains);
            if (retainedInWindow)
            {
                continue;
            }

            return
            [
                new DomainError(
                    RetentionCode,
                    $"Franchise '{franchiseId.Value}' must still control its own round {draftRules.RetainedRoundNumber} pick at least once in every {interval} consecutive future drafts. Giving up the {departingPick.DraftSeason.Year} pick would leave it without one across the {windowStart} to {windowEnd} drafts.")
            ];
        }

        return [];
    }

    private static DomainOperationResult Fail(string code, string message) =>
        DomainOperationResult.Failure(new DomainError(code, message));
}
