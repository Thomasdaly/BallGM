using BallGM.Application.Seasons;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.Tests;

public sealed class GameSheetTests
{
    private const string Away = "team-away";
    private const string Home = "team-home";
    private const string Other = "team-other";

    [Fact]
    public void Preview_ProjectsEachRotationFromTodaysDepthChart()
    {
        var game = Fixture("g-next", day: 5, away: Away, home: Home);

        var sheet = GameSheet.Build(Input(game, currentDay: 4, fixtures: [game]));

        Assert.False(sheet.IsFinal);
        Assert.Equal("Tomorrow", sheet.Status);
        Assert.Equal("Projected rotations", sheet.LineupHeading);
        Assert.Equal(["a-pg", "a-c"], sheet.Away.Starters.Select(row => row.PlayerId));
        Assert.Equal(["PG", "C"], sheet.Away.Starters.Select(row => row.Position));
        Assert.Equal([84, 70], sheet.Away.Starters.Select(row => row.Overall));

        // The bench runs by minutes, and a player the rotation gives no minutes is left out.
        Assert.Equal(["a-6th", "a-7th"], sheet.Away.Bench.Select(row => row.PlayerId));
        Assert.Equal("30", Cell(sheet.Away, sheet.Away.Starters[0], "MIN"));
    }

    [Fact]
    public void Preview_ShowsSeasonAveragesAndRecentScoring()
    {
        var earlier = Played("g-1", day: 1, away: Away, home: Other, awayPoints: 101, homePoints: 99);
        var game = Fixture("g-next", day: 5, away: Away, home: Home);
        var recent = new BoxScoreSummary(
            "g-1", 1, "2031-07-08", Other, "Other", 99, Away, "Away Side", 101, true,
            [],
            [Line("a-pg", "Point Guard", true, minutes: 34, points: 31, rebounds: 4, assists: 9)]);

        var sheet = GameSheet.Build(Input(
            game,
            currentDay: 4,
            fixtures: [earlier, game],
            totals: new Dictionary<string, PlayerSeasonTotals>
            {
                ["a-pg"] = new("a-pg", CountingStats.None with { Games = 4, Minutes = 130, Points = 90, DefensiveRebounds = 18, Assists = 30 }),
            },
            recent: new Dictionary<string, IReadOnlyList<BoxScoreSummary>> { [Away] = [recent] }));

        var pointGuard = sheet.Away.Starters[0];
        Assert.Equal("22.5", Cell(sheet.Away, pointGuard, "PTS"));
        Assert.Equal("4.5", Cell(sheet.Away, pointGuard, "REB"));
        Assert.Equal("7.5", Cell(sheet.Away, pointGuard, "AST"));
        Assert.Equal("31.0", Cell(sheet.Away, pointGuard, "L5 PTS"));
        Assert.Equal("—", Cell(sheet.Away, sheet.Away.Starters[1], "L5 PTS"));
        Assert.Equal(["W"], sheet.Away.Form.Select(chip => chip.Letter));
        Assert.Empty(sheet.Home.Form);
    }

