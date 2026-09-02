using BallGM.Domain.Common;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Development;

namespace BallGM.Rules.Tests.Development;

public sealed class RetirementModelTests
{
    [Fact]
    public void AssessWithNoRulesNeverRetires()
    {
        var result = RetirementModel.Assess(age: 50, RetirementRules.None, new ThrowingRandomSource());

        Assert.False(result.Retires);
        Assert.Equal("retirement.not_configured", result.Finding.RuleCode);
    }

    [Fact]
    public void AssessBelowTheMinimumAgeNeverRetires()
    {
        var rules = RetirementRules.Create(minimumVoluntaryAge: 30, mandatoryRetirementAge: 0, voluntaryOddsByAge: null).Value;

        var result = RetirementModel.Assess(age: 25, rules, new ThrowingRandomSource());

        Assert.False(result.Retires);
        Assert.Equal("retirement.below_minimum_age", result.Finding.RuleCode);
    }

    [Fact]
    public void AssessAtOrAboveTheMandatoryAgeAlwaysRetiresWithoutDrawing()
    {
        var rules = RetirementRules.Create(minimumVoluntaryAge: 30, mandatoryRetirementAge: 42, voluntaryOddsByAge: null).Value;

        var result = RetirementModel.Assess(age: 42, rules, new ThrowingRandomSource());

        Assert.True(result.Retires);
        Assert.Equal("retirement.mandatory_age", result.Finding.RuleCode);
    }

    [Fact]
    public void AssessDrawsVoluntaryRetirementAgainstTheStatedOdds()
    {
        var odds = BandedScale.Create([new ScaleBand(0, 0), new ScaleBand(35, 5_000)]).Value;
        var rules = RetirementRules.Create(minimumVoluntaryAge: 30, mandatoryRetirementAge: 0, odds).Value;

        var retires = RetirementModel.Assess(age: 35, rules, new QueueRandomSource(4_999));
        var continues = RetirementModel.Assess(age: 35, rules, new QueueRandomSource(5_000));

        Assert.True(retires.Retires);
        Assert.Equal("retirement.voluntary_drawn", retires.Finding.RuleCode);

        Assert.False(continues.Retires);
        Assert.Equal("retirement.continues_playing", continues.Finding.RuleCode);
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
