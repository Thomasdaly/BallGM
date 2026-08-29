using BallGM.Domain.Leagues;
using BallGM.Domain.Seasons;
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

    [Fact]
    public void RecordSeasonArchivesTheEntry()
    {
        var league = League.Create(new LeagueId("league-001"), "Fictional Association", [new TeamId("team-001")]).Value;
        var entry = new SeasonHistoryEntry(new Season(2031), new TeamId("team-001"), []);

        var result = league.RecordSeason(entry);

        Assert.True(result.IsSuccess);
        Assert.Same(entry, Assert.Single(league.History));
    }

    [Fact]
    public void RecordSeasonRefusesASecondEntryForTheSameYear()
    {
        var league = League.Create(new LeagueId("league-001"), "Fictional Association", [new TeamId("team-001")]).Value;
        Assert.True(league.RecordSeason(new SeasonHistoryEntry(new Season(2031), null, [])).IsSuccess);

        var result = league.RecordSeason(new SeasonHistoryEntry(new Season(2031), new TeamId("team-001"), []));

        Assert.True(result.IsFailure);
        Assert.Equal("league.season_already_concluded", Assert.Single(result.Errors).Code);
        Assert.Single(league.History);
    }
}
