using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.DraftAssets;

namespace BallGM.Infrastructure.Fixtures;

/// <summary>
/// Gives the fixture league a draft-asset history worth looking at: a franchise hoarding first-round
/// picks, one that has already spent two of its next three, a protected pick that conveys against
/// the shipped draft order and one that does not, an obligation part-way through its rollover, and a
/// live swap right nobody has settled yet.
/// <para>
/// Every scripted move is put through <see cref="PickOwnershipRules"/> before it is applied, so the
/// shipped fixture cannot quietly contain a league state the rules forbid — if a scripted trade ever
/// breaks the configured retention restriction, the league fails to load and says which one.
/// </para>
/// <para>
/// The current draft is then settled against a fixed, supplied order (worst record picks first,
/// mirroring the fixture's own team-strength spread). No lottery, no randomness: the same league
/// appears on every launch, and the conveyance outcomes on the board are reproducible.
/// </para>
/// </summary>
internal static class FixtureDraftAssets
{
    /// <summary>A scripted outright pick trade, in franchise indices into the fixture's franchise list.</summary>
    private sealed record ScriptedTrade(int FromFranchise, int ToFranchise, int SeasonOffset, int Round, string Reason);

    /// <summary>A scripted promise: the owner owes this pick to another franchise, subject to a protection.</summary>
    private sealed record ScriptedObligation(
        int OwnerFranchise,
        int BeneficiaryFranchise,
        int SeasonOffset,
        int Round,
        int[] ProtectedSelections,
        PickProtectionFallbackKind FallbackKind,
        int? ConvertsToRound,
        string Reason);

    /// <summary>A scripted swap right: the holder may take the encumbered pick's selection instead of their own.</summary>
    private sealed record ScriptedSwap(
        int EncumberedFranchise,
        int HolderFranchise,
        int SeasonOffset,
        int Round,
        string Reason);

    /// <summary>
    /// Outright trades. Franchise 0 collects other franchises' firsts; franchise 3 gives one up on
    /// top of the one it already owes, so the board has a hoarder and a franchise that has mortgaged
    /// two of its next three.
    /// </summary>
    private static readonly ScriptedTrade[] Trades =
    [
        new(FromFranchise: 4, ToFranchise: 0, SeasonOffset: 1, Round: 1,
            Reason: "traded to acquire an established starter."),
        new(FromFranchise: 5, ToFranchise: 0, SeasonOffset: 2, Round: 1,
            Reason: "traded to clear an unwanted contract."),
        new(FromFranchise: 3, ToFranchise: 1, SeasonOffset: 3, Round: 1,
            Reason: "traded as the sweetener in a two-player deal."),
    ];

    private static readonly ScriptedObligation[] Obligations =
    [
        // Resolves this draft: the original franchise picks third, inside its own protection, so it
        // keeps the pick and the obligation rolls a year — the board's part-rolled obligation.
        new(OwnerFranchise: 3, BeneficiaryFranchise: 0, SeasonOffset: 0, Round: 1,
            ProtectedSelections: [4, 3], PickProtectionFallbackKind.ConveysUnprotected, ConvertsToRound: null,
            Reason: "owed as the price of a mid-season addition, top-4 protected this draft and top-3 the next."),

        // Resolves this draft the other way: the original franchise picks fifth, outside its
        // protection, so the pick actually conveys.
        new(OwnerFranchise: 1, BeneficiaryFranchise: 2, SeasonOffset: 0, Round: 1,
            ProtectedSelections: [2], PickProtectionFallbackKind.Extinguishes, ConvertsToRound: null,
            Reason: "owed from an earlier deal, protected only at the very top of the draft."),

        // Sits on the board unresolved, so a GM can see a converting fallback written out.
        new(OwnerFranchise: 1, BeneficiaryFranchise: 5, SeasonOffset: 3, Round: 1,
            ProtectedSelections: [6, 4], PickProtectionFallbackKind.ConvertsToRound, ConvertsToRound: 2,
            Reason: "owed on a protected basis, converting to a second-round pick if it never conveys."),
    ];

