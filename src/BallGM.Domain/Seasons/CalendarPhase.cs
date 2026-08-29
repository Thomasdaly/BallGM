using BallGM.Domain.Negotiations;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One stretch of a season's calendar: a phase and the half-open range of season days it covers.
/// Half-open so that two adjacent phases share a boundary day index without both claiming it — a
/// day belonging to two phases is a day two rules disagree about.
/// </summary>
public sealed record CalendarPhase
{
    public CalendarPhase(SeasonPhase phase, SeasonDay startDay, SeasonDay endDayExclusive)
    {
        ArgumentNullException.ThrowIfNull(startDay);
        ArgumentNullException.ThrowIfNull(endDayExclusive);

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Phase must be a defined season phase.");
        }

        if (endDayExclusive <= startDay)
        {
            throw new ArgumentException(
                $"Phase '{phase}' would run from {startDay} to {endDayExclusive}, which is not a stretch of days.",
                nameof(endDayExclusive));
        }

        Phase = phase;
        StartDay = startDay;
        EndDayExclusive = endDayExclusive;
    }

    public SeasonPhase Phase { get; }

    public SeasonDay StartDay { get; }

    public SeasonDay EndDayExclusive { get; }

    public int LengthInDays => EndDayExclusive.Index - StartDay.Index;

    /// <summary>The last day this phase actually covers, which is one before the exclusive end.</summary>
    public SeasonDay LastDay => new(EndDayExclusive.Index - 1);

    public bool Contains(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return day >= StartDay && day < EndDayExclusive;
    }
}
