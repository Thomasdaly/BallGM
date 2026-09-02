using BallGM.Domain.Common;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class ScoutingRulesTests
{
    [Fact]
    public void CreateSucceedsWithValidValues()
    {
        var result = ScoutingRules.Create(baseConfidence: 20, maxRangeWidth: 40);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
    }

    [Fact]
    public void NoneModelsNoUncertaintyAtAll()
    {
        Assert.False(ScoutingRules.None.IsConfigured);
        Assert.Equal(100, ScoutingRules.None.BaseConfidence);
        Assert.Equal(0, ScoutingRules.None.MaxRangeWidth);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void CreateRejectsAnOutOfRangeBaseConfidence(int confidence)
    {
        var result = ScoutingRules.Create(baseConfidence: confidence, maxRangeWidth: 40);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_scouting_confidence");
    }

    [Fact]
    public void CreateRejectsANegativeRangeWidth()
    {
        var result = ScoutingRules.Create(baseConfidence: 20, maxRangeWidth: -1);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.negative_scouting_range_width");
    }

    [Fact]
    public void CreateAcceptsAnInvestmentConfidenceScale()
    {
        var scale = BandedScale.Create([new ScaleBand(0, 0), new ScaleBand(10, 30)]).Value;

        var result = ScoutingRules.Create(baseConfidence: 10, maxRangeWidth: 40, scale);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value.InvestmentConfidence.ValueFor(10));
    }
}