    private static readonly ScriptedSwap[] Swaps =
    [
        // Settles this draft, and is declined by outcome: the holder's own pick lands better.
        new(EncumberedFranchise: 4, HolderFranchise: 5, SeasonOffset: 0, Round: 1,
            Reason: "granted in a deal for a veteran wing."),

        // Still live, so the board has a swap right on it that has not been decided.
        new(EncumberedFranchise: 2, HolderFranchise: 0, SeasonOffset: 2, Round: 1,
            Reason: "granted as protection against a rebuilding season."),
    ];

    public static DomainOperationResult<DraftAssetBook> Build(
        LeagueId leagueId,
        Season currentSeason,
        IReadOnlyList<Franchise> franchises,
        DraftRules draftRules,
        TransactionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(leagueId);
        ArgumentNullException.ThrowIfNull(currentSeason);
        ArgumentNullException.ThrowIfNull(franchises);
        ArgumentNullException.ThrowIfNull(draftRules);
        ArgumentNullException.ThrowIfNull(ledger);

        var book = new DraftAssetBook(leagueId);

        // A league that holds no draft gets an empty book, not a scripted pick history: there is
        // nothing for a franchise to own, and every scripted trade below would be trading an asset
        // no draft will ever select with.
        if (!draftRules.HasDraft)
        {
            return DomainOperationResult<DraftAssetBook>.Success(book);
        }

        var registerResult = RegisterPicks(book, leagueId, currentSeason, franchises, draftRules);
        if (registerResult.IsFailure)
        {
            return DomainOperationResult<DraftAssetBook>.Failure(registerResult.Errors.ToArray());
        }

        var rules = new PickOwnershipRules();

        var tradeResult = ApplyTrades(book, currentSeason, franchises, draftRules, ledger, rules);
        if (tradeResult.IsFailure)
        {
            return DomainOperationResult<DraftAssetBook>.Failure(tradeResult.Errors.ToArray());
        }

        var encumbranceResult = ApplyEncumbrances(book, currentSeason, franchises, draftRules, ledger, rules);
        if (encumbranceResult.IsFailure)
        {
            return DomainOperationResult<DraftAssetBook>.Failure(encumbranceResult.Errors.ToArray());
        }

        var resolveResult = ResolveCurrentDraft(book, currentSeason, franchises, draftRules, ledger);
        if (resolveResult.IsFailure)
        {
            return DomainOperationResult<DraftAssetBook>.Failure(resolveResult.Errors.ToArray());
        }

        return DomainOperationResult<DraftAssetBook>.Success(book);
    }

    /// <summary>
    /// Every franchise's own picks for the current draft and each draft inside the configured
    /// tradable horizon. Registering them all up front is what lets an obligation roll forward: a
    /// rollover needs the following draft's pick to already exist.
    /// </summary>
    private static DomainOperationResult RegisterPicks(
        DraftAssetBook book,
        LeagueId leagueId,
        Season currentSeason,
        IReadOnlyList<Franchise> franchises,
        DraftRules draftRules)
    {
        // One draft beyond the tradable horizon, so an obligation on the last tradable draft still
        // has somewhere to roll to instead of failing at the edge of the board.
        var lastYear = currentSeason.Year + draftRules.TradableFutureDraftHorizon + 1;

        for (var year = currentSeason.Year; year <= lastYear; year++)
        {
            for (var round = 1; round <= draftRules.RoundCount; round++)
            {
                foreach (var franchise in franchises)
                {
                    var pickResult = DraftPick.Create(
                        new DraftPickId(SortableId.NewId()),
                        leagueId,
                        new Season(year),
                        round,
                        franchise.Id);

                    if (pickResult.IsFailure)
                    {
                        return DomainOperationResult.Failure(pickResult.Errors.ToArray());
                    }

                    // Whether this league can hold the pick at all is a rules question, asked before
                    // the book is touched — the same order the trade engine asks it in.
                    var eligibilityResult = new PickOwnershipRules().ValidateRegistration(pickResult.Value, draftRules);
                    if (eligibilityResult.IsFailure)
                    {
                        return eligibilityResult;
                    }

                    var registerResult = book.Register(pickResult.Value);
                    if (registerResult.IsFailure)
                    {
                        return registerResult;
                    }
                }
            }
        }

        return DomainOperationResult.Success;
    }

