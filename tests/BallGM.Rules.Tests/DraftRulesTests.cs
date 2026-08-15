using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class DraftRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsNonPositiveRoundCount(int roundCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DraftRules(
            roundCount,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 1,
            retainedRoundInterval: 2));
    }

    [Fact]
    public void ConstructorAcceptsAFullyConfiguredDraft()
    {
        var draftRules = new DraftRules(
            roundCount: 2,
            lotteryEnabled: false,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 1,
            retainedRoundInterval: 2);

        Assert.Equal(2, draftRules.RoundCount);
        Assert.False(draftRules.LotteryEnabled);
        Assert.Equal(5, draftRules.TradableFutureDraftHorizon);
        Assert.Equal(1, draftRules.RetainedRoundNumber);
        Assert.Equal(2, draftRules.RetainedRoundInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ConstructorRejectsANonPositiveTradableHorizon(int horizon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DraftRules(
            roundCount: 2,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: horizon,
            retainedRoundNumber: 1,
            retainedRoundInterval: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ConstructorRejectsARetainedRoundTheDraftDoesNotHave(int retainedRound)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DraftRules(
            roundCount: 2,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: retainedRound,
            retainedRoundInterval: 2));
    }

    [Fact]
    public void ConstructorRejectsARetentionIntervalBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DraftRules(
            roundCount: 2,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 1,
            retainedRoundInterval: 0));
    }
}
