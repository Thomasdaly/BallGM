using BallGM.Rules.LeagueSetup;
using BallGM.Rules.Validation;

namespace BallGM.Rules.Tests;

public sealed class LeagueSetupRulesTests
{
    [Fact]
    public void ValidateLeagueCanStartReturnsStructuredViolationWhenTooFewFranchises()
    {
        var rules = new LeagueSetupRules();

        var result = rules.ValidateLeagueCanStart(franchiseCount: 1);

        Assert.False(result.IsValid);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("league.minimum_franchises", violation.Code);
        Assert.Contains("at least two franchises", violation.Explanation);
    }

    [Fact]
    public void RuleValidationResultCopiesViolationInputs()
    {
        var violations = new[]
        {
            new RuleViolation("league.minimum_franchises", "A fictional league must have at least two franchises.")
        };

        var result = RuleValidationResult.Invalid(violations);
        violations[0] = new RuleViolation("changed", "Changed after result creation.");

        var violation = Assert.Single(result.Violations);
        Assert.Equal("league.minimum_franchises", violation.Code);
    }

    [Fact]
    public void InvalidValidationResultRequiresAtLeastOneViolation()
    {
        Assert.Throws<ArgumentException>(() => RuleValidationResult.Invalid());
    }
}
