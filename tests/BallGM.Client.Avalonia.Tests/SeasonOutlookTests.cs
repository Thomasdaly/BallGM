using Avalonia.Headless.XUnit;
using BallGM.Application.Seasons;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Tests;

public sealed class SeasonOutlookTests
{
    private const string Us = "team-a";
    private const string Bravo = "team-b";
    private const string Charlie = "team-c";

    // 2031-07-07 is a Monday, so day 0 sits in the grid's first column.
    private const string SeasonStart = "2031-07-07";

    [Fact]
    public void RoadAhead_FlagsABackToBackAndCountsTheRestBeforeEachGame()
    {
        var outlook = Outlook(
            currentDay: 3,
            Played(day: 1, home: Us, away: Bravo, homePoints: 100, awayPoints: 90),
            Upcoming(day: 3, home: Charlie, away: Us),
            Upcoming(day: 4, home: Us, away: Bravo),
            Upcoming(day: 8, home: Bravo, away: Us));

        var cards = outlook.RoadAhead();

        Assert.Equal(["1 day rest", "Back-to-back", "3 days rest"], cards.Select(card => card.RestLine));
        Assert.Equal([false, true, false], cards.Select(card => card.IsBackToBack));
        Assert.Equal(["@", "vs", "@"], cards.Select(card => card.Venue));
        Assert.Equal(["Today", "Tomorrow", "In 5 days"], cards.Select(card => card.When));
    }

    [Fact]
    public void RoadAhead_CallsTheTeamsFirstGameItsSeasonOpener()
    {
        var outlook = Outlook(currentDay: 0, Upcoming(day: 2, home: Us, away: Bravo));

        Assert.Equal("Season opener", Assert.Single(outlook.RoadAhead()).RestLine);
    }

    [Fact]
    public void RoadAhead_ShowsNoOpponentStrengthBeforeTheOpponentHasPlayed()
    {
        // Charlie has not played, so its place in the table is an identifier ordering, not a reading of the team.
        var outlook = Outlook(
            currentDay: 2,
            Played(day: 1, home: Us, away: Bravo, homePoints: 100, awayPoints: 90),
            Upcoming(day: 3, home: Us, away: Charlie),
            Upcoming(day: 5, home: Us, away: Bravo));

        var cards = outlook.RoadAhead();

        Assert.False(cards[0].HasOpponentForm);
        Assert.Equal("No games played yet", cards[0].OpponentDetail);
        Assert.True(cards[1].HasOpponentForm);
        Assert.Equal(0, cards[1].OpponentWinPercent);
        Assert.Equal("0-1", cards[1].OpponentRecord);
    }

    [Fact]
    public void RoadAhead_ReportsTheSeasonSeriesFromPlayedMeetingsOnly()
    {
        var outlook = Outlook(
            currentDay: 6,
            Played(day: 1, home: Us, away: Bravo, homePoints: 100, awayPoints: 90),
            Played(day: 3, home: Bravo, away: Us, homePoints: 110, awayPoints: 95),
            Played(day: 5, home: Us, away: Bravo, homePoints: 101, awayPoints: 99),
            Upcoming(day: 7, home: Bravo, away: Us),
            Upcoming(day: 9, home: Us, away: Charlie));

        var cards = outlook.RoadAhead();

        Assert.Equal("Season series 2-1", cards[0].SeriesLine);
        Assert.Equal("First meeting", cards[1].SeriesLine);
    }

    [Fact]
    public void Streak_CountsTheCurrentRunAndFormShowsTheLastFiveOldestFirst()
    {
        var outlook = Outlook(
            currentDay: 10,
            Played(day: 1, home: Us, away: Bravo, homePoints: 80, awayPoints: 90),
            Played(day: 2, home: Us, away: Bravo, homePoints: 100, awayPoints: 90),
            Played(day: 3, home: Us, away: Bravo, homePoints: 80, awayPoints: 90),
            Played(day: 4, home: Us, away: Bravo, homePoints: 80, awayPoints: 90),
            Played(day: 5, home: Bravo, away: Us, homePoints: 70, awayPoints: 90),
            Played(day: 6, home: Bravo, away: Us, homePoints: 70, awayPoints: 90),
            Played(day: 7, home: Us, away: Charlie, homePoints: 99, awayPoints: 98));

        Assert.Equal("W3", outlook.Streak);
        Assert.Equal(["L", "L", "W", "W", "W"], outlook.Form().Select(chip => chip.Letter));
    }

