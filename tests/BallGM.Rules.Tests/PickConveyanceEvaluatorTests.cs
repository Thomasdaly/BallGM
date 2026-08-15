using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Rules.DraftAssets;

namespace BallGM.Rules.Tests;

/// <summary>
/// Conveyance against a supplied draft order. Every test here writes its own order rather than
/// drawing one, which is the whole point of injecting it: none of this needs a lottery to be true.
/// </summary>
public sealed class PickConveyanceEvaluatorTests
{
    private static readonly LeagueId League = new(SortableId.NewId());
    private static readonly Season Draft2032 = new(2032);
    private static readonly Season Draft2033 = new(2033);

    [Fact]
    public void ProtectedPick_ConveysWhenItLandsOutsideTheProtection()
    {
        var owner = NewFranchise();
        var beneficiary = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, owner);
        Encumber(book, pick, beneficiary, [4], PickProtectionFallback.Extinguishes);

        var report = Resolve(book, Order(Draft2032, (owner, 7)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.Conveyed, outcome.Kind);
        Assert.Equal("conveyance.conveyed_outside_protection", outcome.RuleCode);
        Assert.Contains("Protected through selection 4, landed at 7", outcome.Explanation);
        Assert.Equal(beneficiary, book.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
        Assert.Null(book.Ownership(pick.Id)!.Obligation);
    }

    [Fact]
    public void ProtectedPick_StaysAndTheObligationRollsToTheNextDraftWhenTheProtectionHolds()
    {
        var owner = NewFranchise();
        var beneficiary = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, owner);
        var nextPick = Register(book, Draft2033, 1, owner);
        Encumber(book, pick, beneficiary, [4, 3], PickProtectionFallback.ConveysUnprotected);

        var report = Resolve(book, Order(Draft2032, (owner, 3)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.RolledOver, outcome.Kind);
        Assert.Equal("conveyance.protection_held_rolls_over", outcome.RuleCode);
        Assert.Equal(nextPick.Id, outcome.SuccessorPickId);

        // The pick stayed; the obligation is what moved.
        Assert.Equal(owner, book.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
        Assert.Null(book.Ownership(pick.Id)!.Obligation);
        Assert.Equal(3, book.Ownership(nextPick.Id)!.Obligation!.CurrentProtectionLevel);
    }

    [Fact]
    public void SpentSchedule_RollsOnUnprotectedWhenThatIsTheStatedFallback()
    {
        var owner = NewFranchise();
        var beneficiary = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, owner);
        var nextPick = Register(book, Draft2033, 1, owner);
        Encumber(book, pick, beneficiary, [4], PickProtectionFallback.ConveysUnprotected);

        var report = Resolve(book, Order(Draft2032, (owner, 2)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal("conveyance.protection_held_rolls_over_unprotected", outcome.RuleCode);

        var rolled = book.Ownership(nextPick.Id)!.Obligation!;
        Assert.Null(rolled.CurrentProtectionLevel);

        // Next draft it conveys wherever it lands — including at selection 1.
        var nextReport = Resolve(book, Order(Draft2033, (owner, 1)));
        var nextOutcome = Assert.Single(nextReport.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.Conveyed, nextOutcome.Kind);
        Assert.Equal("conveyance.conveyed_unprotected", nextOutcome.RuleCode);
        Assert.Equal(beneficiary, book.Ownership(nextPick.Id)!.CurrentOwnerFranchiseId);
    }

    [Fact]
    public void SpentSchedule_ExtinguishesTheObligationWhenThatIsTheStatedFallback()
    {
        var owner = NewFranchise();
        var beneficiary = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, owner);
        Register(book, Draft2033, 1, owner);
        Encumber(book, pick, beneficiary, [4], PickProtectionFallback.Extinguishes);

        var report = Resolve(book, Order(Draft2032, (owner, 1)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.Extinguished, outcome.Kind);
        Assert.Equal(owner, book.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
        Assert.False(book.Ownership(pick.Id)!.IsEncumbered);
    }

    [Fact]
    public void SpentSchedule_ConvertsToTheStatedLaterRoundInTheFollowingDraft()
    {
        var owner = NewFranchise();
        var beneficiary = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, owner);
        var nextFirst = Register(book, Draft2033, 1, owner);
        var nextSecond = Register(book, Draft2033, 2, owner);

        var fallback = PickProtectionFallback.ConvertsToLaterRound(2).Value;
        Encumber(book, pick, beneficiary, [5], fallback);

        var report = Resolve(book, Order(Draft2032, (owner, 5)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.ConvertedToLaterRound, outcome.Kind);
        Assert.Equal(nextSecond.Id, outcome.SuccessorPickId);
        Assert.Null(book.Ownership(nextFirst.Id)!.Obligation);
        Assert.NotNull(book.Ownership(nextSecond.Id)!.Obligation);
    }

    [Fact]
    public void SwapRight_IsExercisedWhenTheEncumberedPickLandsBetterThanTheHoldersOwn()
    {
        var encumberedFranchise = NewFranchise();
        var holder = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, encumberedFranchise);
        var counterpart = Register(book, Draft2032, 1, holder);
        Swap(book, pick, holder, counterpart);

        var report = Resolve(book, Order(Draft2032, (encumberedFranchise, 2), (holder, 6)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.SwapExercised, outcome.Kind);

        var selections = report.EffectiveSelections.ToDictionary(selection => selection.PickId, selection => selection.SelectionNumber);
        Assert.Equal(6, selections[pick.Id]);
        Assert.Equal(2, selections[counterpart.Id]);

        // The right is spent either way: a conditional right that survives its draft gets used twice.
        Assert.Null(book.Ownership(pick.Id)!.PendingSwap);
    }

    [Fact]
    public void SwapRight_IsDeclinedByOutcomeWhenTheHoldersOwnPickLandsBetter()
    {
        var encumberedFranchise = NewFranchise();
        var holder = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, encumberedFranchise);
        var counterpart = Register(book, Draft2032, 1, holder);
        Swap(book, pick, holder, counterpart);

        var report = Resolve(book, Order(Draft2032, (encumberedFranchise, 5), (holder, 1)));

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ConveyanceOutcomeKind.SwapDeclined, outcome.Kind);

        var selections = report.EffectiveSelections.ToDictionary(selection => selection.PickId, selection => selection.SelectionNumber);
        Assert.Equal(5, selections[pick.Id]);
        Assert.Equal(1, selections[counterpart.Id]);
        Assert.Null(book.Ownership(pick.Id)!.PendingSwap);
    }

    /// <summary>
    /// The ordering decision, stated as a test: the pick lands at 3, inside its own top-4 protection,
    /// but a swap moves it to selection 8 first — so the protection is judged against 8 and the pick
    /// conveys. Resolving protections first would have kept it, on a protection describing a
    /// selection the asset no longer occupies.
    /// </summary>
    [Fact]
    public void SwapRights_ResolveBeforeProtectionsSoAProtectionIsJudgedOnTheSelectionThePickActuallyMakes()
    {
        var owner = NewFranchise();
        var beneficiary = NewFranchise();
        var holder = NewFranchise();
        var book = new DraftAssetBook(League);

        var pick = Register(book, Draft2032, 1, owner);
        var counterpart = Register(book, Draft2032, 1, holder);
        Register(book, Draft2033, 1, owner);

        Encumber(book, pick, beneficiary, [4], PickProtectionFallback.Extinguishes);
        Swap(book, pick, holder, counterpart);

        var report = Resolve(book, Order(Draft2032, (owner, 3), (holder, 8)));

        Assert.Equal(ConveyanceOutcomeKind.SwapExercised, report.Outcomes[0].Kind);

        var conveyance = report.Outcomes[1];
        Assert.Equal(ConveyanceOutcomeKind.Conveyed, conveyance.Kind);
        Assert.Equal(8, conveyance.SelectionNumber);
        Assert.Equal(beneficiary, book.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
    }

    [Fact]
    public void Resolve_FailsExplainablyWhenTheObligationHasNowhereToRollTo()
    {
        var owner = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, Draft2032, 1, owner);
        Encumber(book, pick, NewFranchise(), [4, 3], PickProtectionFallback.Extinguishes);

        var result = new PickConveyanceEvaluator().Resolve(book, Order(Draft2032, (owner, 1)));

        Assert.True(result.IsFailure);
        Assert.Equal("conveyance.rollover_target_missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Resolve_FailsExplainablyWhenTheDraftOrderDoesNotCoverAPickInTheDraft()
    {
        var owner = NewFranchise();
        var missing = NewFranchise();
        var book = new DraftAssetBook(League);
        Register(book, Draft2032, 1, owner);
        Register(book, Draft2032, 1, missing);

        var result = new PickConveyanceEvaluator().Resolve(book, Order(Draft2032, (owner, 1)));

        Assert.True(result.IsFailure);
        Assert.Equal("conveyance.selection_missing_from_draft_order", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Resolve_IsDeterministicSoTheSameBookAndOrderProduceTheSameOutcomes()
    {
        static (DraftAssetBook Book, DraftOrderSnapshot Order, FranchiseId Owner) Build()
        {
            var owner = new FranchiseId("FRANCHISE-OWNER");
            var beneficiary = new FranchiseId("FRANCHISE-BENEFICIARY");
            var book = new DraftAssetBook(League);
            var pick = DraftPick.Create(new DraftPickId("PICK-2032-R1"), League, Draft2032, 1, owner).Value;
            var next = DraftPick.Create(new DraftPickId("PICK-2033-R1"), League, Draft2033, 1, owner).Value;
            Assert.True(book.Register(pick).IsSuccess);
            Assert.True(book.Register(next).IsSuccess);

            var protection = PickProtection.TopSelections([4, 3], PickProtectionFallback.ConveysUnprotected).Value;
            Assert.True(book.Encumber(
                pick.Id,
                new PickObligation(new PickEncumbranceId("ENCUMBRANCE-1"), beneficiary, protection)).IsSuccess);

            return (book, Order(Draft2032, (owner, 2)), owner);
        }

        var first = Build();
        var second = Build();

        var firstReport = Resolve(first.Book, first.Order);
        var secondReport = Resolve(second.Book, second.Order);

        Assert.Equal(
            firstReport.Outcomes.Select(outcome => (outcome.PickId.Value, outcome.RuleCode, outcome.Explanation)),
            secondReport.Outcomes.Select(outcome => (outcome.PickId.Value, outcome.RuleCode, outcome.Explanation)));
    }

    private static DraftConveyanceReport Resolve(DraftAssetBook book, DraftOrderSnapshot order)
    {
        var result = new PickConveyanceEvaluator().Resolve(book, order);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static DraftOrderSnapshot Order(Season season, params (FranchiseId Franchise, int Selection)[] slots)
    {
        // Selections must run 1..n without gaps, so the order is padded with placeholder franchises
        // for the positions the test does not care about.
        var used = slots.Select(slot => slot.Selection).ToHashSet();
        var highest = used.Max();
        var padded = Enumerable
            .Range(1, highest)
            .Where(selection => !used.Contains(selection))
            .Select(selection => new DraftOrderSlot(1, selection, new FranchiseId($"FILLER-{selection}")));

        var all = slots
            .Select(slot => new DraftOrderSlot(1, slot.Selection, slot.Franchise))
            .Concat(padded);

        var result = DraftOrderSnapshot.Create(season, all);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static FranchiseId NewFranchise() => new(SortableId.NewId());

    private static DraftPick Register(DraftAssetBook book, Season season, int round, FranchiseId original)
    {
        var pick = DraftPick.Create(new DraftPickId(SortableId.NewId()), League, season, round, original).Value;
        Assert.True(book.Register(pick).IsSuccess);
        return pick;
    }

    private static void Encumber(
        DraftAssetBook book,
        DraftPick pick,
        FranchiseId beneficiary,
        int[] protectedSelections,
        PickProtectionFallback fallback)
    {
        var protection = PickProtection.TopSelections(protectedSelections, fallback).Value;
        var obligation = new PickObligation(new PickEncumbranceId(SortableId.NewId()), beneficiary, protection);
        Assert.True(book.Encumber(pick.Id, obligation).IsSuccess);
    }

    private static void Swap(DraftAssetBook book, DraftPick pick, FranchiseId holder, DraftPick counterpart)
    {
        var swap = new SwapRight(new PickEncumbranceId(SortableId.NewId()), holder, counterpart.Id);
        Assert.True(book.Encumber(pick.Id, swap).IsSuccess);
    }
}
