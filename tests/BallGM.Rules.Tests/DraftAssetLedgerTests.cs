using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Rules.Configuration;
using BallGM.Rules.DraftAssets;

namespace BallGM.Rules.Tests;

/// <summary>
/// What the board actually tells a GM. These tests assert on the words as well as the states,
/// because the sentence explaining what happens if a protection holds is the part of this screen
/// that changes a decision.
/// </summary>
public sealed class DraftAssetLedgerTests
{
    private static readonly LeagueId League = new(SortableId.NewId());
    private static readonly Season FirstDraft = new(2032);

    private static readonly DraftRules Rules = DraftRules.Create(
        roundCount: 1,
        lotteryEnabled: true,
        tradableFutureDraftHorizon: 3,
        retainedRoundNumber: 1,
        retainedRoundInterval: 2).Value;

    private static readonly FranchiseId Harbourline = new("FRANCHISE-HARBOURLINE");
    private static readonly FranchiseId Verdanmoor = new("FRANCHISE-VERDANMOOR");

    private static readonly IReadOnlyList<FranchiseDraftIdentity> Franchises =
    [
        new(Harbourline, "Harbourline Sporting Club"),
        new(Verdanmoor, "Verdanmoor Basketball Club"),
    ];

    [Fact]
    public void Board_CoversEveryFranchiseAcrossTheConfiguredNumberOfDrafts()
    {
        var board = Build(NewBook());

        Assert.Equal(2, board.Rows.Count);
        Assert.All(board.Rows, row => Assert.Equal(Rules.TradableFutureDraftHorizon, row.Drafts.Count));
        Assert.Equal([2032, 2033, 2034], board.Rows[0].Drafts.Select(cell => cell.DraftSeason.Year));
    }

    [Fact]
    public void Board_ShowsAnUnencumberedPickAsOwnedOutrightWithNothingRidingOnIt()
    {
        var board = Build(NewBook());
        var asset = AssetFor(board, Harbourline, 2032);

        Assert.Equal(PickControlState.OwnedOutright, asset.State);
        Assert.Null(asset.ProtectionSummary);
        Assert.Null(asset.OutcomeIfProtectionHolds);
    }

    [Fact]
    public void Board_SpellsOutAProtectionAndWhatHappensIfItHolds()
    {
        var book = NewBook();
        Promise(book, 2032, Harbourline, Verdanmoor, [4, 3], PickProtectionFallback.ConveysUnprotected);

        var asset = AssetFor(Build(book), Harbourline, 2032);

        Assert.Equal(PickControlState.OwedAway, asset.State);
        Assert.Equal(Verdanmoor, asset.CounterpartyFranchiseId);
        Assert.Equal("Owed to Verdanmoor Basketball Club: protected through selection 4.", asset.ProtectionSummary);
        Assert.Equal(
            "If it lands in the top 4, it stays and the obligation rolls to the 2033 draft protected through selection 3.",
            asset.OutcomeIfProtectionHolds);
    }

    [Fact]
    public void Board_SaysHowManyDraftsAnObligationHasAlreadyRolledThrough()
    {
        var book = NewBook();
        var pick = book.Find(FirstDraft, 1, Harbourline)!;
        var protection = PickProtection.TopSelections([4, 3], PickProtectionFallback.Extinguishes).Value;
        Assert.True(book.Encumber(
            pick.Id,
            new PickObligation(new PickEncumbranceId(SortableId.NewId()), Verdanmoor, protection, scheduleIndex: 1)).IsSuccess);

        var asset = AssetFor(Build(book), Harbourline, 2032);

        Assert.Contains("already rolled over 1 draft", asset.ProtectionSummary);
        Assert.Contains("the obligation extinguishes", asset.OutcomeIfProtectionHolds);
    }

    [Fact]
    public void Board_ShowsTheFallbackThatConvertsToALaterRound()
    {
        var book = NewBook();
        Promise(book, 2032, Harbourline, Verdanmoor, [6], PickProtectionFallback.ConvertsToLaterRound(2).Value);

        var asset = AssetFor(Build(book), Harbourline, 2032);

        Assert.Contains("converts to an unprotected round 2 pick in the 2033 draft", asset.OutcomeIfProtectionHolds);
    }

