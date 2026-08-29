using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;

namespace BallGM.Domain.Seasons;

/// <summary>
/// A stretch of days one player is unavailable for, inside one season.
/// <para>
/// Kept on the season rather than on <see cref="Player"/> because it is bounded by season days, and
/// <see cref="Player.CurrentInjury"/> answers a different question — whether someone is hurt right
/// now, which is what a roster screen and a trade's injured-player eligibility rule read. A spell
/// says <em>until when</em>, and "until when" only means something against a calendar.
/// </para>
/// </summary>
public sealed record InjurySpell
{
    public InjurySpell(PlayerId playerId, string description, SeasonDay fromDay, SeasonDay untilDayExclusive)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(fromDay);
        ArgumentNullException.ThrowIfNull(untilDayExclusive);

        if (untilDayExclusive <= fromDay)
        {
            throw new ArgumentException(
                $"An injury spell for player '{playerId.Value}' has to cost at least one day.",
                nameof(untilDayExclusive));
        }

        PlayerId = playerId;
        Description = description;
        FromDay = fromDay;
        UntilDayExclusive = untilDayExclusive;
    }

    public PlayerId PlayerId { get; }

    public string Description { get; }

    public SeasonDay FromDay { get; }

    public SeasonDay UntilDayExclusive { get; }

    public int LengthInDays => UntilDayExclusive.Index - FromDay.Index;

    public bool Covers(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return day >= FromDay && day < UntilDayExclusive;
    }
}
