using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;

namespace BallGM.Rules.Tests.Draft;

public sealed class ScoutingModelTests
{
    [Fact]
    public void AssessWithNoScoutingRulesRevealsTheTrueRatingExactly()
    {
        var result = ScoutingModel.Assess(new PlayerRating(overall: 77), ScoutingRules.None, investedPoints: 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(77, result.Value.LowerBound);
        Assert.Equal(77, result.Value.UpperBound);
        Assert.Equal(100, result.Value.Confidence);
    }

    [Fact]
    public void AssessAtZeroConfidenceProducesTheFullWidthCenteredOnTheTrueRating()
    {
        var rules = ScoutingRules.Create(baseConfidence: 0, maxRangeWidth: 40).Value;

        var result = ScoutingModel.Assess(new PlayerRating(overall: 50), rules, investedPoints: 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Confidence);
        Assert.Equal(30, result.Value.LowerBound);
        Assert.Equal(70, result.Value.UpperBound);
    }

    [Fact]
    public void AssessNarrowsTheRangeAsInvestmentConfidenceIncreases()
    {
        var scale = BandedScale.Create([new ScaleBand(0, 0), new ScaleBand(10, 50)]).Value;
        var rules = ScoutingRules.Create(baseConfidence: 0, maxRangeWidth: 40, scale).Value;

        var unscouted = ScoutingModel.Assess(new PlayerRating(overall: 50), rules, investedPoints: 0).Value;
        var scouted = ScoutingModel.Assess(new PlayerRating(overall: 50), rules, investedPoints: 10).Value;

        Assert.Equal(0, unscouted.Confidence);
        Assert.Equal(50, scouted.Confidence);
        Assert.True(scouted.UpperBound - scouted.LowerBound < unscouted.UpperBound - unscouted.LowerBound);
    }

    [Fact]
    public void AssessClampsTheBandToTheRatingScaleNearAnExtreme()
    {
        var rules = ScoutingRules.Create(baseConfidence: 0, maxRangeWidth: 40).Value;

        var result = ScoutingModel.Assess(new PlayerRating(overall: 5), rules, investedPoints: 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.LowerBound);
        Assert.True(result.Value.UpperBound <= 100);
    }

    [Fact]
    public void AssessRejectsNegativeInvestment()
    {
        var result = ScoutingModel.Assess(new PlayerRating(overall: 50), ScoutingRules.None, investedPoints: -1);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "scouting.negative_investment");
    }
}
