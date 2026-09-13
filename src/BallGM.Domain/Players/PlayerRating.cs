namespace BallGM.Domain.Players;

/// <summary>
/// A player's skill profile: five independent attributes rather than one scalar, so a tall,
/// ground-bound centre and a short, darting guard can both carry the same <see cref="Overall"/>
/// without being the same player. <see cref="Overall"/> is derived from the five rather than stored,
/// the same "re-derived, never stored" reading the rest of this codebase gives a value that could
/// otherwise drift out of sync with what it is computed from.
/// <para>
/// The single-value constructor is kept deliberately: a rating with no attribute spread, for a test
/// or a hand-authored fixture where only the aggregate number matters. Real players are meant to come
/// from <c>BallGM.Rules.Players.RatingProfileGenerator</c>, not from five equal attributes — a rating
/// built this way is flat by construction and should never be mistaken for a generated one.
/// </para>
/// </summary>
public sealed record PlayerRating
{
    public const int MinimumOverall = 0;
    public const int MaximumOverall = 100;

    public PlayerRating(int height, int speed, int strength, int passing, int lateralQuickness)
    {
        Height = Validated(height, nameof(height));
        Speed = Validated(speed, nameof(speed));
        Strength = Validated(strength, nameof(strength));
        Passing = Validated(passing, nameof(passing));
        LateralQuickness = Validated(lateralQuickness, nameof(lateralQuickness));
    }

    /// <summary>A flat rating: every attribute set to the same value, so <see cref="Overall"/> equals it exactly.</summary>
    public PlayerRating(int overall)
        : this(overall, overall, overall, overall, overall)
    {
    }

    public int Height { get; }

    public int Speed { get; }

    public int Strength { get; }

    public int Passing { get; }

    public int LateralQuickness { get; }

    /// <summary>The single number everything outside the rating attributes themselves still reads.</summary>
    public int Overall => (Height + Speed + Strength + Passing + LateralQuickness) / 5;

    /// <summary>
    /// A new rating <paramref name="delta"/> points from this one, every attribute clamped to the
    /// scale independently rather than throwing at an extreme — the growth and decline a development
    /// curve applies are unbounded by construction, and a prospect already at 100 ageing past their
    /// peak should decline normally rather than the clamp becoming the caller's problem. Every
    /// attribute moves by the same delta: there is no per-attribute development curve yet, so ageing
    /// still reads as "one curve," the same shape it had as a single scalar.
    /// </summary>
    public PlayerRating Adjust(int delta) => new(
        Clamp(Height + delta),
        Clamp(Speed + delta),
        Clamp(Strength + delta),
        Clamp(Passing + delta),
        Clamp(LateralQuickness + delta));

    private static int Clamp(int value) => Math.Clamp(value, MinimumOverall, MaximumOverall);

    private static int Validated(int value, string paramName)
    {
        if (value < MinimumOverall || value > MaximumOverall)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Rating attributes must be between {MinimumOverall} and {MaximumOverall}.");
        }

        return value;
    }
}
