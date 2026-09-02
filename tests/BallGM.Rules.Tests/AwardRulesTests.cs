using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class AwardRulesTests
{
    [Fact]
    public void CreateSucceedsWithDistinctCodes()
    {
        var result = AwardRules.Create([
            new AwardDefinition("mvp", "Most Valuable Player", AwardStatBasis.TotalPoints),
            new AwardDefinition("apg-leader", "Assists Leader", AwardStatBasis.TotalAssists),
        ]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
        Assert.Equal(2, result.Value.Awards.Count);
    }

    [Fact]
    public void CreateWithNoAwardsIsNone()
    {
        var result = AwardRules.Create([]);

        Assert.True(result.IsSuccess);
        Assert.Same(AwardRules.None, result.Value);
    }

    [Fact]
    public void CreateRejectsAMissingCode()
    {
        var result = AwardRules.Create([new AwardDefinition("", "Most Valuable Player", AwardStatBasis.TotalPoints)]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_missing_code");
    }

    [Fact]
    public void CreateRejectsAMissingName()
    {
        var result = AwardRules.Create([new AwardDefinition("mvp", "", AwardStatBasis.TotalPoints)]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_missing_name");
    }

    [Fact]
    public void CreateRejectsADuplicateCode()
    {
        var result = AwardRules.Create([
            new AwardDefinition("mvp", "Most Valuable Player", AwardStatBasis.TotalPoints),
            new AwardDefinition("mvp", "Most Valuable Player Again", AwardStatBasis.TotalAssists),
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_duplicate_code");
    }
}