    [Fact]
    public void Final_ShowsTheGamesOwnLinesWithRatingsFromTheDepthCharts()
    {
        var game = Played("g-final", day: 3, away: Away, home: Home, awayPoints: 96, homePoints: 104);
        var box = new BoxScoreSummary(
            "g-final", 3, "2031-07-10", Home, "Home Side", 104, Away, "Away Side", 96, true,
            [Line("h-1", "Home Starter", true, minutes: 36, points: 28, rebounds: 7, assists: 3)],
            [
                Line("a-pg", "Point Guard", true, minutes: 35, points: 19, rebounds: 3, assists: 11),
                Line("a-gone", "Traded Away", false, minutes: 12, points: 4, rebounds: 2, assists: 0),
            ],
            new BoxScoreTeamLine(CountingStats.None with { Games = 1, Points = 104, PlusMinus = 8 }, 1, 2),
            new BoxScoreTeamLine(CountingStats.None with { Games = 1, Points = 96, PlusMinus = -8 }, 0, 3));

        var sheet = GameSheet.Build(Input(game, currentDay: 6, fixtures: [game], played: box));

        Assert.True(sheet.IsFinal);
        Assert.Equal("Final", sheet.Status);
        Assert.Equal("96", sheet.Away.Score);
        Assert.Equal("104", sheet.Home.Score);
        Assert.True(sheet.Home.IsWinner);
        Assert.False(sheet.Away.IsWinner);

        var pointGuard = Assert.Single(sheet.Away.Starters);
        Assert.Equal("19", Cell(sheet.Away, pointGuard, "PTS"));
        Assert.Equal("4-9", Cell(sheet.Away, pointGuard, "FG"));
        Assert.Equal("3", Cell(sheet.Away, pointGuard, "DREB"));

        // The team row is the team's own totals, and the team rebounds no player secured are stated.
        Assert.Equal("96", sheet.Away.Totals[sheet.Away.Headers.ToList().FindIndex(header => header.Header == "PTS")].Value);
        Assert.Contains("Team rebounds: 3", sheet.Home.TeamRebounds, StringComparison.Ordinal);

        // The team's plus-minus row is its margin.
        Assert.Equal("-8", sheet.Away.Totals[sheet.Away.Headers.ToList().FindIndex(header => header.Header == "+/-")].Value);
        Assert.Equal(84, pointGuard.Overall);
        Assert.True(pointGuard.HasOverall);

        var gone = Assert.Single(sheet.Away.Bench);
        Assert.False(gone.HasOverall);
        Assert.Equal("—", gone.Position);
    }

    [Fact]
    public void Final_ShowsTheFourFactorsWithTheLosingSideOfEachDimmed()
    {
        var game = Played("g-final", day: 3, away: Away, home: Home, awayPoints: 96, homePoints: 104);
        var shooting = CountingStats.None with { Games = 1, FieldGoalsMade = 40, FieldGoalsAttempted = 80, FreeThrowsMade = 16, OffensiveRebounds = 10, DefensiveRebounds = 30 };
        var box = new BoxScoreSummary(
            "g-final", 3, "2031-07-10", Home, "Home Side", 104, Away, "Away Side", 96, true, [], [],

            // Home: 50.0 eFG%, 10 turnovers. Away: 45.0 eFG%, 14 turnovers; level at the line.
            new BoxScoreTeamLine(shooting with { Points = 104, Turnovers = 10 }, 0, 0),
            new BoxScoreTeamLine(shooting with { Points = 96, FieldGoalsMade = 36, Turnovers = 14 }, 0, 0));

        var factors = GameSheet.Build(Input(game, currentDay: 6, fixtures: [game], played: box)).FourFactors;

        var effective = factors.Single(row => row.Label == "Effective FG%");
        Assert.Equal(("45.0", "50.0"), (effective.Away, effective.Home));
        Assert.True(effective.AwayTrails);
        Assert.False(effective.HomeTrails);

        // Fewer turnovers is the better side.
        var turnovers = factors.Single(row => row.Label == "Turnover %");
        Assert.True(turnovers.AwayTrails);
        Assert.EndsWith("home better", turnovers.AccessibleName, StringComparison.Ordinal);

        var line = factors.Single(row => row.Label == "Free throws per FGA");
        Assert.Equal(("20.0", "20.0"), (line.Away, line.Home));
        Assert.False(line.AwayTrails || line.HomeTrails);
    }

    [Fact]
    public void Meetings_ListTheOtherGamesBetweenTheSameTwoTeamsOnly()
    {
        var first = Played("g-1", day: 1, away: Home, home: Away, awayPoints: 90, homePoints: 100);
        var unrelated = Played("g-2", day: 2, away: Away, home: Other, awayPoints: 90, homePoints: 100);
        var game = Fixture("g-3", day: 5, away: Away, home: Home);
        var later = Fixture("g-4", day: 9, away: Home, home: Away);

        var sheet = GameSheet.Build(Input(game, currentDay: 4, fixtures: [first, unrelated, game, later]));

        Assert.Equal(["g-1", "g-4"], sheet.Meetings.Select(meeting => meeting.GameId));
        Assert.Equal("90–100", sheet.Meetings[0].Score);
        Assert.Equal("AWA won", sheet.Meetings[0].Winner);
        Assert.Equal("To play", sheet.Meetings[1].Score);
    }

