using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>
/// Turns the configured phase lengths into the calendar a season is played against.
/// <para>
/// A phase a league configures as zero days long is left out of the calendar rather than included
/// as an empty stretch, so <c>LeagueCalendar.Has(SeasonPhase.Postseason)</c> is a straight answer
/// to "does this league hold a postseason" instead of a length comparison every caller has to
/// remember to make.
/// </para>
/// </summary>
public sealed class SeasonCalendarBuilder
{
    private const string PostseasonWithoutDaysCode = "calendar.postseason_configured_without_days";

    public DomainOperationResult<LeagueCalendar> Build(
        Season season,
        DateOnly seasonStart,
        ScheduleRules scheduleRules,
        PostseasonRules postseasonRules)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(scheduleRules);
        ArgumentNullException.ThrowIfNull(postseasonRules);

        if (postseasonRules.QualifyingTeamsPerConference > 0 && postseasonRules.PostseasonDays <= 0)
        {
            return DomainOperationResult<LeagueCalendar>.Failure(new DomainError(
                PostseasonWithoutDaysCode,
                $"This league qualifies {postseasonRules.QualifyingTeamsPerConference} teams per conference for a postseason that runs no days."));
        }

        var phases = new List<CalendarPhase>();
        var cursor = 0;

        cursor = Append(phases, SeasonPhase.Preseason, cursor, scheduleRules.PreseasonDays);
        cursor = Append(phases, SeasonPhase.RegularSeason, cursor, scheduleRules.RegularSeasonDays);
        cursor = Append(phases, SeasonPhase.Postseason, cursor, postseasonRules.IsConfigured ? postseasonRules.PostseasonDays : 0);
        Append(phases, SeasonPhase.Offseason, cursor, scheduleRules.OffseasonDays);

        return LeagueCalendar.Create(season, seasonStart, phases);
    }

    private static int Append(List<CalendarPhase> phases, SeasonPhase phase, int startDay, int lengthInDays)
    {
        if (lengthInDays <= 0)
        {
            return startDay;
        }

        phases.Add(new CalendarPhase(phase, new SeasonDay(startDay), new SeasonDay(startDay + lengthInDays)));
        return startDay + lengthInDays;
    }
}
