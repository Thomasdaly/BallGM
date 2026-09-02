using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class DraftLotteryRulesTests
{
    [Fact]
    public void CreateSucceedsWithPositiveWeights()
    {
        var result = DraftLotteryRules.Create([140, 140, 140, 125]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
        Assert.Equal(4, result.Value.Weights.Count);
    }

    [Fact]
    public void CreateWithNoWeightsIsNone()
    {
        var result = DraftLotteryRules.Create([]);

        Assert.True(result.IsSuccess);
        Assert.Same(DraftLotteryRules.None, result.Value);
    }

    [Fact]
    public void CreateRejectsANonPositiveWeight()
    {
        var result = DraftLotteryRules.Create([140, 0, 125]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.non_positive_lottery_weight");
    }
}
