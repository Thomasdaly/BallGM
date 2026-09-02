using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class DevelopmentRulesTests
{
    [Fact]
    public void CreateSucceedsWithAValidPeakRange()
    {
        var result = DevelopmentRules.Create(peakAgeStart: 26, peakAgeEnd: 29, growthCurve: null, declineCurve: null, varianceRange: 2);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
    }

    [Fact]
    public void NoneModelsNoAgeingAtAll()
    {
        Assert.False(DevelopmentRules.None.IsConfigured);
    }

    [Theory]
    [InlineData(0, 29)]
    [InlineData(30, 29)]
    public void CreateRejectsAnInvalidPeakRange(int start, int end)
    {
        var result = DevelopmentRules.Create(start, end, null, null, varianceRange: 0);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_development_peak_range");
    }

    [Fact]
    public void CreateRejectsNegativeVariance()
    {
        var result = DevelopmentRules.Create(26, 29, null, null, varianceRange: -1);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.negative_development_variance");
    }
}
