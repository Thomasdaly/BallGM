using BallGM.Domain.Common;
using BallGM.Rules.LeagueSetup;

namespace BallGM.Rules.Tests;

public sealed class LeagueSetupRulesTests
{
    [Fact]
    public void ValidateLeagueCanStartReturnsStructuredViolationWhenTooFewFranchises()
    {
        var rules = new LeagueSetupRules();

        var result = rules.ValidateLeagueCanStart(franchiseCount: 1);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("league.minimum_franchises", error.Code);
        Assert.Contains("at least two franchises", error.Message);
    }

    [Fact]
    public void ValidateLeagueCanStartSucceedsWithTwoOrMoreFranchises()
    {
        var rules = new LeagueSetupRules();

        var result = rules.ValidateLeagueCanStart(franchiseCount: 2);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FailureRequiresAtLeastOneError()
    {
        Assert.Throws<ArgumentException>(() => DomainOperationResult.Failure());
    }
}
