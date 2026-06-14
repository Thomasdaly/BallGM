using BallGM.Domain;

namespace BallGM.Application.Overview;

public sealed class GetBallGmOverviewQuery
{
    public BallGmOverview Execute()
    {
        return new BallGmOverview(
            BallGmProduct.Name,
            BallGmProduct.ArchitectureStage,
            "Avalonia client -> Application -> Domain");
    }
}
