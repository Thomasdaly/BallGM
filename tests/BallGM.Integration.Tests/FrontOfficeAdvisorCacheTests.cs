using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.Fixtures;

namespace BallGM.Integration.Tests;

/// <summary>
/// The advisor reuses each team's depth chart between trade searches. Teams and players are mutable, so
/// a roster changed in place — same snapshot object — must be read as it is now, not as it was cached.
/// </summary>
public sealed class FrontOfficeAdvisorCacheTests
{
    [Fact]
    public void APlayerWhoseRatingChangesInPlace_IsReadAsHeIsNow_NotFromTheCachedChart()
    {
        var snapshot = new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 5).Load().Value;
        var advisor = new RulesFrontOfficeAdvisor(new RulesCapLedger());

        var (shopper, first) = snapshot.Teams
            .Select(team => (team, Candidates: advisor.FindTradeTargets(team.Id, snapshot).Value))
            .First(entry => entry.Candidates.Count > 0);
        var target = first[0].Proposal.Movements.First(movement => movement.ToTeamId == shopper.Id && movement.PlayerId is not null);
        var player = snapshot.Players.Single(candidate => candidate.Id == target.PlayerId);
        Assert.True(player.Develop(new PlayerRating(99)).IsSuccess);

        var again = advisor.FindTradeTargets(shopper.Id, snapshot).Value;
        var fresh = new RulesFrontOfficeAdvisor(new RulesCapLedger()).FindTradeTargets(shopper.Id, snapshot).Value;

        Assert.NotEqual(first.Select(Describe), fresh.Select(Describe));
        Assert.Equal(fresh.Select(Describe), again.Select(Describe));
    }

    private static string Describe(BallGM.Domain.AI.TradeTargetCandidate candidate) =>
        string.Join(";", candidate.Proposal.Movements.Select(movement => $"{movement.FromTeamId.Value}>{movement.ToTeamId.Value}:{movement.PlayerId?.Value}"));
}
