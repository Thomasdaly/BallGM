using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;

namespace BallGM.Rules.Tests.Draft;

public sealed class DraftLotteryTests
{
    private static readonly FranchiseId Worst = new("franchise-worst");
    private static readonly FranchiseId Second = new("franchise-second");
    private static readonly FranchiseId Third = new("franchise-third");
    private static readonly FranchiseId Best = new("franchise-best");

    [Fact]
    public void RunWithoutALotteryKeepsStraightReverseStandingsOrderInEveryRound()
    {
        var draftRules = DraftRules.Create(
            roundCount: 2, lotteryEnabled: false, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;
        var standingsOrder = new[] { Worst, Second, Third, Best };

        var result = DraftLottery.Run(
            new Season(2030), standingsOrder, draftRules, DraftLotteryRules.None, new ThrowingRandomSource());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SelectionFor(1, Worst));
        Assert.Equal(2, result.Value.SelectionFor(1, Second));
        Assert.Equal(1, result.Value.SelectionFor(2, Worst));
        Assert.Equal(4, result.Value.SelectionFor(2, Best));
    }

    [Fact]
    public void RunWithALotteryDrawsOnlyTheWeightedPoolAndAppendsTheRestInStandingsOrder()
    {
        var draftRules = DraftRules.Create(
            roundCount: 2, lotteryEnabled: true, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;
        var lotteryRules = DraftLotteryRules.Create([3, 2, 1]).Value;
        var standingsOrder = new[] { Worst, Second, Third, Best };

        // Weights [3,2,1] over [Worst,Second,Third]; targets [0, 2, 0] trace to Worst, then Third, then Second.
        var random = new QueueRandomSource(0, 2, 0);

        var result = DraftLottery.Run(new Season(2030), standingsOrder, draftRules, lotteryRules, random);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SelectionFor(1, Worst));
        Assert.Equal(2, result.Value.SelectionFor(1, Third));
        Assert.Equal(3, result.Value.SelectionFor(1, Second));
        Assert.Equal(4, result.Value.SelectionFor(1, Best));

        // Round two is not drawn: plain reverse-standings order regardless of round one's result.
        Assert.Equal(1, result.Value.SelectionFor(2, Worst));
        Assert.Equal(2, result.Value.SelectionFor(2, Second));
        Assert.Equal(3, result.Value.SelectionFor(2, Third));
        Assert.Equal(4, result.Value.SelectionFor(2, Best));
    }

    [Fact]
    public void RunFailsWhenTheLeagueHoldsNoDraft()
    {
        var result = DraftLottery.Run(
            new Season(2030), [Worst], DraftRules.NoDraft, DraftLotteryRules.None, new ThrowingRandomSource());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_lottery.no_draft");
    }

    [Fact]
    public void RunFailsWithNoStandingsSupplied()
    {
        var draftRules = DraftRules.Create(
            roundCount: 1, lotteryEnabled: false, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;

        var result = DraftLottery.Run(
            new Season(2030), [], draftRules, DraftLotteryRules.None, new ThrowingRandomSource());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_lottery.empty_standings");
    }

    [Fact]
    public void RunFailsWhenTheLotteryPoolIsLargerThanTheLeague()
    {
        var draftRules = DraftRules.Create(
            roundCount: 1, lotteryEnabled: true, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;
        var lotteryRules = DraftLotteryRules.Create([3, 2, 1, 1, 1]).Value;

        var result = DraftLottery.Run(
            new Season(2030), [Worst, Second], draftRules, lotteryRules, new ThrowingRandomSource());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_lottery.pool_larger_than_league");
    }

    private sealed class QueueRandomSource(params int[] draws) : IRandomSource
    {
        private int _index;

        public int NextInt32(int minInclusive, int maxExclusive) => draws[_index++];
    }

    private sealed class ThrowingRandomSource : IRandomSource
    {
        public int NextInt32(int minInclusive, int maxExclusive) =>
            throw new InvalidOperationException("No randomness should have been drawn.");
    }
}
