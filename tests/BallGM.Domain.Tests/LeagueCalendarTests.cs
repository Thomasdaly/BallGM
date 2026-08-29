using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;

namespace BallGM.Domain.Tests;

public sealed class LeagueCalendarTests
{
    private static readonly Season Season = new(2031);
    private static readonly DateOnly Opening = new(2031, 7, 1);

    [Fact]
    public void Calendar_MapsSeasonDayZeroOntoTheDayTheSeasonOpened()
    {
        var calendar = BuildCalendar();

        Assert.Equal(Opening, calendar.DateOn(SeasonDay.Opening));
        Assert.Equal(Opening.AddDays(9), calendar.DateOn(new SeasonDay(9)));
    }

    [Fact]
    public void Calendar_MapsADateBackOntoTheSeasonDayItFallsOn()
    {
        var calendar = BuildCalendar();

        var day = calendar.DayOn(Opening.AddDays(12));

        Assert.True(day.IsSuccess);
        Assert.Equal(12, day.Value.Index);
    }

    [Fact]
    public void Calendar_RejectsADateOutsideTheSeasonRatherThanClampingIt()
    {
        var calendar = BuildCalendar();

        var before = calendar.DayOn(Opening.AddDays(-1));
        var after = calendar.DayOn(Opening.AddDays(calendar.LengthInDays));

        Assert.True(before.IsFailure);
        Assert.True(after.IsFailure);
        Assert.Equal("calendar.date_outside_season", before.Errors[0].Code);
    }

    [Fact]
    public void Calendar_ReportsWhichPhaseADayFallsIn()
    {
        var calendar = BuildCalendar();

        Assert.Equal(SeasonPhase.Preseason, calendar.PhaseOn(new SeasonDay(2)).Value);
        Assert.Equal(SeasonPhase.RegularSeason, calendar.PhaseOn(new SeasonDay(5)).Value);
        Assert.Equal(SeasonPhase.Postseason, calendar.PhaseOn(new SeasonDay(26)).Value);
    }

    [Fact]
    public void Calendar_RefusesPhasesWithAGapBetweenThem()
    {
        var result = LeagueCalendar.Create(
            Season,
            Opening,
            [
                new CalendarPhase(SeasonPhase.Preseason, SeasonDay.Opening, new SeasonDay(5)),
                new CalendarPhase(SeasonPhase.RegularSeason, new SeasonDay(7), new SeasonDay(25)),
            ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "calendar.phases_not_contiguous");
    }

    [Fact]
    public void Calendar_RefusesPhasesThatDoNotStartOnTheOpeningDay()
    {
        var result = LeagueCalendar.Create(
            Season,
            Opening,
            [new CalendarPhase(SeasonPhase.RegularSeason, new SeasonDay(3), new SeasonDay(25))]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "calendar.phases_do_not_start_at_opening");
    }

    [Fact]
    public void Calendar_RefusesPhasesInAnOrderThatRunsASeasonBackwards()
    {
        var result = LeagueCalendar.Create(
            Season,
            Opening,
            [
                new CalendarPhase(SeasonPhase.RegularSeason, SeasonDay.Opening, new SeasonDay(5)),
                new CalendarPhase(SeasonPhase.Preseason, new SeasonDay(5), new SeasonDay(9)),
            ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "calendar.phases_out_of_order");
    }

    [Fact]
    public void Calendar_ReportsThatALeagueWithNoPostseasonHasNoPostseasonPhase()
    {
        var result = LeagueCalendar.Create(
            Season,
            Opening,
            [new CalendarPhase(SeasonPhase.RegularSeason, SeasonDay.Opening, new SeasonDay(20))]);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Has(SeasonPhase.Postseason));
        Assert.True(result.Value.FirstDayOf(SeasonPhase.Postseason).IsFailure);
    }

    [Fact]
    public void Calendar_RefusesADayPastItsEnd()
    {
        var calendar = BuildCalendar();

        var phase = calendar.PhaseOn(new SeasonDay(calendar.LengthInDays));

        Assert.True(phase.IsFailure);
        Assert.Equal("calendar.day_outside_season", phase.Errors[0].Code);
    }

    private static LeagueCalendar BuildCalendar()
    {
        var result = LeagueCalendar.Create(
            Season,
            Opening,
            [
                new CalendarPhase(SeasonPhase.Preseason, SeasonDay.Opening, new SeasonDay(4)),
                new CalendarPhase(SeasonPhase.RegularSeason, new SeasonDay(4), new SeasonDay(24)),
                new CalendarPhase(SeasonPhase.Postseason, new SeasonDay(24), new SeasonDay(30)),
            ]);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