    private static DomainOperationResult ApplyTrades(
        DraftAssetBook book,
        Season currentSeason,
        IReadOnlyList<Franchise> franchises,
        DraftRules draftRules,
        TransactionLedger ledger,
        PickOwnershipRules rules)
    {
        foreach (var trade in Trades)
        {
            var from = franchises[trade.FromFranchise];
            var to = franchises[trade.ToFranchise];
            var pick = book.Find(new Season(currentSeason.Year + trade.SeasonOffset), trade.Round, from.Id);
            if (pick is null)
            {
                return MissingPick(currentSeason.Year + trade.SeasonOffset, trade.Round, from);
            }

            var validation = rules.ValidateTransfer(book, pick.Id, from.Id, to.Id, currentSeason, draftRules);
            if (validation.IsFailure)
            {
                return validation;
            }

            var transferResult = book.Transfer(pick.Id, to.Id);
            if (transferResult.IsFailure)
            {
                return transferResult;
            }

            ledger.RecordPickEvent(
                TransactionKind.DraftPickTransferred,
                currentSeason,
                from.Id,
                pick.Id,
                $"The {pick.DraftSeason.Year} round {pick.Round} pick went to {to.Name}, {trade.Reason}",
                to.Id);
        }

        return DomainOperationResult.Success;
    }

