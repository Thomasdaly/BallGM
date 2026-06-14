using BallGM.Rules.Validation;

namespace BallGM.Rules.LeagueSetup;

public sealed class LeagueSetupRules
{
    public RuleValidationResult ValidateLeagueCanStart(int franchiseCount)
    {
        if (franchiseCount >= 2)
        {
            return RuleValidationResult.Valid;
        }

        return RuleValidationResult.Invalid(
            new RuleViolation(
                "league.minimum_franchises",
                "A fictional league must have at least two franchises before simulation can start."));
    }
}