    [Fact]
    public void Ribbon_DrawsWinsAboveTheLineAndLossesBelowItScaledByMargin()
    {
        var outlook = Outlook(
            currentDay: 4,
            Played(day: 1, home: Us, away: Bravo, homePoints: 101, awayPoints: 100),
            Played(day: 2, home: Bravo, away: Us, homePoints: 140, awayPoints: 90),
            Upcoming(day: 5, home: Us, away: Charlie));

        var days = outlook.RibbonDays();

        Assert.Equal(20, days.Count);
        Assert.True(days[1].WinHeight > 0);
        Assert.Equal(0, days[1].LossHeight);
        Assert.Equal(SeasonOutlook.RibbonHalfHeight, days[2].LossHeight);
        Assert.True(days[1].WinHeight < days[2].LossHeight, "A one-point win should draw shorter than a fifty-point loss.");
        Assert.True(days[5].IsUpcomingGame);
        Assert.True(days[4].IsToday);
        Assert.Equal(0, days[3].WinHeight + days[3].LossHeight);
    }

    [Fact]
    public void Ribbon_SizesEachPhaseByItsDays()
    {
        var outlook = Outlook(currentDay: 0);

        var phases = outlook.RibbonPhases();

        Assert.Equal([5, 15], phases.Select(phase => phase.Span));
        Assert.True(phases[0].IsCurrent);
    }

    [Fact]
    public void Month_StartsOnMondayAndPadsToWholeWeeks()
    {
        var outlook = Outlook(
            currentDay: 1,
            Played(day: 0, home: Bravo, away: Us, homePoints: 90, awayPoints: 100),
            Upcoming(day: 2, home: Us, away: Charlie));

        var cells = outlook.Month(new DateOnly(2031, 7, 1));

        // July 2031 starts on a Tuesday: one blank, then 31 days, padded to five weeks.
        Assert.Equal(35, cells.Count);
        Assert.Equal(string.Empty, cells[0].DayOfMonth);
        Assert.Equal("1", cells[1].DayOfMonth);
        Assert.False(cells[1].IsInSeason);

        var opener = cells[7];
        Assert.Equal("7", opener.DayOfMonth);
        Assert.Equal("@ BRA", opener.Matchup);
        Assert.Equal("W", opener.Result);
        Assert.Equal("100–90", opener.Detail);
        Assert.Equal("Preseason", opener.Marker);

        Assert.True(cells[8].IsToday);
        Assert.False(cells[8].HasGame);

        var next = cells[9];
        Assert.True(next.IsUpcoming);
        Assert.Equal("vs CHA", next.Matchup);
        Assert.Equal("0-0", next.Detail);
    }

    [Fact]
    public void Month_KeepsNavigationInsideTheSeason()
    {
        var outlook = Outlook(currentDay: 0);

        Assert.Equal(new DateOnly(2031, 7, 1), outlook.Clamp(new DateOnly(2030, 1, 1)));
        Assert.Equal(new DateOnly(2031, 7, 1), outlook.Clamp(new DateOnly(2032, 1, 1)));
    }

    [Fact]
    public void Stretch_CombinesTheOpponentsRecordsAndCountsVenues()
    {
        var outlook = Outlook(
            currentDay: 2,
            Played(day: 1, home: Bravo, away: Charlie, homePoints: 100, awayPoints: 90),
            Upcoming(day: 3, home: Us, away: Bravo),
            Upcoming(day: 4, home: Charlie, away: Us));

        Assert.Equal("1 home · 1 away", outlook.StretchVenues);
        Assert.Equal(".500", outlook.StretchOpponents);
        Assert.Equal(1, outlook.StretchBackToBacks);
    }

    [Fact]
    public void Outlook_WithNoViewedTeamHasNoGames()
    {
        var outlook = SeasonOutlook.Build(Summary(0, []), [], teamId: null);

        Assert.False(outlook.HasTeam);
        Assert.Empty(outlook.RoadAhead());
        Assert.All(outlook.RibbonDays(), day => Assert.False(day.IsUpcomingGame));
    }

