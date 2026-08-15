using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;

namespace BallGM.Rules.DraftAssets;

/// <summary>
/// Settles one draft's encumbrances against a supplied draft order: which swaps are worth taking,
/// which protected picks convey, and what happens to the ones that do not.
/// <para>
/// <strong>Resolution order: swap rights resolve before protections.</strong> This is a rule, so it
/// is a decision rather than an accident, and sims differ on it — which is exactly why it is written
/// down. A swap changes <em>which selection a pick is</em>; a protection asks <em>where this pick
/// landed</em>. Testing the protection first would test it against a selection number the asset no
/// longer occupies, so a franchise could sell a top-4-protected pick, swap into a better selection,
/// and still keep the pick on a protection that no longer describes reality. Settling swaps first
/// means a protection is always judged against the selection the pick will actually make.
/// </para>
/// <para>
/// Deliberately not modelled here: lottery odds (the order arrives supplied — Milestone 8 generates
/// it), range and record-conditional protections, cash considerations, and multi-team pick routing.
/// Swap chains that pass through more than two assets resolve in the book's deterministic pick
/// order rather than through a routing graph; a league needing genuine routing needs that graph
/// built deliberately, not inferred from this loop.
/// </para>
/// </summary>
public sealed class PickConveyanceEvaluator
{
    private const string MissingSelectionCode = "conveyance.selection_missing_from_draft_order";
    private const string MissingCounterpartCode = "conveyance.swap_counterpart_missing";
    private const string MissingRolloverTargetCode = "conveyance.rollover_target_missing";

    private const string SwapExercisedCode = "conveyance.swap_exercised";
    private const string SwapDeclinedCode = "conveyance.swap_declined";
    private const string ConveyedUnprotectedCode = "conveyance.conveyed_unprotected";
    private const string ConveyedOutsideProtectionCode = "conveyance.conveyed_outside_protection";
    private const string RolledOverCode = "conveyance.protection_held_rolls_over";
    private const string RolledOverUnprotectedCode = "conveyance.protection_held_rolls_over_unprotected";
    private const string ConvertedCode = "conveyance.protection_held_converts_round";
    private const string ExtinguishedCode = "conveyance.protection_held_extinguishes";

    /// <summary>
    /// Resolves the draft and applies the result to the book: conveyed picks change hands, held
    /// protections move their obligation to the following draft, spent swap rights come off.
    /// Applying is part of the job on purpose — a conveyance that is calculated but not applied
    /// leaves the board showing an obligation that has already been decided.
    /// <para>
    /// The returned report is what the caller writes to the transaction ledger. Nothing here reads a
    /// clock or a random source: the same book and the same order produce the same report every run.
    /// </para>
    /// </summary>
    public DomainOperationResult<DraftConveyanceReport> Resolve(DraftAssetBook book, DraftOrderSnapshot draftOrder)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(draftOrder);

        var draftSeason = draftOrder.DraftSeason;
        var picks = book.PicksInDraft(draftSeason);

        var selections = new Dictionary<DraftPickId, int>();
        var errors = new List<DomainError>();

        foreach (var pick in picks)
        {
            var selection = draftOrder.SelectionFor(pick.Round, pick.OriginalFranchiseId);
            if (selection is null)
            {
                errors.Add(new DomainError(
                    MissingSelectionCode,
                    $"The {draftSeason.Year} draft order does not say where franchise '{pick.OriginalFranchiseId.Value}' selects in round {pick.Round}."));
                continue;
            }

            selections[pick.Id] = selection.Value;
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<DraftConveyanceReport>.Failure(errors.ToArray());
        }

        var outcomes = new List<ConveyanceOutcome>();

        var swapResult = ResolveSwapRights(book, picks, selections, outcomes);
        if (swapResult.IsFailure)
        {
            return DomainOperationResult<DraftConveyanceReport>.Failure(swapResult.Errors.ToArray());
        }

        var protectionResult = ResolveProtections(book, picks, selections, draftSeason, outcomes);
        if (protectionResult.IsFailure)
        {
            return DomainOperationResult<DraftConveyanceReport>.Failure(protectionResult.Errors.ToArray());
        }

        var effective = picks
            .Select(pick => new EffectiveSelection(pick.Id, pick.Round, selections[pick.Id]))
            .ToList();

