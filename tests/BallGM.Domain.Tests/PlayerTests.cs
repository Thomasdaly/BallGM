using BallGM.Domain.Players;

namespace BallGM.Domain.Tests;

public sealed class PlayerTests
{
    [Fact]
    public void CreateSucceedsWithValidAttributesAndNoInjury()
    {
        var result = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            new DateOnly(2000, 6, 15),
            seasonsOfService: 4);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsInjured);
        Assert.Null(result.Value.CurrentInjury);
        Assert.Equal(4, result.Value.SeasonsOfService);
    }

    [Fact]
    public void CreateAcceptsAnInitialInjury()
    {
        var result = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            new DateOnly(2000, 6, 15),
            seasonsOfService: 4,
            new Injury("Sprained ankle"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsInjured);
        Assert.Equal("Sprained ankle", result.Value.CurrentInjury!.Description);
    }

    [Fact]
    public void MarkInjuredRecordsInjuryWhenHealthy()
    {
        var player = CreatePlayer();

        var result = player.MarkInjured(new Injury("Twisted knee"));

        Assert.True(result.IsSuccess);
        Assert.True(player.IsInjured);
        Assert.Equal("Twisted knee", player.CurrentInjury!.Description);
    }

    [Fact]
    public void MarkInjuredRejectsWhenAlreadyInjured()
    {
        var player = CreatePlayer();
        player.MarkInjured(new Injury("Twisted knee"));

        var result = player.MarkInjured(new Injury("Sore back"));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("player.already_injured", error.Code);
        Assert.Equal("Twisted knee", player.CurrentInjury!.Description);
    }

    [Fact]
    public void ClearInjuryRemovesInjuryWhenInjured()
    {
        var player = CreatePlayer();
        player.MarkInjured(new Injury("Twisted knee"));

        var result = player.ClearInjury();

        Assert.True(result.IsSuccess);
        Assert.False(player.IsInjured);
    }

    [Fact]
    public void ClearInjuryRejectsWhenHealthy()
    {
        var player = CreatePlayer();

        var result = player.ClearInjury();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("player.not_injured", error.Code);
    }

    [Fact]
    public void DevelopReplacesTheRating()
    {
        var player = CreatePlayer();

        var result = player.Develop(new PlayerRating(overall: 60));

        Assert.True(result.IsSuccess);
        Assert.Equal(60, player.Rating.Overall);
    }

    [Fact]
    public void RetireFlagsThePlayerAsRetired()
    {
        var player = CreatePlayer();

        var result = player.Retire();

        Assert.True(result.IsSuccess);
        Assert.True(player.IsRetired);
    }

    [Fact]
    public void RetireRejectsWhenAlreadyRetired()
    {
        var player = CreatePlayer();
        player.Retire();

        var result = player.Retire();

        Assert.True(result.IsFailure);
        Assert.Equal("player.already_retired", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void CreateDefaultsToAnUnknownBiographyAndNotRetired()
    {
        var player = CreatePlayer();

        Assert.False(player.IsRetired);
        Assert.False(player.Biography.WasDrafted);
    }

    [Fact]
    public void CreateAcceptsABiography()
    {
        var result = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            new DateOnly(2000, 6, 15),
            seasonsOfService: 4,
            biography: new PlayerBiography("Harbourline", "Verdanmoor Institute", 2018, 1, 4));

        Assert.True(result.IsSuccess);
        Assert.Equal("Harbourline", result.Value.Biography.Birthplace);
        Assert.True(result.Value.Biography.WasDrafted);
    }

    /// <summary>
    /// Age is measured against a supplied date rather than the wall clock, so this assertion cannot
    /// start failing on the player's birthday.
    /// </summary>
    [Fact]
    public void AgeIsCountedInCompletedYearsAgainstTheSuppliedDate()
    {
        var player = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            new DateOnly(2000, 6, 15),
            seasonsOfService: 4).Value;

        Assert.Equal(31, player.AgeOn(new DateOnly(2031, 6, 15)));
        Assert.Equal(30, player.AgeOn(new DateOnly(2031, 6, 14)));
    }

    [Fact]
    public void CreateRejectsNegativeSeasonsOfServiceRatherThanThrowing()
    {
        var result = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            new DateOnly(2000, 6, 15),
            seasonsOfService: -1);

        Assert.True(result.IsFailure);
        Assert.Equal("player.negative_seasons_of_service", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void CreateRejectsAMissingBirthDateRatherThanDefaultingIt()
    {
        var result = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            birthDate: default,
            seasonsOfService: 4);

        Assert.True(result.IsFailure);
        Assert.Equal("player.missing_birth_date", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void CompleteSeasonOfServiceIncrementsByOne()
    {
        var player = CreatePlayer();

        var first = player.CompleteSeasonOfService();
        var second = player.CompleteSeasonOfService();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(6, player.SeasonsOfService);
    }

    private static Player CreatePlayer()
    {
        return Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
            new DateOnly(2000, 6, 15),
            seasonsOfService: 4).Value;
    }
}
