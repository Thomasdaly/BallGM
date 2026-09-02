using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class RetirementRulesTests
{
    [Fact]
    public void CreateSucceedsWithAValidAgeRange()
    {
        var result = RetirementRules.Create(minimumVoluntaryAge: 30, mandatoryRetirementAge: 42, voluntaryOddsByAge: null);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
        Assert.True(result.Value.HasMandatoryAge);
    }

    [Fact]
    public void CreateSucceedsWithNoMandatoryAge()
    {
        var result = RetirementRules.Create(minimumVoluntaryAge: 30, mandatoryRetirementAge: 0, voluntaryOddsByAge: null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasMandatoryAge);
    }

    [Fact]
    public void NoneModelsNoRetirementAtAll()
    {
        Assert.False(RetirementRules.None.IsConfigured);
    }

    [Fact]
    public void CreateRejectsANonPositiveMinimumAge()
    {
        var result = RetirementRules.Create(minimumVoluntaryAge: 0, mandatoryRetirementAge: 0, voluntaryOddsByAge: null);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_retirement_age_range");
    }

    [Fact]
    public void CreateRejectsAMandatoryAgeBelowTheMinimum()
    {
        var result = RetirementRules.Create(minimumVoluntaryAge: 30, mandatoryRetirementAge: 25, voluntaryOddsByAge: null);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_retirement_age_range");
    }
}