        return DomainOperationResult<DraftConveyanceReport>.Success(
            new DraftConveyanceReport(draftSeason, outcomes, effective));
    }

    /// <summary>
    /// Phase one. A swap right is exercised when the encumbered pick lands better — a lower selection
    /// number — than the counterpart the holder would give up. Either way the right is spent: a
    /// conditional right that survives its own draft is a right that gets exercised twice.
    /// </summary>
    private static DomainOperationResult ResolveSwapRights(
        DraftAssetBook book,
        IReadOnlyList<DraftPick> picks,
        Dictionary<DraftPickId, int> selections,
        List<ConveyanceOutcome> outcomes)
    {
        foreach (var pick in picks)
        {
            var ownership = book.Ownership(pick.Id);
            var swap = ownership?.PendingSwap;
            if (ownership is null || swap is null)
            {
                continue;
            }

            if (!selections.TryGetValue(swap.CounterpartPickId, out var counterpartSelection))
            {
                return DomainOperationResult.Failure(
                    new DomainError(
                        MissingCounterpartCode,
                        $"The swap right on pick '{pick.Id.Value}' names counterpart pick '{swap.CounterpartPickId.Value}', which is not in the {pick.DraftSeason.Year} draft."));
            }

            var counterpart = book.Pick(swap.CounterpartPickId)!;
            var thisSelection = selections[pick.Id];
            var exercised = thisSelection < counterpartSelection;

            if (exercised)
            {
                selections[pick.Id] = counterpartSelection;
                selections[swap.CounterpartPickId] = thisSelection;
            }

            var releaseResult = ownership.Release(swap.Id);
            if (releaseResult.IsFailure)
            {
                return releaseResult;
            }

            outcomes.Add(new ConveyanceOutcome(
                pick.Id,
                swap.Id,
                exercised ? ConveyanceOutcomeKind.SwapExercised : ConveyanceOutcomeKind.SwapDeclined,
                exercised ? counterpartSelection : thisSelection,
                exercised ? SwapExercisedCode : SwapDeclinedCode,
                exercised
                    ? $"Swap exercised: this pick landed at selection {thisSelection} and the swap holder's own pick at {counterpartSelection}, so the two selections change places."
                    : $"Swap declined by outcome: this pick landed at selection {thisSelection}, no better than the holder's own pick at {counterpartSelection}, so the selections stay where they are.",
                swap.HolderFranchiseId,
                counterpart.Id));
        }

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Phase two, against the selections phase one left behind. Every obligation ends this draft in
    /// exactly one of four states — conveyed, rolled to the next draft, converted to a later round,
    /// or extinguished — because a protection schedule that terminates is the whole reason
    /// <see cref="PickProtection"/> insists on a fallback.
    /// </summary>
    private static DomainOperationResult ResolveProtections(
        DraftAssetBook book,
        IReadOnlyList<DraftPick> picks,
        IReadOnlyDictionary<DraftPickId, int> selections,
        Season draftSeason,
        List<ConveyanceOutcome> outcomes)
    {
        var nextDraftSeason = new Season(draftSeason.Year + 1);

        foreach (var pick in picks)
        {
            var ownership = book.Ownership(pick.Id);
            var obligation = ownership?.Obligation;
            if (ownership is null || obligation is null)
            {
                continue;
            }

            var selection = selections[pick.Id];
            var level = obligation.CurrentProtectionLevel;

            if (level is null || selection > level.Value)
            {
                var conveyResult = Convey(ownership, obligation, pick, selection, level, outcomes);
                if (conveyResult.IsFailure)
                {
                    return conveyResult;
                }

                continue;
            }

            var heldResult = ProtectionHeld(book, ownership, obligation, pick, selection, level.Value, nextDraftSeason, outcomes);
            if (heldResult.IsFailure)
            {
                return heldResult;
            }
        }

        return DomainOperationResult.Success;
    }

    private static DomainOperationResult Convey(
        PickOwnership ownership,
        PickObligation obligation,
        DraftPick pick,
        int selection,
        int? level,
        List<ConveyanceOutcome> outcomes)
    {
        // An obligation whose beneficiary already holds the asset conveys without a transfer: the
        // pick is where it was promised to end up, and moving it to itself would be a failure.
        if (ownership.CurrentOwnerFranchiseId != obligation.BeneficiaryFranchiseId)
        {
            var transferResult = ownership.TransferTo(obligation.BeneficiaryFranchiseId);
            if (transferResult.IsFailure)
            {
                return transferResult;
            }
        }

        var releaseResult = ownership.Release(obligation.Id);
        if (releaseResult.IsFailure)
        {
            return releaseResult;
        }

        outcomes.Add(new ConveyanceOutcome(
            pick.Id,
            obligation.Id,
            ConveyanceOutcomeKind.Conveyed,
            selection,
            level is null ? ConveyedUnprotectedCode : ConveyedOutsideProtectionCode,
            level is null
                ? $"Unprotected: the pick landed at selection {selection} and conveys."
                : $"Protected through selection {level.Value}, landed at {selection}: the protection does not cover it, so the pick conveys.",
            obligation.BeneficiaryFranchiseId));

        return DomainOperationResult.Success;
    }

    private static DomainOperationResult ProtectionHeld(
        DraftAssetBook book,
        PickOwnership ownership,
        PickObligation obligation,
        DraftPick pick,
        int selection,
        int level,
        Season nextDraftSeason,
        List<ConveyanceOutcome> outcomes)
    {
        var held = $"Protected through selection {level}, landed at {selection}";

        if (obligation.HasRemainingSchedule)
        {
            var rolled = obligation.RolledForward();
            return Roll(
                book,
                ownership,
                obligation,
                rolled,
                pick,
                pick.Round,
                nextDraftSeason,
                selection,
                ConveyanceOutcomeKind.RolledOver,
                RolledOverCode,
                $"{held}: the pick stays, and the obligation rolls to the {nextDraftSeason.Year} draft protected through selection {rolled.CurrentProtectionLevel}.",
                outcomes);
        }

        switch (obligation.Protection.Fallback.Kind)
        {
            case PickProtectionFallbackKind.ConveysUnprotected:
                return Roll(
                    book,
                    ownership,
                    obligation,
                    obligation.Unprotected(),
                    pick,
                    pick.Round,
                    nextDraftSeason,
                    selection,
                    ConveyanceOutcomeKind.RolledOver,
                    RolledOverUnprotectedCode,
                    $"{held}: the protection schedule is spent, so the obligation rolls to the {nextDraftSeason.Year} draft unprotected and conveys there wherever it lands.",
                    outcomes);

            case PickProtectionFallbackKind.ConvertsToRound:
                {
                    var convertedRound = obligation.Protection.Fallback.ConvertsToRound!.Value;
                    return Roll(
                        book,
                        ownership,
                        obligation,
                        obligation.Unprotected(),
                        pick,
                        convertedRound,
                        nextDraftSeason,
                        selection,
                        ConveyanceOutcomeKind.ConvertedToLaterRound,
                        ConvertedCode,
                        $"{held}: the protection schedule is spent, so the obligation converts to an unprotected round {convertedRound} pick in the {nextDraftSeason.Year} draft.",
                        outcomes);
                }

            default:
                {
                    var releaseResult = ownership.Release(obligation.Id);
                    if (releaseResult.IsFailure)
                    {
                        return releaseResult;
                    }

                    outcomes.Add(new ConveyanceOutcome(
                        pick.Id,
                        obligation.Id,
                        ConveyanceOutcomeKind.Extinguished,
                        selection,
                        ExtinguishedCode,
                        $"{held}: the protection schedule is spent and the obligation extinguishes. The pick stays for good and nothing is owed.",
                        ownership.CurrentOwnerFranchiseId));

                    return DomainOperationResult.Success;
                }
        }
    }

    /// <summary>
    /// Moves an obligation off the pick it failed to convey on and onto the following draft's pick.
    /// The obligation travels; the pick it was riding does not — which is only expressible because
    /// identity and ownership are separate types.
    /// </summary>
    private static DomainOperationResult Roll(
        DraftAssetBook book,
        PickOwnership ownership,
        PickObligation obligation,
        PickObligation successorObligation,
        DraftPick pick,
        int successorRound,
        Season nextDraftSeason,
        int selection,
        ConveyanceOutcomeKind kind,
        string ruleCode,
        string explanation,
        List<ConveyanceOutcome> outcomes)
    {
        var successorPick = book.Find(nextDraftSeason, successorRound, pick.OriginalFranchiseId);
        if (successorPick is null)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    MissingRolloverTargetCode,
                    $"The obligation on the {pick.DraftSeason.Year} round {pick.Round} pick cannot roll forward: the {nextDraftSeason.Year} round {successorRound} pick for franchise '{pick.OriginalFranchiseId.Value}' is not registered."));
        }

        var releaseResult = ownership.Release(obligation.Id);
        if (releaseResult.IsFailure)
        {
            return releaseResult;
        }

        var encumberResult = book.Encumber(successorPick.Id, successorObligation);
        if (encumberResult.IsFailure)
        {
            return encumberResult;
        }

        outcomes.Add(new ConveyanceOutcome(
            pick.Id,
            obligation.Id,
            kind,
            selection,
            ruleCode,
            explanation,
            ownership.CurrentOwnerFranchiseId,
            successorPick.Id));

        return DomainOperationResult.Success;
    }
}
