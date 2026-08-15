using BallGM.Domain.Franchises;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class TeamRosterTests
{
    [Fact]
    public void AddPlayerAddsMembershipWhenRosterHasRoom()
    {
        var team = CreateTeam(new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 2));
        var playerId = new PlayerId("player-001");

        var result = team.AddPlayer(playerId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Contains(playerId, team.PlayerIds);
        Assert.Equal(1, team.RosterCount);
    }

    [Fact]
    public void AddPlayerRejectsDuplicateMembership()
    {
        var playerId = new PlayerId("player-001");
        var team = CreateTeam(
            new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 2),
            playerId);

        var result = team.AddPlayer(playerId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("roster.player_already_on_team", error.Code);
        Assert.Contains("already on team", error.Message);
        Assert.Equal(1, team.RosterCount);
    }

    [Fact]
    public void RemovePlayerRemovesExistingMembership()
    {
        var playerId = new PlayerId("player-001");
        var team = CreateTeam(
            new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 2),
            playerId);

        var result = team.RemovePlayer(playerId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.DoesNotContain(playerId, team.PlayerIds);
        Assert.Equal(0, team.RosterCount);
    }

    [Fact]
    public void RemovePlayerRejectsPlayersNotOnRoster()
    {
        var playerId = new PlayerId("player-001");
        var team = CreateTeam(new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 2));

        var result = team.RemovePlayer(playerId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("roster.player_not_on_team", error.Code);
        Assert.Contains("is not on team", error.Message);
        Assert.Empty(team.PlayerIds);
    }

    [Fact]
    public void AddPlayerRejectsMaximumRosterLimit()
    {
        var firstPlayerId = new PlayerId("player-001");
        var secondPlayerId = new PlayerId("player-002");
        var team = CreateTeam(
            new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 1),
            firstPlayerId);

        var result = team.AddPlayer(secondPlayerId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("roster.maximum_exceeded", error.Code);
        Assert.Contains("roster maximum", error.Message);
        Assert.DoesNotContain(secondPlayerId, team.PlayerIds);
        Assert.Equal(1, team.RosterCount);
    }

    [Fact]
    public void RemovePlayerRejectsMinimumRosterLimit()
    {
        var playerId = new PlayerId("player-001");
        var team = CreateTeam(
            new RosterSizeLimits(minimumPlayers: 1, maximumPlayers: 2),
            playerId);

        var result = team.RemovePlayer(playerId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("roster.minimum_required", error.Code);
        Assert.Contains("roster minimum", error.Message);
        Assert.Contains(playerId, team.PlayerIds);
        Assert.Equal(1, team.RosterCount);
    }

    [Fact]
    public void CreateReturnsStructuredFailureForUndersizedInitialRosterInsteadOfThrowing()
    {
        var result = Team.Create(
            new TeamId("team-001"),
            new FranchiseId("franchise-001"),
            "Fictional City Five",
            new RosterSizeLimits(minimumPlayers: 2, maximumPlayers: 5),
            initialPlayers: [new PlayerId("player-001")]);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("roster.minimum_required", error.Code);
    }

    [Fact]
    public void CreateReturnsStructuredFailureForDuplicateInitialPlayersInsteadOfThrowing()
    {
        var playerId = new PlayerId("player-001");

        var result = Team.Create(
            new TeamId("team-001"),
            new FranchiseId("franchise-001"),
            "Fictional City Five",
            new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 5),
            initialPlayers: [playerId, playerId]);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("roster.initial_duplicate_players", error.Code);
    }

    private static Team CreateTeam(RosterSizeLimits rosterLimits, params PlayerId[] initialPlayers)
    {
        return Team.Create(
            new TeamId("team-001"),
            new FranchiseId("franchise-001"),
            "Fictional City Five",
            rosterLimits,
            initialPlayers).Value;
    }
}
