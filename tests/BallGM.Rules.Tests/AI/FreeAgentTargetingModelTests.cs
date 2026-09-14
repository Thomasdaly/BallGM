using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;

namespace BallGM.Rules.Tests.AI;

public sealed class FreeAgentTargetingModelTests
{
    [Fact]
    public void OffersAFreeAgentAtANeededPositionTheirAskingPrice()
    {
        var league = FreeAgentTargetingTestLeague.Build()
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000)) // A's need: a weak Center starter.
            .WithFreeAgent("CENTER", Position.Center, overall: 70);

        var candidates = FreeAgentTargetingModel.FindCandidates(league.TeamId("A"), league.FreeAgents, league.Context());

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.Assessment.IsLegal, string.Join("; ", candidate.Assessment.Violations.Select(v => v.Explanation)));
        Assert.Equal(league.TeamId("A"), candidate.ShoppingTeamId);
        Assert.Equal(league.FreeAgentId("CENTER"), candidate.Offer.PlayerId);

        // floor $1,000,000 + (ceiling $25,000,000 - floor) * QualityShare(70)=60% = 15,400,000.
        Assert.Equal(2, candidate.Offer.SeasonCount);
        Assert.All(candidate.Offer.Terms, term => Assert.Equal(15_400_000, term.Compensation.SmallestUnits));

        Assert.Contains(candidate.Rationale, finding => finding.RuleCode == "ai_fa_target.needs_match");
        Assert.Contains(candidate.Rationale, finding => finding.RuleCode == "ai_fa_target.offer_at_asking_price");
    }

    [Fact]
    public void CapsTheOfferTermAtTheLeaguesConfiguredMaximum()
    {
        var league = FreeAgentTargetingTestLeague.Build(maximumContractSeasons: 1)
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000))
            .WithFreeAgent("CENTER", Position.Center, overall: 70);

        var candidates = FreeAgentTargetingModel.FindCandidates(league.TeamId("A"), league.FreeAgents, league.Context());

        var candidate = Assert.Single(candidates);
        Assert.Equal(1, candidate.Offer.SeasonCount);
    }

    [Fact]
    public void SkipsAFreeAgentAtAPositionTheTeamDoesNotNeed()
    {
        var league = FreeAgentTargetingTestLeague.Build()
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.PointGuard, 65, 3_000_000), // A backup, so point guard has depth and no need.
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 70, 8_000_000))
            .WithFreeAgent("PG", Position.PointGuard, overall: 90);

        var candidates = FreeAgentTargetingModel.FindCandidates(league.TeamId("A"), league.FreeAgents, league.Context());

        Assert.Empty(candidates);
    }

    [Fact]
    public void SkipsEveryoneInAnOpenMarketWithNoConfiguredRange()
    {
        var league = FreeAgentTargetingTestLeague.Build(floor: null, ceilingPercentOfSoftCap: null)
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000))
            .WithFreeAgent("CENTER", Position.Center, overall: 70);

        var candidates = FreeAgentTargetingModel.FindCandidates(league.TeamId("A"), league.FreeAgents, league.Context());

        Assert.Empty(candidates);
    }

    [Fact]
    public void ExcludesACandidateWhoseSigningWouldOverfillTheRoster()
    {
        var league = FreeAgentTargetingTestLeague.Build(minimumRoster: 2, maximumRoster: 5)
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000)) // Already at the 5-player maximum.
            .WithFreeAgent("CENTER", Position.Center, overall: 70);

        var candidates = FreeAgentTargetingModel.FindCandidates(league.TeamId("A"), league.FreeAgents, league.Context());

        Assert.Empty(candidates);
    }

    [Fact]
    public void ReturnsNoCandidatesForATeamNotInTheLeague()
    {
        var league = FreeAgentTargetingTestLeague.Build()
            .WithTeam("A", (Position.Center, 40, 3_000_000))
            .WithFreeAgent("CENTER", Position.Center, overall: 70);

        var candidates = FreeAgentTargetingModel.FindCandidates(new TeamId("TEAM-MISSING"), league.FreeAgents, league.Context());

        Assert.Empty(candidates);
    }
}