    private static DomainOperationResult ApplyEncumbrances(
        DraftAssetBook book,
        Season currentSeason,
        IReadOnlyList<Franchise> franchises,
        DraftRules draftRules,
        TransactionLedger ledger,
        PickOwnershipRules rules)
    {
        foreach (var scripted in Obligations)
        {
            var owner = franchises[scripted.OwnerFranchise];
            var beneficiary = franchises[scripted.BeneficiaryFranchise];
            var season = new Season(currentSeason.Year + scripted.SeasonOffset);
            var pick = book.Find(season, scripted.Round, owner.Id);
            if (pick is null)
            {
                return MissingPick(season.Year, scripted.Round, owner);
            }

            var fallbackResult = PickProtectionFallback.Rebuild(scripted.FallbackKind, scripted.ConvertsToRound);
            if (fallbackResult.IsFailure)
            {
                return DomainOperationResult.Failure(fallbackResult.Errors.ToArray());
            }

            var protectionResult = PickProtection.TopSelections(scripted.ProtectedSelections, fallbackResult.Value);
            if (protectionResult.IsFailure)
            {
                return DomainOperationResult.Failure(protectionResult.Errors.ToArray());
            }

            var obligation = new PickObligation(
                new PickEncumbranceId(SortableId.NewId()),
                beneficiary.Id,
                protectionResult.Value);

            var validation = rules.ValidateEncumbrance(book, pick.Id, owner.Id, obligation, currentSeason, draftRules);
            if (validation.IsFailure)
            {
                return validation;
            }

            var encumberResult = book.Encumber(pick.Id, obligation);
            if (encumberResult.IsFailure)
            {
                return encumberResult;
            }

            ledger.RecordPickEvent(
                TransactionKind.DraftPickEncumbered,
                currentSeason,
                owner.Id,
                pick.Id,
                $"The {season.Year} round {scripted.Round} pick is {beneficiary.Name}'s, {scripted.Reason}",
                beneficiary.Id);
        }

        foreach (var scripted in Swaps)
        {
            var encumbered = franchises[scripted.EncumberedFranchise];
            var holder = franchises[scripted.HolderFranchise];
            var season = new Season(currentSeason.Year + scripted.SeasonOffset);

            var pick = book.Find(season, scripted.Round, encumbered.Id);
            var counterpart = book.Find(season, scripted.Round, holder.Id);
            if (pick is null)
            {
                return MissingPick(season.Year, scripted.Round, encumbered);
            }

            if (counterpart is null)
            {
                return MissingPick(season.Year, scripted.Round, holder);
            }

            var swap = new SwapRight(new PickEncumbranceId(SortableId.NewId()), holder.Id, counterpart.Id);

            var validation = rules.ValidateEncumbrance(book, pick.Id, encumbered.Id, swap, currentSeason, draftRules);
            if (validation.IsFailure)
            {
                return validation;
            }

            var encumberResult = book.Encumber(pick.Id, swap);
            if (encumberResult.IsFailure)
            {
                return encumberResult;
            }

            ledger.RecordPickEvent(
                TransactionKind.DraftPickEncumbered,
                currentSeason,
                encumbered.Id,
                pick.Id,
                $"{holder.Name} may swap their {season.Year} round {scripted.Round} selection for this one, {scripted.Reason}",
                holder.Id);
        }

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Settles the current draft against a supplied order and writes every outcome to the ledger, so
    /// the board's rolled-over and conveyed obligations have an auditable line explaining them.
    /// </summary>
    private static DomainOperationResult ResolveCurrentDraft(
        DraftAssetBook book,
        Season currentSeason,
        IReadOnlyList<Franchise> franchises,
        DraftRules draftRules,
        TransactionLedger ledger)
    {
        var orderResult = BuildDraftOrder(currentSeason, franchises, draftRules);
        if (orderResult.IsFailure)
        {
            return DomainOperationResult.Failure(orderResult.Errors.ToArray());
        }

        var reportResult = new PickConveyanceEvaluator().Resolve(book, orderResult.Value);
        if (reportResult.IsFailure)
        {
            return DomainOperationResult.Failure(reportResult.Errors.ToArray());
        }

        var namesById = franchises.ToDictionary(franchise => franchise.Id, franchise => franchise.Name);

        foreach (var outcome in reportResult.Value.Outcomes)
        {
            var pick = book.Pick(outcome.PickId)!;
            var subject = pick.OriginalFranchiseId;
            var counterparty = outcome.ResultingOwnerFranchiseId;

            var subjectName = namesById.GetValueOrDefault(subject, subject.Value);

            ledger.RecordPickEvent(
                KindFor(outcome.Kind),
                currentSeason,
                subject,
                outcome.PickId,
                $"{subjectName}'s {pick.DraftSeason.Year} round {pick.Round} pick: {outcome.Explanation}",
                counterparty);

            // An obligation that moved leaves a line on both assets. The pick it left needs to say
            // where the obligation went, and the pick it landed on needs to say where it came from —
            // a drill-down that only shows one half cannot explain why a clean future pick is
            // suddenly encumbered.
            var obligationMoved =
                outcome.Kind is ConveyanceOutcomeKind.RolledOver or ConveyanceOutcomeKind.ConvertedToLaterRound;

            if (!obligationMoved || outcome.SuccessorPickId is null)
            {
                continue;
            }

            var successor = book.Pick(outcome.SuccessorPickId)!;
            ledger.RecordPickEvent(
                KindFor(outcome.Kind),
                currentSeason,
                subject,
                successor.Id,
                $"The obligation on {subjectName}'s {pick.DraftSeason.Year} round {pick.Round} pick moved onto this one when that pick did not convey.",
                counterparty);
        }

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// The shipped draft order: the franchise list is built strongest-first, so reversing it gives
    /// the weakest franchise the first selection. Fixed rather than drawn, because the lottery is
    /// Milestone 8 and conveyance has to be testable without one.
    /// </summary>
    private static DomainOperationResult<DraftOrderSnapshot> BuildDraftOrder(
        Season currentSeason,
        IReadOnlyList<Franchise> franchises,
        DraftRules draftRules)
    {
        var slots = new List<DraftOrderSlot>();

        for (var round = 1; round <= draftRules.RoundCount; round++)
        {
            for (var position = 0; position < franchises.Count; position++)
            {
                slots.Add(new DraftOrderSlot(
                    round,
                    position + 1,
                    franchises[franchises.Count - 1 - position].Id));
            }
        }

        return DraftOrderSnapshot.Create(currentSeason, slots);
    }

    private static TransactionKind KindFor(ConveyanceOutcomeKind kind) => kind switch
    {
        ConveyanceOutcomeKind.SwapExercised => TransactionKind.SwapRightExercised,
        ConveyanceOutcomeKind.SwapDeclined => TransactionKind.SwapRightDeclined,
        ConveyanceOutcomeKind.Conveyed => TransactionKind.DraftPickConveyed,
        ConveyanceOutcomeKind.RolledOver => TransactionKind.DraftPickRolledOver,
        ConveyanceOutcomeKind.ConvertedToLaterRound => TransactionKind.DraftPickConverted,
        _ => TransactionKind.DraftPickExtinguished,
    };

    private static DomainOperationResult MissingPick(int year, int round, Franchise franchise) =>
        DomainOperationResult.Failure(
            new DomainError(
                "fixture.draft_pick_missing",
                $"The fixture expected a {year} round {round} pick for {franchise.Name}, which was not registered."));
}
