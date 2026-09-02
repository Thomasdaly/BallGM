using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Development;

namespace BallGM.Rules.Tests.Development;

public sealed class PlayerDevelopmentModelTests
{
    private static readonly BandedScale GrowthCurve = BandedScale.Create([new ScaleBand(0, 3), new ScaleBand(22, 1)]).Value;
    private static readonly BandedScale DeclineCurve = BandedScale.Create([new ScaleBand(0, 2), new ScaleBand(34, 4)]).Value;

    [Fact]
    public void DevelopWithNoRulesLeavesTheRatingUnchanged()
    {
        var result = PlayerDevelopmentModel.Develop(new PlayerRating(overall: 60), age: 20, DevelopmentRules.None, new ThrowingRandomSource());

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.Overall);
    }

    [Fact]
    public void DevelopGrowsAYoungPlayerBelowThePeakRange()
    {
        var rules = DevelopmentRules.Create(peakAgeStart: 26, peakAgeEnd: 29, GrowthCurve, DeclineCurve, varianceRange: 0).Value;

        var result = PlayerDevelopmentModel.Develop(new PlayerRating(overall: 60), age: 20, rules, new ThrowingRandomSource());

        Assert.Equal(63, result.Value.Overall);
    }

    [Fact]
    public void DevelopDeclinesAnOldPlayerAbovePeak()
    {
        var rules = DevelopmentRules.Create(peakAgeStart: 26, peakAgeEnd: 29, GrowthCurve, DeclineCurve, varianceRange: 0).Value;

        var result = PlayerDevelopmentModel.Develop(new PlayerRating(overall: 60), age: 35, rules, new ThrowingRandomSource());

        Assert.Equal(56, result.Value.Overall);
    }

    [Fact]
    public void DevelopLeavesAPlayerInsideThePeakRangeUnchangedBeforeVariance()
    {
        var rules = DevelopmentRules.Create(peakAgeStart: 26, peakAgeEnd: 29, GrowthCurve, DeclineCurve, varianceRange: 0).Value;

        var result = PlayerDevelopmentModel.Develop(new PlayerRating(overall: 60), age: 27, rules, new ThrowingRandomSource());

        Assert.Equal(60, result.Value.Overall);
    }

    [Fact]
    public void DevelopAppliesVarianceOnTopOfTheCurve()
    {
        var rules = DevelopmentRules.Create(peakAgeStart: 26, peakAgeEnd: 29, GrowthCurve, DeclineCurve, varianceRange: 2).Value;

        var result = PlayerDevelopmentModel.Develop(new PlayerRating(overall: 60), age: 27, rules, new QueueRandomSource(2));

        // Peak range curve delta is 0; a variance draw of 2 (within [-2, 2]) lands the total at +2.
        Assert.Equal(62, result.Value.Overall);
    }

    private sealed class QueueRandomSource(params int[] draws) : IRandomSource
    {
        private int _index;

        public int NextInt32(int minInclusive, int maxExclusive) => draws[_index++];
    }

    private sealed class ThrowingRandomSource : IRandomSource
    {
        public int NextInt32(int minInclusive, int maxExclusive) =>
            throw new InvalidOperationException("No randomness should have been drawn with zero variance range.");
    }
}