    [Fact]
    public void Board_ShowsAPickAlreadyTradedOutrightAsGoneFromTheOriginalFranchiseAndAcquiredByTheNewOne()
    {
        var book = NewBook();
        var pick = book.Find(FirstDraft, 1, Harbourline)!;
        Assert.True(book.Transfer(pick.Id, Verdanmoor).IsSuccess);

        var board = Build(book);

        Assert.Equal(PickControlState.TradedAway, AssetFor(board, Harbourline, 2032).State);

        var acquired = board.Rows
            .Single(row => row.FranchiseId == Verdanmoor)
            .Drafts
            .Single(cell => cell.DraftSeason.Year == 2032)
            .Assets
            .Single(asset => asset.OriginalFranchiseId == Harbourline);

        Assert.Equal(PickControlState.Acquired, acquired.State);
        Assert.Equal("Acquired from Harbourline Sporting Club.", acquired.ProtectionSummary);
    }

    [Fact]
    public void Board_ShowsBothSidesOfALiveSwapRight()
    {
        var book = NewBook();
        var encumbered = book.Find(FirstDraft, 1, Harbourline)!;
        var counterpart = book.Find(FirstDraft, 1, Verdanmoor)!;
        Assert.True(book.Encumber(
            encumbered.Id,
            new SwapRight(new PickEncumbranceId(SortableId.NewId()), Verdanmoor, counterpart.Id)).IsSuccess);

        var board = Build(book);

        var encumberedLine = AssetFor(board, Harbourline, 2032);
        Assert.Equal(PickControlState.SwapEncumbered, encumberedLine.State);
        Assert.Contains("may swap this selection for their own", encumberedLine.ProtectionSummary);

        var heldLine = board.Rows
            .Single(row => row.FranchiseId == Verdanmoor)
            .Drafts
            .Single(cell => cell.DraftSeason.Year == 2032)
            .Assets
            .Single(asset => asset.State == PickControlState.SwapRightHeld);

        Assert.Contains("Swap right held over Harbourline Sporting Club's round 1 pick", heldLine.ProtectionSummary);
    }

    [Fact]
    public void Board_ShowsTheBeneficiaryThatAPickIsOwedToThemBeforeItConveys()
    {
        var book = NewBook();
        Promise(book, 2032, Harbourline, Verdanmoor, [4], PickProtectionFallback.Extinguishes);

        var incoming = Build(book).Rows
            .Single(row => row.FranchiseId == Verdanmoor)
            .Drafts
            .Single(cell => cell.DraftSeason.Year == 2032)
            .Assets
            .Single(asset => asset.OriginalFranchiseId == Harbourline);

        // Not "acquired": an asset that may never convey is not an asset in hand.
        Assert.Equal(PickControlState.Incoming, incoming.State);
        Assert.Contains("Owed by Harbourline Sporting Club", incoming.ProtectionSummary);
    }

    private static DraftAssetBoard Build(DraftAssetBook book)
    {
        var result = new DraftAssetLedger().BuildBoard(book, Franchises, FirstDraft, Rules);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static PickAssetLine AssetFor(DraftAssetBoard board, FranchiseId franchise, int draftSeason) =>
        board.Rows
            .Single(row => row.FranchiseId == franchise)
            .Drafts
            .Single(cell => cell.DraftSeason.Year == draftSeason)
            .Assets
            .Single(asset => asset.OriginalFranchiseId == franchise);

    private static DraftAssetBook NewBook()
    {
        var book = new DraftAssetBook(League);

        foreach (var franchise in Franchises)
        {
            for (var year = FirstDraft.Year; year < FirstDraft.Year + Rules.TradableFutureDraftHorizon; year++)
            {
                var pick = DraftPick.Create(
                    new DraftPickId($"PICK-{franchise.FranchiseId.Value}-{year}"),
                    League,
                    new Season(year),
                    1,
                    franchise.FranchiseId).Value;

                Assert.True(book.Register(pick).IsSuccess);
            }
        }

        return book;
    }

    private static void Promise(
        DraftAssetBook book,
        int year,
        FranchiseId owner,
        FranchiseId beneficiary,
        int[] protectedSelections,
        PickProtectionFallback fallback)
    {
        var pick = book.Find(new Season(year), 1, owner)!;
        var protection = PickProtection.TopSelections(protectedSelections, fallback).Value;
        Assert.True(book.Encumber(
            pick.Id,
            new PickObligation(new PickEncumbranceId(SortableId.NewId()), beneficiary, protection)).IsSuccess);
    }
}
