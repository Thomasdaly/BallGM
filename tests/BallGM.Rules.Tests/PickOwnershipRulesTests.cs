using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Rules.Configuration;
using BallGM.Rules.DraftAssets;

namespace BallGM.Rules.Tests;

/// <summary>
/// The validation the trade engine will call at Milestone 5. Nothing here executes a transfer —
/// a validator that mutates cannot be asked "would this be legal" while a proposal is still being
/// put together.
/// </summary>
public sealed class PickOwnershipRulesTests
{
    private static readonly LeagueId League = new(SortableId.NewId());
    private static readonly Season CurrentSeason = new(2031);

    /// <summary>Two rounds, four tradable future drafts, and a first-rounder retained every two drafts.</summary>
    private static readonly DraftRules Rules = DraftRules.Create(
        roundCount: 2,
        lotteryEnabled: true,
        tradableFutureDraftHorizon: 4,
        retainedRoundNumber: 1,
        retainedRoundInterval: 2).Value;

    [Fact]
    public void OrdinaryTransfer_OfAFranchisesOwnPickIsAllowedWhileItStillKeepsEnoughOfThem()
    {
        var league = new TestLeague();
        var pick = league.Own(league.First, 2032, 1);

        var result = Validate(league, pick, league.First, league.Second);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void Transfer_IsRejectedWhenTheFranchiseDoesNotControlThePick()
    {
        var league = new TestLeague();
        var pick = league.Own(league.Second, 2032, 1);

        var result = Validate(league, pick, league.First, league.Third);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.not_controlled");
    }

    [Fact]
    public void Transfer_IsRejectedWhenTheReceivingFranchiseAlreadyControlsThePick()
    {
        var league = new TestLeague();
        var pick = league.Own(league.First, 2032, 1);

        var result = Validate(league, pick, league.First, league.First);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.already_owned");
    }

    [Fact]
    public void Transfer_IsRejectedBeyondTheConfiguredTradableHorizon()
    {
        var league = new TestLeague();
        var pick = league.Register(league.First, CurrentSeason.Year + Rules.TradableFutureDraftHorizon + 1, 1);

        var result = Validate(league, pick, league.First, league.Second);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.outside_tradable_horizon");
    }

    [Fact]
    public void Transfer_IsRejectedWhileThePickIsStillPromisedToAThirdFranchise()
    {
        var league = new TestLeague();
        var pick = league.Own(league.First, 2033, 1);
        league.Promise(pick, league.Third, [4]);

        var result = Validate(league, pick, league.First, league.Second);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.conflicting_encumbrance");
    }

    [Fact]
    public void Transfer_IsRejectedWhenItWouldBreakTheConsecutiveFutureDraftRestriction()
    {
        var league = new TestLeague();

        // 2033 is already gone. Giving up 2032 as well would leave the 2032–2033 pair with no
        // first-round pick of the franchise's own in it.
        league.Move(league.Own(league.First, 2033, 1), league.Second);
        var pick = league.Own(league.First, 2032, 1);

        var result = Validate(league, pick, league.First, league.Second);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pick_transfer.retained_round_restriction", error.Code);
        Assert.Contains("2032 to 2033", error.Message);
    }

    [Fact]
    public void Retention_CountsAPickCarryingAPendingObligationAsAlreadyGone()
    {
        var league = new TestLeague();

        // Not traded, but owed away conditionally — a rule satisfied by an asset the franchise may
        // lose is not a retention rule.
        league.Promise(league.Own(league.First, 2033, 1), league.Second, [4]);
        var pick = league.Own(league.First, 2032, 1);

        var result = Validate(league, pick, league.First, league.Second);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.retained_round_restriction");
    }

    [Fact]
    public void Retention_DoesNotRestrictPicksAcquiredFromOtherFranchises()
    {
        var league = new TestLeague();

        // The first franchise has already given up its own next three firsts, so it is at the limit
        // on its own assets — but somebody else's pick is not what the rule protects.
        league.Move(league.Own(league.First, 2032, 1), league.Second);
        league.Move(league.Own(league.First, 2033, 1), league.Second);
        var acquired = league.Own(league.Second, 2032, 1);
        league.Move(acquired, league.First);

        var result = Validate(league, acquired, league.First, league.Third);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void Retention_DoesNotRestrictARoundTheRulesetDoesNotProtect()
    {
        var league = new TestLeague();
        league.Move(league.Own(league.First, 2032, 2), league.Second);
        var secondRounder = league.Own(league.First, 2033, 2);

        var result = Validate(league, secondRounder, league.First, league.Second);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void Encumbrance_IsRejectedWhenTheFranchiseDoesNotControlThePick()
    {
        var league = new TestLeague();
        var pick = league.Own(league.Second, 2032, 1);
        var obligation = new PickObligation(
            new PickEncumbranceId(SortableId.NewId()),
            league.Third,
            PickProtection.Unprotected);

        var result = new PickOwnershipRules().ValidateEncumbrance(
            league.Book,
            pick.Id,
            league.First,
            obligation,
            CurrentSeason,
            Rules);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.not_controlled");
    }

    [Fact]
    public void Encumbrance_IsRejectedWhenThePickIsAlreadyPromisedElsewhere()
    {
        var league = new TestLeague();
        var pick = league.Own(league.First, 2033, 1);
        league.Promise(pick, league.Second, [4]);

        var second = new PickObligation(
            new PickEncumbranceId(SortableId.NewId()),
            league.Third,
            PickProtection.Unprotected);

        var result = new PickOwnershipRules().ValidateEncumbrance(
            league.Book,
            pick.Id,
            league.First,
            second,
            CurrentSeason,
            Rules);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.conflicting_encumbrance");
    }

    [Fact]
    public void Encumbrance_IsRejectedWhenTheSwapCounterpartIsNotARegisteredAsset()
    {
        var league = new TestLeague();
        var pick = league.Own(league.First, 2032, 1);
        var swap = new SwapRight(
            new PickEncumbranceId(SortableId.NewId()),
            league.Second,
            new DraftPickId("PICK-THAT-DOES-NOT-EXIST"));

        var result = new PickOwnershipRules().ValidateEncumbrance(
            league.Book,
            pick.Id,
            league.First,
            swap,
            CurrentSeason,
            Rules);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.unknown_pick");
    }

    [Fact]
    public void Encumbrance_IsHeldToTheSameRetentionRestrictionAsATransfer()
    {
        var league = new TestLeague();
        league.Move(league.Own(league.First, 2033, 1), league.Second);
        var pick = league.Own(league.First, 2032, 1);

        var obligation = new PickObligation(
            new PickEncumbranceId(SortableId.NewId()),
            league.Second,
            PickProtection.Unprotected);

        var result = new PickOwnershipRules().ValidateEncumbrance(
            league.Book,
            pick.Id,
            league.First,
            obligation,
            CurrentSeason,
            Rules);

        // A franchise cannot get around "keep a first" by owing every one of them away instead.
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pick_transfer.retained_round_restriction");
    }

    private static DomainOperationResult Validate(TestLeague league, DraftPick pick, FranchiseId from, FranchiseId to) =>
        new PickOwnershipRules().ValidateTransfer(league.Book, pick.Id, from, to, CurrentSeason, Rules);

    /// <summary>Three franchises, each with their own picks across the whole tradable horizon.</summary>
    private sealed class TestLeague
    {
        public TestLeague()
        {
            Book = new DraftAssetBook(League);

            foreach (var franchise in new[] { First, Second, Third })
            {
                for (var offset = 0; offset <= Rules.TradableFutureDraftHorizon; offset++)
                {
                    for (var round = 1; round <= Rules.RoundCount; round++)
                    {
                        Register(franchise, CurrentSeason.Year + offset, round);
                    }
                }
            }
        }

        public DraftAssetBook Book { get; }

        public FranchiseId First { get; } = new("FRANCHISE-FIRST");

        public FranchiseId Second { get; } = new("FRANCHISE-SECOND");

        public FranchiseId Third { get; } = new("FRANCHISE-THIRD");

        public DraftPick Register(FranchiseId original, int year, int round)
        {
            var pick = DraftPick.Create(
                new DraftPickId($"PICK-{original.Value}-{year}-R{round}"),
                League,
                new Season(year),
                round,
                original).Value;

            Assert.True(Book.Register(pick).IsSuccess);
            return pick;
        }

        public DraftPick Own(FranchiseId original, int year, int round) =>
            Book.Find(new Season(year), round, original)!;

        public void Move(DraftPick pick, FranchiseId to) =>
            Assert.True(Book.Transfer(pick.Id, to).IsSuccess);

        public void Promise(DraftPick pick, FranchiseId beneficiary, int[] protectedSelections)
        {
            var protection = PickProtection.TopSelections(protectedSelections, PickProtectionFallback.Extinguishes).Value;
            var obligation = new PickObligation(new PickEncumbranceId(SortableId.NewId()), beneficiary, protection);
            Assert.True(Book.Encumber(pick.Id, obligation).IsSuccess);
        }
    }
}
