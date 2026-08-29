using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;

namespace BallGM.Domain.Seasons;

/// <summary>
/// Identifies one fixture inside one season.
/// <para>
/// Deliberately <em>derived from the fixture's coordinates</em> rather than minted with
/// <c>SortableId.NewId()</c>, which is the exception to the identifier rule in
/// <c>docs/domain-language.md</c> and the reason it is written down here. A minted identifier
/// carries a timestamp and eighty bits of randomness, so two runs of the same season from the same
/// seed would produce two different sets of game identifiers — and the per-game random stream is
/// derived from the seed <em>and the game identifier</em>, so the games themselves would then differ
/// too. Every determinism guarantee in this milestone rests on this identifier being a function of
/// the season, the day, and the slot, and of nothing else.
/// </para>
/// </summary>
public sealed record GameId
{
    public GameId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// The identifier for the <paramref name="slot"/>-th game played on <paramref name="day"/> of
    /// <paramref name="season"/>. Fixed width so the string order and the play order agree.
    /// </summary>
    public static GameId For(Season season, SeasonDay day, int slot)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(day);

        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "A game slot cannot be negative.");
        }

        return new GameId($"{season.Year:D4}-{day.Index:D4}-{slot:D3}");
    }

    public override string ToString() => Value;
}
