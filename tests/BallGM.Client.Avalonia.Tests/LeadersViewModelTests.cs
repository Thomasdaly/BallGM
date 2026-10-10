using BallGM.Application.Leagues;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.Tests;

public sealed class LeadersViewModelTests
{
    private static LeaguePlayerLine Line(string name, int games, int points, int threesMade = 0, int minutesPerGame = 30) => new(
        name,
        name,
        "Team",
        "SG",
        CountingStats.None with
        {
            Games = games,
            Minutes = games * minutesPerGame,
            Points = points,
            FieldGoalsMade = points / 2,
            FieldGoalsAttempted = points,
            ThreePointsMade = threesMade,
            ThreePointsAttempted = threesMade * 2,
        },
        new RatingAverage(400 + (points * 10), games));

    private static LeagueStatsSummary League(params LeaguePlayerLine[] lines) => new(lines, LeaderQualifiersConfiguration.Default, 10);

    [Fact]
    public void PerGameLeadersNeedTheGamesShareButTotalsCountEveryone()
    {
        // The cameo averages 40 in two games: he leads the totals board only if he has the most points.
        var league = League(Line("Regular", 10, 200), Line("Cameo", 2, 80), Line("Starter", 8, 120));
        var screen = new LeadersViewModel(() => league);

        var points = screen.Boards.Single(board => board.Header == "PTS");
        Assert.Equal(["Regular", "Starter"], points.Leaders.Select(row => row.Name));
        Assert.Equal([1, 2], points.Leaders.Select(row => row.Rank));
        Assert.Equal("20.0", points.Leaders[0].Value);

        screen.Basis.Basis = StatBasis.Totals;
        Assert.Equal(["Regular", "Starter", "Cameo"], screen.Boards.Single(board => board.Header == "PTS").Leaders.Select(row => row.Name));
    }

    [Fact]
    public void Per36LeadersAlsoNeedTheMinutes()
    {
        // Two points in three minutes, every game, is 24 per 36: more than the starter, but not enough minutes.
        var league = League(Line("Specialist", 10, 20, minutesPerGame: 3), Line("Starter", 10, 200, minutesPerGame: 34));
        var screen = new LeadersViewModel(() => league);
        screen.Basis.Basis = StatBasis.Per36;

        Assert.Equal("Starter", Assert.Single(screen.Boards.Single(board => board.Header == "PTS").Leaders).Name);
    }

    [Fact]
    public void RatingLeadersRankOnTheUnroundedAverage()
    {
        // 7.04 and 6.96 both print 7.0; the higher one still ranks first, whatever the names.
        var league = League(
            Line("Adams", 10, 100) with { Rating = new RatingAverage(696, 10) },
            Line("Zane", 10, 100) with { Rating = new RatingAverage(704, 10) });
        var screen = new LeadersViewModel(() => league);

        Assert.Equal(["Zane", "Adams"], screen.Boards.Single(board => board.Header == "RTG").Leaders.Select(row => row.Name));
        screen.QualifiedOnly = false;
        screen.SortByRatingCommand.Execute(null);
        Assert.Equal(["Zane", "Adams"], screen.Rows.Select(row => row.Name));
    }

    [Fact]
    public void ShootingLeadersAlsoNeedTheMakes()
    {
        // 1.00 three made per team game over 10 games is 10.
        var league = League(Line("Volume", 10, 100, threesMade: 12), Line("Sniper", 10, 100, threesMade: 6));
        var threes = new LeadersViewModel(() => league).Boards.Single(board => board.Header == "3P%");

        Assert.Equal("Volume", Assert.Single(threes.Leaders).Name);
    }

    [Fact]
    public void TheTableSortsPagesAndFiltersToTheQualified()
    {
        var lines = Enumerable.Range(0, LeadersViewModel.PageSize + 5).Select(index => Line($"P{index:D3}", 10, index)).Append(Line("Cameo", 1, 30)).ToArray();
        var screen = new LeadersViewModel(() => League(lines));

        Assert.Equal(LeadersViewModel.PageSize, screen.Rows.Count);
        Assert.True(screen.CanShowMore);
        Assert.Equal($"P{LeadersViewModel.PageSize + 4:D3}", screen.Rows[0].Name);
        Assert.DoesNotContain(screen.Rows, row => row.Name == "Cameo");

        screen.ShowMoreCommand.Execute(null);
        Assert.False(screen.CanShowMore);
        Assert.Equal(lines.Length - 1, screen.Rows.Count);

        screen.QualifiedOnly = false;
        screen.SortByRatingCommand.Execute(null);
        Assert.Equal("RTG ▾", screen.RatingHeader);
        Assert.Equal($"P{LeadersViewModel.PageSize + 4:D3}", screen.Rows[0].Name);
        screen.SortByRatingCommand.Execute(null);
        Assert.Equal("P000", screen.Rows[0].Name);
    }

    [Fact]
    public void BeforeAnyGamesTheScreenSaysSoInsteadOfShowingEmptyBoards()
    {
        var screen = new LeadersViewModel(() => League());

        Assert.False(screen.HasStats);
        Assert.Empty(screen.Boards);
        Assert.Contains("70% of the season's games", screen.QualifierNote, StringComparison.Ordinal);
        Assert.Contains("15 minutes a game", screen.QualifierNote, StringComparison.Ordinal);
        Assert.Equal("66.67", DisplayText.Share(6_667));
        Assert.Contains("3.66 made field goals, 1 made three or 1.52 made free throws", screen.QualifierNote, StringComparison.Ordinal);
    }

    [Fact]
    public void PercentileRowsKeepTheApplicationsOrderAndPrintTheirFigure()
    {
        var rows = PercentileRow.From([new("PTS", 91, false), new("AST", 13, false), new("TOV", 12, true), new("3P%", 2, false)]);

        Assert.Equal(["PTS", "AST", "TOV", "3P%"], rows.Select(row => row.Stat));
        Assert.Equal(["91st", "13th", "12th", "2nd"], rows.Select(row => row.Text));
        Assert.True(rows.Single(row => row.Stat == "TOV").LowerIsBetter);
        Assert.Contains("flipped", rows.Single(row => row.Stat == "TOV").Tooltip, StringComparison.Ordinal);
        Assert.Empty(PercentileRow.From(null));
    }

    [Fact]
    public void ProductionLineReadsPerGameFromTheSharedColumns()
    {
        var season = new PlayerSeasonTotals("P", CountingStats.None with { Games = 4, Points = 80, OffensiveRebounds = 4, DefensiveRebounds = 20, Assists = 10, Steals = 2, Blocks = 1 }, new RatingAverage(712, 4));

        Assert.Equal("20.0 PTS · 6.0 REB · 2.5 AST · 0.5 STL · 0.3 BLK · 7.1 RTG in 4 games", ProductionLine.For(season));
        Assert.Equal("no games this regular season", ProductionLine.For(null));
    }
}
