namespace BallGM.Domain.Negotiations;

/// <summary>
/// A day inside a season, counted from the day the season's market opened. The unit
/// <c>NegotiationRules.OfferExpiryDays</c> is expressed in, and the only notion of elapsed time the
/// free-agency market has.
/// <para>
/// An index rather than a date, deliberately. There is no league calendar yet — it arrives with the
/// schedule — and an offer that expired because a wall clock moved would make a save irreproducible:
/// re-opening a league next week must not quietly expire everything in it. When the calendar lands,
/// it maps its own dates onto this index; nothing that reads an expiry has to change.
/// </para>
/// </summary>
public sealed record SeasonDay : IComparable<SeasonDay>
{
    public SeasonDay(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "A season day index cannot be negative.");
        }

        Index = index;
    }

    public int Index { get; }

    /// <summary>The day a season's market opens, and the default day everything before a calendar uses.</summary>
    public static SeasonDay Opening { get; } = new(0);

    public SeasonDay Plus(int days) => new(checked(Index + days));

    /// <summary>Days from <paramref name="earlier"/> to this day. Negative if this day came first.</summary>
    public int DaysSince(SeasonDay earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        return Index - earlier.Index;
    }

    public int CompareTo(SeasonDay? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Index.CompareTo(other.Index);
    }

    public static bool operator <(SeasonDay left, SeasonDay right) => left.CompareTo(right) < 0;

    public static bool operator >(SeasonDay left, SeasonDay right) => left.CompareTo(right) > 0;

    public static bool operator <=(SeasonDay left, SeasonDay right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SeasonDay left, SeasonDay right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"day {Index}";
}