    [AvaloniaFact]
    public void CalendarScreen_FollowsTheViewedTeamAndOpensAGamesBoxScore()
    {
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        var season = shell.Season!;
        season.StartSeasonCommand.Execute(null);
        season.AdvanceDays = 14;
        season.AdvanceCommand.Execute(null);
        season.AdvanceCommand.Execute(null);

        shell.SelectedTeam = shell.Teams[1];

        Assert.Equal(shell.Teams[1].TeamId, season.ViewedTeamId);
        Assert.True(season.HasViewedTeam);
        Assert.NotEmpty(season.RoadAhead);
        Assert.Contains(shell.Teams[1].TeamName, season.TeamSeasonHeadline, StringComparison.Ordinal);

        var played = season.MonthCells.Concat(PreviousMonth(season)).First(cell => cell.IsPlayed);
        season.ShowGameCommand.Execute(played.GameId);

        Assert.True(season.HasSelectedFixture);
        Assert.Equal(played.GameId, season.SelectedFixture!.GameId);
    }

    private static IEnumerable<CalendarDayCell> PreviousMonth(SeasonViewModel season)
    {
        season.PreviousMonthCommand.Execute(null);
        return season.MonthCells;
    }

    private static SeasonOutlook Outlook(int currentDay, params FixtureLine[] fixtures)
    {
        var schedule = fixtures
            .GroupBy(fixture => fixture.Day)
            .OrderBy(group => group.Key)
            .Select(group => new ScheduleDayLine(group.Key, group.First().Date, "RegularSeason", group.ToList()))
            .ToList();

        return SeasonOutlook.Build(Summary(currentDay, fixtures), schedule, Us);
    }

    private static SeasonSummary Summary(int currentDay, IReadOnlyList<FixtureLine> fixtures)
    {
        var start = DateOnly.Parse(SeasonStart, System.Globalization.CultureInfo.InvariantCulture);
        var phases = new[]
        {
            new CalendarPhaseLine("Preseason", 0, 5, SeasonStart, "2031-07-11", currentDay < 5),
            new CalendarPhaseLine("RegularSeason", 5, 20, "2031-07-12", "2031-07-26", currentDay >= 5),
        };
        var calendar = new SeasonCalendarSummary(
            2031, SeasonStart, currentDay, Date(start, currentDay), "Preseason", 20, false,
            fixtures.Count(fixture => fixture.Played), fixtures.Count, phases);

        var rows = new[] { Us, Bravo, Charlie }
            .Select((team, index) =>
            {
                var played = fixtures.Where(fixture => fixture.Played && (fixture.HomeTeamId == team || fixture.AwayTeamId == team)).ToList();
                var wins = played.Count(fixture => (fixture.HomeTeamId == team) == (fixture.HomePoints > fixture.AwayPoints));
                var pointsFor = played.Sum(fixture => fixture.HomeTeamId == team ? fixture.HomePoints!.Value : fixture.AwayPoints!.Value);
                var pointsAgainst = played.Sum(fixture => fixture.HomeTeamId == team ? fixture.AwayPoints!.Value : fixture.HomePoints!.Value);
                return new StandingsLine(index + 1, team, Name(team), null, null, wins, played.Count - wins, played.Count, null, null, null, null, pointsFor, pointsAgainst, pointsFor - pointsAgainst);
            })
            .ToList();

        return new SeasonSummary(calendar, new StandingsSummary(false, [], rows, []), [], [], []);
    }

    private static FixtureLine Played(int day, string home, string away, int homePoints, int awayPoints) =>
        new($"game-{day}-{home}", day, Date(day), "RegularSeason", home, Name(home), away, Name(away), true, homePoints, awayPoints);

    private static FixtureLine Upcoming(int day, string home, string away) =>
        new($"game-{day}-{home}", day, Date(day), "RegularSeason", home, Name(home), away, Name(away), false, null, null);

    private static string Date(int day) =>
        Date(DateOnly.Parse(SeasonStart, System.Globalization.CultureInfo.InvariantCulture), day);

    private static string Date(DateOnly start, int day) =>
        start.AddDays(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Name(string team) => team switch
    {
        Us => "Alpha City",
        Bravo => "Bravo Town",
        _ => "Charlie Harbour",
    };
}
