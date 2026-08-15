using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.Tests;

public sealed class DraftAssetTests
{
    private static readonly LeagueId League = new(SortableId.NewId());
    private static readonly Season DraftSeason = new(2032);

    [Fact]
    public void Pick_StillNamesItsOriginalFranchiseAfterChangingHandsTwice()
    {
        var original = NewFranchise();
        var second = NewFranchise();
        var third = NewFranchise();

        var book = new DraftAssetBook(League);
        var pick = Register(book, DraftSeason, 1, original);

        Assert.True(book.Transfer(pick.Id, second).IsSuccess);
        Assert.True(book.Transfer(pick.Id, third).IsSuccess);

        // The question every protection is written against: whose pick was this originally?
        Assert.Equal(original, book.Pick(pick.Id)!.OriginalFranchiseId);
        Assert.Equal(third, book.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
    }

    [Fact]
    public void Pick_RejectsARoundBelowOneAsAnExplainableFailureRatherThanAThrow()
    {
        var result = DraftPick.Create(
            new DraftPickId(SortableId.NewId()),
            League,
            DraftSeason,
            round: 0,
            NewFranchise());

        Assert.True(result.IsFailure);
        Assert.Equal("draft_pick.invalid_round", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Book_RefusesASecondPickWithTheSameDraftRoundAndOriginalFranchise()
    {
        var franchise = NewFranchise();
        var book = new DraftAssetBook(League);
        Register(book, DraftSeason, 1, franchise);

        var duplicate = DraftPick.Create(
            new DraftPickId(SortableId.NewId()),
            League,
            DraftSeason,
            1,
            franchise).Value;

        var result = book.Register(duplicate);

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.duplicate_pick_coordinates", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Ownership_HasExactlyOneOwnerSoADuplicateTransferCannotSplitTheAsset()
    {
        var original = NewFranchise();
        var buyer = NewFranchise();
        var book = new DraftAssetBook(League);
        var pick = Register(book, DraftSeason, 1, original);

        Assert.True(book.Transfer(pick.Id, buyer).IsSuccess);
        var second = book.Transfer(pick.Id, buyer);

        Assert.True(second.IsFailure);
        Assert.Equal("pick_ownership.already_owned_by_franchise", Assert.Single(second.Errors).Code);
        Assert.Equal(buyer, book.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
    }

    [Fact]
    public void Ownership_RefusesToPromiseTheSamePickToASecondFranchise()
    {
        var book = new DraftAssetBook(League);
        var pick = Register(book, DraftSeason, 1, NewFranchise());

        Assert.True(book.Encumber(pick.Id, Obligation(NewFranchise())).IsSuccess);
        var second = book.Encumber(pick.Id, Obligation(NewFranchise()));

        Assert.True(second.IsFailure);
        Assert.Equal("pick_ownership.conflicting_obligation", Assert.Single(second.Errors).Code);
    }

    [Fact]
    public void Ownership_AllowsASwapRightAlongsideAnObligationBecauseThatPairingIsReal()
    {
        var book = new DraftAssetBook(League);
        var pick = Register(book, DraftSeason, 1, NewFranchise());
        var counterpart = Register(book, DraftSeason, 1, NewFranchise());
        var holder = NewFranchise();

        Assert.True(book.Encumber(pick.Id, Obligation(NewFranchise())).IsSuccess);
        Assert.True(book.Encumber(
            pick.Id,
            new SwapRight(new PickEncumbranceId(SortableId.NewId()), holder, counterpart.Id)).IsSuccess);

        var ownership = book.Ownership(pick.Id)!;
        Assert.NotNull(ownership.Obligation);
        Assert.Equal(holder, ownership.PendingSwap!.HolderFranchiseId);
    }

    [Fact]
    public void Ownership_RefusesASecondSwapRightOnTheSamePick()
    {
        var book = new DraftAssetBook(League);
        var pick = Register(book, DraftSeason, 1, NewFranchise());
        var counterpart = Register(book, DraftSeason, 1, NewFranchise());

        Assert.True(book.Encumber(
            pick.Id,
            new SwapRight(new PickEncumbranceId(SortableId.NewId()), NewFranchise(), counterpart.Id)).IsSuccess);

        var second = book.Encumber(
            pick.Id,
            new SwapRight(new PickEncumbranceId(SortableId.NewId()), NewFranchise(), counterpart.Id));

        Assert.True(second.IsFailure);
        Assert.Equal("pick_ownership.conflicting_swap_right", Assert.Single(second.Errors).Code);
    }

    [Fact]
    public void Book_ReportsAnUnknownPickRatherThanThrowingWhenAssetsAreMissing()
    {
        var book = new DraftAssetBook(League);

        var result = book.Transfer(new DraftPickId(SortableId.NewId()), NewFranchise());

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.unknown_pick", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Protection_RequiresAtLeastOneProtectedDraft()
    {
        var result = PickProtection.TopSelections([], PickProtectionFallback.Extinguishes);

        Assert.True(result.IsFailure);
        Assert.Equal("pick_protection.empty_schedule", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Protection_RejectsAProtectionLevelBelowOne()
    {
        var result = PickProtection.TopSelections([4, 0], PickProtectionFallback.ConveysUnprotected);

        Assert.True(result.IsFailure);
        Assert.Equal("pick_protection.invalid_protected_selection", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Protection_ReportsTheLevelForEachDraftAndNothingBeyondTheSchedule()
    {
        var protection = PickProtection.TopSelections([4, 3], PickProtectionFallback.ConveysUnprotected).Value;

        Assert.Equal(4, protection.LevelAt(0));
        Assert.Equal(3, protection.LevelAt(1));
        Assert.Null(protection.LevelAt(2));
        Assert.False(protection.IsUnprotected);
        Assert.True(PickProtection.Unprotected.IsUnprotected);
    }

    [Fact]
    public void Fallback_RefusesToConvertWithoutNamingARound()
    {
        var result = PickProtectionFallback.Rebuild(PickProtectionFallbackKind.ConvertsToRound, convertsToRound: null);

        Assert.True(result.IsFailure);
        Assert.Equal("pick_protection.fallback_round_missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Fallback_RefusesARoundOnAFallbackThatDoesNotConvert()
    {
        var result = PickProtectionFallback.Rebuild(PickProtectionFallbackKind.Extinguishes, convertsToRound: 2);

        Assert.True(result.IsFailure);
        Assert.Equal("pick_protection.fallback_round_not_applicable", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Obligation_MovesOneStepAlongItsScheduleWhenRolledForward()
    {
        var protection = PickProtection.TopSelections([4, 3], PickProtectionFallback.ConveysUnprotected).Value;
        var obligation = new PickObligation(new PickEncumbranceId(SortableId.NewId()), NewFranchise(), protection);

        var rolled = obligation.RolledForward();

        Assert.Equal(4, obligation.CurrentProtectionLevel);
        Assert.Equal(3, rolled.CurrentProtectionLevel);
        Assert.False(rolled.HasRemainingSchedule);
        Assert.Null(rolled.Unprotected().CurrentProtectionLevel);
        Assert.Equal(obligation.Id, rolled.Id);
    }

    [Fact]
    public void DraftOrder_RejectsARoundThatGivesAFranchiseTwoSelections()
    {
        var franchise = NewFranchise();
        var result = DraftOrderSnapshot.Create(
            DraftSeason,
            [
                new DraftOrderSlot(1, 1, franchise),
                new DraftOrderSlot(1, 2, franchise),
            ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_order.duplicate_franchise_in_round");
    }

    [Fact]
    public void DraftOrder_RejectsSelectionNumbersWithAGapInThem()
    {
        var result = DraftOrderSnapshot.Create(
            DraftSeason,
            [
                new DraftOrderSlot(1, 1, NewFranchise()),
                new DraftOrderSlot(1, 3, NewFranchise()),
            ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_order.selections_not_contiguous");
    }

    [Fact]
    public void DraftOrder_LooksUpASelectionByTheOriginalFranchiseNotTheCurrentOwner()
    {
        var original = NewFranchise();
        var owner = NewFranchise();
        var order = DraftOrderSnapshot.Create(
            DraftSeason,
            [
                new DraftOrderSlot(1, 1, original),
                new DraftOrderSlot(1, 2, owner),
            ]).Value;

        Assert.Equal(1, order.SelectionFor(1, original));
        Assert.Null(order.SelectionFor(2, original));
    }

    private static FranchiseId NewFranchise() => new(SortableId.NewId());

    private static PickObligation Obligation(FranchiseId beneficiary) =>
        new(new PickEncumbranceId(SortableId.NewId()), beneficiary, PickProtection.Unprotected);

    private static DraftPick Register(DraftAssetBook book, Season season, int round, FranchiseId original)
    {
        var pick = DraftPick.Create(new DraftPickId(SortableId.NewId()), League, season, round, original).Value;
        Assert.True(book.Register(pick).IsSuccess);
        return pick;
    }
}
