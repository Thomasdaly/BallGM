using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One season's dates: the day it opened, and the contiguous run of phases laid across it.
/// <para>
/// <see cref="SeasonDay"/> remains the unit everything measures in — an offer expiry, a signing
/// window, a fixture's day — and this type is only the mapping between that index and a date a human
/// reads. Nothing in the rules or the simulation reads the date side: a season advanced by dates
/// would be a season that reproduced differently depending on which day of the week it started,
/// and an offer that expired because a wall clock moved is exactly the failure
/// <see cref="SeasonDay"/> was introduced to refuse.
/// </para>
/// <para>
/// Day 0 is the day the season opened, which is also the day the free-agency market opens. That is
/// what makes the mapping compatible with everything Milestone 6b already measures: the calendar
/// arrived after the index, and it maps onto the index rather than replacing it.
/// </para>
/// </summary>
public sealed class LeagueCalendar
{
    private const string NoPhasesCode = "calendar.no_phases";
    private const string PhasesNotContiguousCode = "calendar.phases_not_contiguous";
    private const string PhasesDoNotStartAtOpeningCode = "calendar.phases_do_not_start_at_opening";
    private const string DuplicatePhaseCode = "calendar.duplicate_phase";
    private const string PhasesOutOfOrderCode = "calendar.phases_out_of_order";
    private const string MissingStartDateCode = "calendar.missing_start_date";
    private const string DayOutsideSeasonCode = "calendar.day_outside_season";
    private const string DateOutsideSeasonCode = "calendar.date_outside_season";

    private readonly List<CalendarPhase> _phases;

    private LeagueCalendar(Season season, DateOnly seasonStart, List<CalendarPhase> phases)
    {
        Season = season;
        SeasonStart = seasonStart;
        _phases = phases;
    }

    /// <summary>
    /// Builds a calendar from an ordered, contiguous run of phases starting at
    /// <see cref="SeasonDay.Opening"/>. A gap or an overlap is a structured failure rather than a
    /// throw, because the phase lengths come from a ruleset file and a ruleset file is untrusted
    /// input: a calendar with a hole in it must fail the load, not the first advance that reaches
    /// the hole.
    /// </summary>
    public static DomainOperationResult<LeagueCalendar> Create(
        Season season,
        DateOnly seasonStart,
        IEnumerable<CalendarPhase> phases)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(phases);

        var ordered = phases.ToList();
        if (ordered.Any(phase => phase is null))
        {
            throw new ArgumentException("A calendar cannot contain null phases.", nameof(phases));
        }

        var errors = new List<DomainError>();

        if (seasonStart == default)
        {
            errors.Add(new DomainError(
                MissingStartDateCode,
                "A season calendar needs the date its opening day falls on, so a season day can be shown to a human as a date."));
        }

        if (ordered.Count == 0)
        {
            errors.Add(new DomainError(
                NoPhasesCode,
                "A season calendar has to cover at least one phase. A league whose calendar covers no days cannot be advanced at all."));

            return DomainOperationResult<LeagueCalendar>.Failure(errors.ToArray());
        }

        if (ordered[0].StartDay != SeasonDay.Opening)
        {
            errors.Add(new DomainError(
                PhasesDoNotStartAtOpeningCode,
                $"The first phase starts on {ordered[0].StartDay} rather than on the opening day. Season day 0 is the day the season opened, and everything measured in season days counts from it."));
        }

        var seen = new HashSet<SeasonPhase>();
        foreach (var phase in ordered)
        {
            if (!seen.Add(phase.Phase))
            {
                errors.Add(new DomainError(
                    DuplicatePhaseCode,
                    $"Phase '{phase.Phase}' appears more than once in the calendar. A season passes through each phase once."));
            }
        }

        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];

            if (current.StartDay != previous.EndDayExclusive)
            {
                errors.Add(new DomainError(
                    PhasesNotContiguousCode,
                    $"Phase '{current.Phase}' starts on {current.StartDay} but '{previous.Phase}' ran to {previous.EndDayExclusive}. A calendar with a gap or an overlap has days no rule can answer for."));
            }

            if (current.Phase <= previous.Phase)
            {
                errors.Add(new DomainError(
                    PhasesOutOfOrderCode,
                    $"Phase '{current.Phase}' follows '{previous.Phase}', which runs a season backwards."));
            }
        }

        return errors.Count > 0
            ? DomainOperationResult<LeagueCalendar>.Failure(errors.ToArray())
            : DomainOperationResult<LeagueCalendar>.Success(new LeagueCalendar(season, seasonStart, ordered));
    }

    public Season Season { get; }

    /// <summary>The date <see cref="SeasonDay.Opening"/> falls on. The only place a date enters the model.</summary>
    public DateOnly SeasonStart { get; }

    public IReadOnlyList<CalendarPhase> Phases => _phases;

    /// <summary>The day after the last day the calendar covers.</summary>
    public SeasonDay EndDayExclusive => _phases[^1].EndDayExclusive;

    public SeasonDay LastDay => _phases[^1].LastDay;

    public int LengthInDays => EndDayExclusive.Index;

    public bool Covers(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return day < EndDayExclusive;
    }

    /// <summary>
    /// Which phase a day falls in, or a structured failure if the calendar does not reach that day.
    /// Advancing past the end of a season is a thing a caller can legitimately try, so it is an
    /// answer rather than an exception.
    /// </summary>
    public DomainOperationResult<SeasonPhase> PhaseOn(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        var phase = _phases.FirstOrDefault(candidate => candidate.Contains(day));

        return phase is null
            ? DomainOperationResult<SeasonPhase>.Failure(new DomainError(
                DayOutsideSeasonCode,
                $"Season {Season.Year} runs {LengthInDays} days, so {day} falls outside it."))
            : DomainOperationResult<SeasonPhase>.Success(phase.Phase);
    }

    /// <summary>The first day of a phase, or a failure if this calendar does not have that phase at all.</summary>
    public DomainOperationResult<SeasonDay> FirstDayOf(SeasonPhase phase)
    {
        var found = _phases.FirstOrDefault(candidate => candidate.Phase == phase);

        return found is null
            ? DomainOperationResult<SeasonDay>.Failure(new DomainError(
                DayOutsideSeasonCode,
                $"Season {Season.Year} has no {phase} phase."))
            : DomainOperationResult<SeasonDay>.Success(found.StartDay);
    }

    public CalendarPhase? Phase(SeasonPhase phase) =>
        _phases.FirstOrDefault(candidate => candidate.Phase == phase);

    public bool Has(SeasonPhase phase) => Phase(phase) is not null;

    /// <summary>The date a season day falls on. Presentation only — no rule reads this.</summary>
    public DateOnly DateOn(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return SeasonStart.AddDays(day.Index);
    }

    /// <summary>
    /// The season day a date falls on. The inverse of <see cref="DateOn"/>, for a screen that lets a
    /// GM pick a date rather than an index; a date outside the season is an answer, not a crash.
    /// </summary>
    public DomainOperationResult<SeasonDay> DayOn(DateOnly date)
    {
        var offset = date.DayNumber - SeasonStart.DayNumber;

        return offset < 0 || offset >= LengthInDays
            ? DomainOperationResult<SeasonDay>.Failure(new DomainError(
                DateOutsideSeasonCode,
                $"{date:yyyy-MM-dd} is not a day in season {Season.Year}, which runs from {SeasonStart:yyyy-MM-dd} for {LengthInDays} days."))
            : DomainOperationResult<SeasonDay>.Success(new SeasonDay(offset));
    }
}
