using BallGM.Domain.Randomness;

namespace BallGM.Domain.Seasons;

/// <summary>
/// The one number a whole season's randomness comes from.
/// <para>
/// It lives on the season being played rather than in the ruleset, because a ruleset is shared
/// configuration two saves can both load while a seed is what makes one save's season <em>this</em>
/// season. It is written into the season's save file so that re-opening a league and running the
/// rest of its schedule produces the games it would have produced without the interruption.
/// </para>
/// <para>
/// Nothing draws from this seed directly. Every consumer derives its own — the schedule from
/// <c>"schedule"</c>, each game from its own identifier — so that no consumer's results depend on
/// how much randomness some other consumer used first.
/// </para>
/// </summary>
public sealed record SeasonSeed(int Value)
{
    private const string ScheduleKey = "schedule";

    /// <summary>The seed the fixture list is drawn with.</summary>
    public int ForSchedule() => SeedMixer.Mix(Value, ScheduleKey);

    /// <summary>
    /// The seed one game is played with. Derived from the game's identifier, which is itself derived
    /// from the season, day, and slot — so the same fixture is always the same game.
    /// </summary>
    public int ForGame(GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        return SeedMixer.Mix(Value, gameId.Value);
    }

    /// <summary>The seed a named part of the season is drawn with — injuries on a day, a bracket draw.</summary>
    public int For(string purpose) => SeedMixer.Mix(Value, purpose);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
