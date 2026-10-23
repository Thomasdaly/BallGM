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
        new AttributeSheet(
            [
                new AttributeGroupLine("Scoring", [new AttributeLine("ThreePoint", 70), new AttributeLine("FreeThrow", 88)]),
                new AttributeGroupLine("Mental", [new AttributeLine("BasketballIq", 75)]),
            ],
            88,
            new BodyLine(193, 201, 90),
            [new PositionFamiliarityLine("PG", "Natural", 4), new PositionFamiliarityLine("SG", "Accomplished", 3), new PositionFamiliarityLine("SF", "Competent", 2), new PositionFamiliarityLine("PF", "Awkward", 0), new PositionFamiliarityLine("C", "Not assessed", PositionFamiliarityLine.NotAssessed)],
            ["StretchesTheFloor"],
            "Professional"),
        "Test Team",
        null,
        false,
        null,
        [new ContractSeasonLine(2026, 20_000_000, false), new ContractSeasonLine(2027, 21_000_000, true)],
        current,
        recent ?? [],
        [new CareerSeasonLine("2024-25", "Old Team", 70, 2100, 1400, 350, 420), .. current is null ? Array.Empty<CareerSeasonLine>() : [current]]);

    [Fact]
    public void Profile_ShowsEveryAttributeInSheetOrderAndTheContract()
    {
        var profile = new PlayerProfileViewModel(Sample(), () => { });

        var sheet = profile.Sheet;
        Assert.Equal(["Scoring", "Mental"], sheet.Columns[0].Select(group => group.Title));
        // Two groups to a column, in the order the read model sends them.
        Assert.Single(sheet.Columns);
        Assert.Equal(["Three point", "Free throw"], sheet.Columns[0][0].Attributes.Select(cell => cell.Name));
        Assert.Equal([70, 88], sheet.Columns[0][0].Attributes.Select(cell => cell.Value));
        Assert.Equal("Basketball IQ", sheet.Columns[0][1].Attributes[0].Name);
        Assert.Contains("threes", sheet.Columns[0][0].Attributes[0].Description, StringComparison.Ordinal);

        // Ability as a coach's stars (Overall 80 is four stars, potential 88 four and a half), measurements both ways, positions in order.
        Assert.Equal("★★★★☆", sheet.CurrentStars);
        Assert.Equal("★★★★½", sheet.PotentialStars);
        Assert.Equal("Current ability: 4 of 5 stars", sheet.CurrentStarsName);
        Assert.Equal("193 cm (6 ft 4 in) · wingspan 201 cm · 90 kg (198 lb)", sheet.Body);
        Assert.Equal(["PG", "SG", "SF", "PF", "C"], sheet.Positions.Select(chip => chip.Position));
        Assert.True(sheet.Positions[0].IsNatural);
        Assert.Equal("Not assessed", sheet.Positions[4].Familiarity);
        Assert.True(sheet.Positions[1].IsAccomplished);
        Assert.False(sheet.Positions[3].IsCompetent);
        Assert.Equal(["Stretches the floor"], sheet.Traits);
        Assert.Equal("Professional", sheet.Personality);
        Assert.Equal(["2026-27", "2027-28"], profile.Contract.Select(row => row.Season));
        Assert.Equal("Option", profile.Contract[1].Note);
        Assert.Equal("2 seasons remaining · $41.0M total", profile.ContractSummary);
    }

    [Fact]
    public void Ratings_ShowTheTendenciesAsTheirOwnGroupAfterTheAttributes()
    {
        var sheet = Sample().Attributes with { Tendencies = [new AttributeLine("ShotThree", 82), new AttributeLine("Drive", 35)] };
        var profile = new PlayerProfileViewModel(Sample() with { Attributes = sheet }, () => { });

        var tendencies = profile.Groups[^1];
        Assert.Equal("Tendencies", tendencies.Title);
        Assert.Equal(["Shot three", "Drive"], tendencies.Attributes.Select(cell => cell.Name));
        Assert.StartsWith("Tendency:", tendencies.Attributes[0].Description, StringComparison.Ordinal);
        Assert.DoesNotContain(profile.OverviewGroups, group => group.Title == "Tendencies");

        Assert.DoesNotContain(new PlayerProfileViewModel(Sample(), () => { }).Groups, group => group.Title == "Tendencies");
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
    public void Career_ShowsStealsBlocksTurnoversAndPlusMinusPerGameFromTheSeasonsMetrics()
    {
        var metrics = new Dictionary<string, double> { ["STL"] = 15, ["BLK"] = 5, ["TOV"] = 25, ["PLUS_MINUS"] = -12 };
        var current = new CareerSeasonLine("2026-27", "Test Team", 10, 300, 250, 40, 60, IsCurrent: true, Metrics: metrics);

        var row = new PlayerProfileViewModel(Sample(current), () => { }).Career[^1];

        Assert.Equal(("1.5", "0.5", "2.5", "-1.2"), (Cell(row, "STL"), Cell(row, "BLK"), Cell(row, "TOV"), Cell(row, "+/-")));

        // A season that states none of them shows a dash, not a zero.
        Assert.Equal("—", Cell(new PlayerProfileViewModel(Sample(), () => { }).Career[0], "BLK"));
    }

    [Fact]
    public void Career_ShowsPerGameAveragesAndMarksTheCurrentSeason()
    {
        var current = new CareerSeasonLine("2026-27", "Test Team", 10, 300, 250, 40, 60, IsCurrent: true);
        var profile = new PlayerProfileViewModel(Sample(current), () => { });

        Assert.Equal("20.0", Cell(profile.Career[0], "PTS"));
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
            Steals = 2,
            Turnovers = 4,
            Blocks = 1,
            PersonalFouls = 5,
        });
        var profile = new PlayerProfileViewModel(Sample(recent: [game]), () => { });

        var row = Assert.Single(profile.RecentGames);
        Assert.Equal("@ Rivals", row.Opponent);
        Assert.Equal("W 110–104", row.Result);
        // The full box-score line, in the same columns a game sheet shows.
        Assert.Equal(["MIN", "PTS", "FG", "3P", "FT", "OREB", "DREB", "REB", "AST", "STL", "BLK", "TOV", "PF", "+/-"], profile.GameLogHeaders.Select(header => header.Header));
        Assert.Equal(["34", "28", "11-20", "2-5", "4-5", "2", "4", "6", "9", "2", "1", "4", "5", "-3"], row.Stats.Select(cell => cell.Value));
    }

    [Fact]
    public void Back_ReturnsToTheSquad()
    {
        var backed = false;
        var profile = new PlayerProfileViewModel(Sample(), () => backed = true);

        profile.BackCommand.Execute(null);

        Assert.True(backed);
    }

    /// <summary>A career row's figure under one header, read the way the table draws it.</summary>
    private static string Cell(CareerRow row, string header) =>
        row.Cells[CareerRow.Headers.ToList().FindIndex(column => column.Header == header)].Value;
}
