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
            new PlayerRating(overall: 75));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsInjured);
        Assert.Null(result.Value.CurrentInjury);
    }

    [Fact]
    public void CreateAcceptsAnInitialInjury()
    {
        var result = Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75),
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

    private static Player CreatePlayer()
    {
        return Player.Create(
            new PlayerId("player-001"),
            "Fictional Forward",
            Position.SmallForward,
            new PlayerRating(overall: 75)).Value;
    }
}
