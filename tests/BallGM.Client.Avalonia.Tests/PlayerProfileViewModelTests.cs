using BallGM.Application.Players;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Tests;

public sealed class PlayerProfileViewModelTests
{
    private static PlayerProfileSummary Sample(CareerSeasonLine? current = null, IReadOnlyList<RecentGameLine>? recent = null) => new(
        "P1",
        "Test Player",
        "PG",
        27,
        new DateOnly(1999, 3, 4),
        6,
        80,
        new PlayerAttributes(70, 88, 75, 90, 77),
        "Test Team",
        null,
        false,
        null,
        [new ContractSeasonLine(2026, 20_000_000, false), new ContractSeasonLine(2027, 21_000_000, true)],
        current,
        recent ?? [],
        [new CareerSeasonLine("2024-25", "Old Team", 70, 2100, 1400, 350, 420), .. current is null ? Array.Empty<CareerSeasonLine>() : [current]]);

    [Fact]
    public void Profile_ShowsAllFiveAttributesAndTheContract()
    {
        var profile = new PlayerProfileViewModel(Sample(), () => { });

        Assert.Equal(["Height", "Speed", "Strength", "Passing", "Lateral quickness"], profile.Attributes.Select(row => row.Name));
        Assert.Equal([70, 88, 75, 90, 77], profile.Attributes.Select(row => row.Value));
        Assert.Equal(["2026-27", "2027-28"], profile.Contract.Select(row => row.Season));
        Assert.Equal("Option", profile.Contract[1].Note);
        Assert.Equal("2 seasons remaining · $41.0M total", profile.ContractSummary);
    }

    [Fact]
    public void Career_ShowsPerGameAveragesAndMarksTheCurrentSeason()
    {
        var current = new CareerSeasonLine("2026-27", "Test Team", 10, 300, 250, 40, 60, IsCurrent: true);
        var profile = new PlayerProfileViewModel(Sample(current), () => { });

        Assert.Equal("20.0", profile.Career[0].Points);
        Assert.True(profile.Career[^1].IsCurrent);
        Assert.Equal("25.0", profile.SeasonTiles.Single(tile => tile.Label == "PPG").Value);
        Assert.StartsWith("Career: 2 seasons · 80 games", profile.CareerTotalsLine);
    }

    [Fact]
    public void RecentForm_ReadsLikeAGameLog()
    {
        var game = new RecentGameLine(20, "Jul 21", "Rivals", IsHome: false, Won: true, 110, 104, 34, 28, 6, 9, 11, 20);
        var profile = new PlayerProfileViewModel(Sample(recent: [game]), () => { });

        var row = Assert.Single(profile.RecentGames);
        Assert.Equal("@ Rivals", row.Opponent);
        Assert.Equal("W 110–104", row.Result);
        Assert.Equal("11-20", row.Shooting);
    }

    [Fact]
    public void Back_ReturnsToTheSquad()
    {
        var backed = false;
        var profile = new PlayerProfileViewModel(Sample(), () => backed = true);

        profile.BackCommand.Execute(null);

        Assert.True(backed);
    }
}
