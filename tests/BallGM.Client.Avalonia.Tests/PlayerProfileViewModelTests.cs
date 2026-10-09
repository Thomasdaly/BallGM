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
    public void Career_ListsAPlayoffRowSeparatelyAndLeavesItOutOfTheCareerTotals()
    {
        var playoffs = new CareerSeasonLine("2024-25", "Old Team", 8, 280, 200, 40, 30, IsPostseason: true);
        var summary = Sample() with { Career = [.. Sample().Career, playoffs] };

        var profile = new PlayerProfileViewModel(summary, () => { });

        Assert.Equal("2024-25 playoffs", profile.Career[^1].Season);
        Assert.StartsWith("Career: 1 season · 70 games", profile.CareerTotalsLine);
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
        var game = new RecentGameLine(20, "Jul 21", "Rivals", IsHome: false, Won: true, 110, 104, BallGM.Domain.Seasons.CountingStats.None with
        {
            Games = 1,
            Minutes = 34,
            Points = 28,
            FieldGoalsMade = 11,
            FieldGoalsAttempted = 20,
            ThreePointsMade = 2,
            ThreePointsAttempted = 5,
            FreeThrowsMade = 4,
            FreeThrowsAttempted = 5,
            OffensiveRebounds = 2,
            DefensiveRebounds = 4,
            Assists = 9,
            PlusMinus = -3,
        });
        var profile = new PlayerProfileViewModel(Sample(recent: [game]), () => { });

        var row = Assert.Single(profile.RecentGames);
        Assert.Equal("@ Rivals", row.Opponent);
        Assert.Equal("W 110–104", row.Result);
        // The full box-score line, in the same columns a game sheet shows.
        Assert.Equal(["MIN", "PTS", "FG", "3P", "FT", "OREB", "DREB", "REB", "AST", "+/-"], profile.GameLogHeaders.Select(header => header.Header));
        Assert.Equal(["34", "28", "11-20", "2-5", "4-5", "2", "4", "6", "9", "-3"], row.Stats.Select(cell => cell.Value));
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