    [Fact]
    public void PlayedWithoutPlayerLines_SaysSoRatherThanShowingAnEmptyTable()
    {
        var game = Played("g-final", day: 3, away: Away, home: Home, awayPoints: 96, homePoints: 104);

        var sheet = GameSheet.Build(Input(game, currentDay: 6, fixtures: [game]));

        Assert.False(sheet.Away.HasLineup);
        Assert.Equal("This result was recorded without player lines.", sheet.Away.LineupNote);
    }

    private static GameSheetInput Input(
        FixtureLine game,
        int currentDay,
        IReadOnlyList<FixtureLine> fixtures,
        IReadOnlyDictionary<string, PlayerSeasonTotals>? totals = null,
        IReadOnlyDictionary<string, IReadOnlyList<BoxScoreSummary>>? recent = null,
        BoxScoreSummary? played = null) =>
        new(
            game,
            currentDay,
            fixtures,
            new Dictionary<string, StandingsLine>
            {
                [Away] = new(1, Away, "Away Side", null, null, 1, 0, 1, null, null, null, null, 101, 99, 2),
                [Home] = new(2, Home, "Home Side", null, null, 0, 0, 0, null, null, null, null, 0, 0, 0),
            },
            new Dictionary<string, TeamArt>(),
            totals ?? new Dictionary<string, PlayerSeasonTotals>(),
            recent ?? new Dictionary<string, IReadOnlyList<BoxScoreSummary>>(),
            Chart(Away, "Away Side", ("PG", "a-pg", 84, 30, true), ("C", "a-c", 70, 28, true), ("PG", "a-6th", 66, 22, false), ("C", "a-7th", 61, 14, false), ("C", "a-end", 50, 0, false)),
            Chart(Home, "Home Side", ("SF", "h-1", 77, 36, true)),
            played,
            day => $"date {day}");

    private static DepthChartSummary Chart(string teamId, string name, params (string Position, string Id, int Overall, int Minutes, bool Starter)[] players) =>
        new(
            teamId,
            name,
            0,
            "2031-07-07",
            240,
            players
                .GroupBy(player => player.Position)
                .Select(group => new DepthChartPositionColumn(
                    group.Key,
                    group.Count(),
                    group.Sum(player => player.Minutes),
                    group.Select((player, rank) => new DepthChartLine(player.Id, player.Id, player.Overall, rank + 1, player.Minutes, player.Starter)).ToList()))
                .ToList(),
            [],
            []);

    private static FixtureLine Fixture(string id, int day, string away, string home) =>
        new(id, day, "2031-07-07", "RegularSeason", home, Name(home), away, Name(away), false, null, null);

    private static FixtureLine Played(string id, int day, string away, string home, int awayPoints, int homePoints) =>
        new(id, day, "2031-07-07", "RegularSeason", home, Name(home), away, Name(away), true, homePoints, awayPoints);

    private static string Name(string team) => team switch
    {
        Away => "Away Side",
        Home => "Home Side",
        _ => "Other",
    };

    /// <summary>A box-score line whose points come from two-point makes and free throws, all rebounds defensive.</summary>
    private static BoxScoreLine Line(string playerId, string name, bool started, int minutes, int points, int rebounds, int assists) =>
        new(playerId, name, started, CountingStats.None with
        {
            Games = 1,
            Starts = started ? 1 : 0,
            Minutes = minutes,
            Points = points,
            FieldGoalsMade = points / 2 / 2,
            FieldGoalsAttempted = points / 2,
            FreeThrowsMade = points - (2 * (points / 2 / 2)),
            FreeThrowsAttempted = points - (2 * (points / 2 / 2)),
            DefensiveRebounds = rebounds,
            Assists = assists,
        });

    private static string Cell(GameSheetSide side, LineupRow row, string header) =>
        row.Stats[side.Headers.ToList().FindIndex(column => column.Header == header)].Value;
}
