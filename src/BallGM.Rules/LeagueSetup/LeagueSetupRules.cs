using BallGM.Domain.Common;

namespace BallGM.Rules.LeagueSetup;

public sealed class LeagueSetupRules
{
    public DomainOperationResult ValidateLeagueCanStart(int franchiseCount)
    {
        if (franchiseCount >= 2)
        {
            return DomainOperationResult.Success;
        }

        return DomainOperationResult.Failure(
            new DomainError(
                "league.minimum_franchises",
                "A fictional league must have at least two franchises before simulation can start."));
    }
}
