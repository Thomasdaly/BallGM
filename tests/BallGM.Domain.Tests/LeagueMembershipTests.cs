using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class LeagueMembershipTests
{
    [Fact]
    public void CreateSucceedsWithUniqueTeamIds()
    {
        var teamIds = new[] { new TeamId("team-001"), new TeamId("team-002") };

        var result = League.Create(new LeagueId("league-001"), "Fictional Association", teamIds);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TeamIds.Count);
    }

    [Fact]
    public void CreateReturnsStructuredFailureForDuplicateTeamsInsteadOfThrowing()
    {
        var teamId = new TeamId("team-001");

        var result = League.Create(new LeagueId("league-001"), "Fictional Association", [teamId, teamId]);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("league.duplicate_team_membership", error.Code);
    }
}
