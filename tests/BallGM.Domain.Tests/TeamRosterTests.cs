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

    /// <summary>
    /// A squad short of the roster minimum is a squad with signings still to make, not a squad that
    /// cannot exist. The minimum is an obligation the cap sheet prices as roster-slot holds; making
    /// it a construction invariant would mean no team could ever be in the state the holds describe,
    /// and free agency would have nowhere to happen.
    /// </summary>
    [Fact]
    public void CreateAcceptsARosterBelowTheMinimumBecauseThatIsAnObligationRatherThanAnImpossibility()
    {
        var result = Team.Create(
            new TeamId("team-001"),
            new FranchiseId("franchise-001"),
            "Fictional City Five",
            new RosterSizeLimits(minimumPlayers: 2, maximumPlayers: 5),
            initialPlayers: [new PlayerId("player-001")]);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(1, result.Value.RosterCount);
    }

    /// <summary>
    /// The maximum is the other kind of rule, and stays a refusal: a team over its limit is not a
    /// team with something left to do, it is a team in a state the league forbids outright.
    /// </summary>
    [Fact]
    public void CreateStillRefusesARosterAboveTheMaximum()
    {
        var result = Team.Create(
            new TeamId("team-001"),
            new FranchiseId("franchise-001"),
            "Fictional City Five",
            new RosterSizeLimits(minimumPlayers: 1, maximumPlayers: 2),
            initialPlayers: [new PlayerId("player-001"), new PlayerId("player-002"), new PlayerId("player-003")]);

        Assert.True(result.IsFailure);
        Assert.Equal("roster.maximum_exceeded", Assert.Single(result.Errors).Code);
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

    /// <summary>
    /// A contract's natural expiry at a season boundary is not a GM's voluntary cut, and the roster
    /// minimum obligation the ordinary release path enforces would refuse the exact state a season
    /// boundary needs to reach.
    /// </summary>
    [Fact]
    public void ReleaseExpiredPlayerDropsBelowTheRosterMinimumWithoutRefusing()
    {
        var playerId = new PlayerId("player-001");
        var team = CreateTeam(
            new RosterSizeLimits(minimumPlayers: 1, maximumPlayers: 2),
            playerId);

        var result = team.ReleaseExpiredPlayer(playerId);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.DoesNotContain(playerId, team.PlayerIds);
        Assert.Equal(0, team.RosterCount);
    }

    [Fact]
    public void ReleaseExpiredPlayerRejectsPlayersNotOnRoster()
    {
        var playerId = new PlayerId("player-001");
        var team = CreateTeam(new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 2));

        var result = team.ReleaseExpiredPlayer(playerId);

        Assert.True(result.IsFailure);
        Assert.Equal("roster.player_not_on_team", Assert.Single(result.Errors).Code);
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
