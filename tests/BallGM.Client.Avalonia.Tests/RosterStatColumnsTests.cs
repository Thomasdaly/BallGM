using BallGM.Application.Leagues;
using BallGM.Application.Players;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.Tests;

public sealed class RosterStatColumnsTests
{
    private static readonly RosterSpot Spot = new("P1", "Test Player", "PG", 80, false, null, 10_000_000, 2);

    private static readonly CountingStats Season = CountingStats.None with
    {
        Games = 4,
        Minutes = 120,
        Points = 80,
        FieldGoalsMade = 30,
        FieldGoalsAttempted = 60,
        ThreePointsMade = 8,
        ThreePointsAttempted = 20,
        FreeThrowsMade = 12,
        FreeThrowsAttempted = 16,
        OffensiveRebounds = 4,
        DefensiveRebounds = 16,
        Assists = 24,
        PlusMinus = 18,
        Steals = 6,
        Turnovers = 10,
    };

    [Fact]
    public void RosterRow_ShowsTheSharedSeasonColumnsOnTheChosenBasis()
    {
        var perGame = RosterRow.From(Spot, Season, StatBasis.PerGame);
        var per36 = RosterRow.From(Spot, Season, StatBasis.Per36);
        var totals = RosterRow.From(Spot, Season, StatBasis.Totals);

        Assert.Equal(["GP", "MIN", "PTS", "REB", "AST", "STL", "TOV", "+/-", "FG%", "3P%", "FT%", "TS%"], StatColumns.Season.Select(column => column.Header));
        Assert.Equal(["4", "30.0", "20.0", "5.0", "6.0", "1.5", "2.5", "+4.5", "50.0", "40.0", "75.0", "59.7"], perGame.Stats.Select(cell => cell.Value));
        Assert.Equal("24.0", per36.Stats[2].Value);
        Assert.Equal("30.0", per36.Stats[1].Value); // minutes stay per game on a per-36 view
        Assert.Equal("80", totals.Stats[2].Value);
    }

    [Fact]
    public void RosterRow_WithNoGamesShowsDashesAndSortsBelowEveryone()
    {
        var row = RosterRow.From(Spot);

        Assert.All(row.Stats, cell => Assert.Equal("—", cell.Value));
        Assert.All(row.StatSortKeys, key => Assert.Null(key));
    }

    [Fact]
    public void SortingByAStat_PutsPlayersWithNoFigureLastInBothDirections()
    {
        RosterRow[] rows =
        [
            RosterRow.From(Spot with { PlayerId = "none" }),
            RosterRow.From(Spot with { PlayerId = "high" }, Season with { Points = 120 }),
            RosterRow.From(Spot with { PlayerId = "low" }, Season with { Points = 40 }),
        ];
        var points = StatColumns.Season.ToList().IndexOf(StatColumns.Points);

        Assert.Equal(["high", "low", "none"], RosterRow.OrderByStat(rows, points, descending: true).Select(row => row.PlayerId));
        Assert.Equal(["low", "high", "none"], RosterRow.OrderByStat(rows, points, descending: false).Select(row => row.PlayerId));
    }
}
