namespace BallGM.Domain.Players;

/// <summary>
/// A player's full sheet, as a scout would keep it: the visible attributes (<see cref="PlayerAttribute"/>,
/// 0–100, in six groups), the hidden ones (<see cref="HiddenAttribute"/>), Potential Ability, body
/// measurements, how at home he is at each position, and his traits. <see cref="Overall"/> is his
/// Current Ability, derived from the visible attributes by <see cref="PlayerAttributes.AbilityWeight"/>
/// and never stored, the same "re-derived, never stored" reading the rest of this codebase gives a
/// value that could drift out of sync with what it is computed from.
/// <para>
/// The single-value constructor is kept deliberately: a sheet with no spread, for a test or a
/// hand-authored fixture where only the aggregate matters — every visible attribute at the value,
/// hidden ones at the middle, no body, no positions, no traits. Real players come from
/// <c>BallGM.Rules.Players.RatingProfileGenerator</c>; <see cref="With(PlayerAttribute, int)"/> shapes
/// a flat sheet for a test that needs one attribute away from the rest.
/// </para>
/// </summary>
public sealed record PlayerRating
{
    public const int MinimumOverall = 0;
    public const int MaximumOverall = 100;

    /// <summary>A hidden attribute nobody has measured: the league's middle.</summary>
    public const int NeutralHidden = 50;

    private readonly int[] _visible;
    private readonly int[] _hidden;

    public PlayerRating(
        IReadOnlyList<int> visible,
        IReadOnlyList<int> hidden,
        int potentialAbility,
        PlayerBody? body = null,
        IReadOnlyDictionary<Position, PositionFamiliarity>? positions = null,
        IReadOnlyCollection<PlayerTrait>? traits = null)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(hidden);
        if (visible.Count != PlayerAttributes.Count)
        {
            throw new ArgumentException($"A sheet states all {PlayerAttributes.Count} visible attributes.", nameof(visible));
        }

        if (hidden.Count != PlayerAttributes.HiddenCount)
        {
            throw new ArgumentException($"A sheet states all {PlayerAttributes.HiddenCount} hidden attributes.", nameof(hidden));
        }

        _visible = visible.Select(value => Validated(value, nameof(visible))).ToArray();
        _hidden = hidden.Select(value => Validated(value, nameof(hidden))).ToArray();
        Overall = PlayerAttributes.CurrentAbility(_visible);
        PotentialAbility = Validated(potentialAbility, nameof(potentialAbility));
        Body = body;
        Positions = positions is null ? new Dictionary<Position, PositionFamiliarity>() : new Dictionary<Position, PositionFamiliarity>(positions);
        Traits = traits is null ? [] : traits.Distinct().Order().ToArray();
    }

    /// <summary>A copy sharing an already-valid sheet's parts, with new visible or hidden values the caller has validated.</summary>
    private PlayerRating(PlayerRating source, int[] visible, int[] hidden, PlayerBody? body)
    {
        _visible = visible;
        _hidden = hidden;
        Overall = PlayerAttributes.CurrentAbility(visible);
        PotentialAbility = source.PotentialAbility;
        Body = body;
        Positions = source.Positions;
        Traits = source.Traits;
    }

    /// <summary>A flat sheet: every visible attribute at <paramref name="overall"/>, so <see cref="Overall"/> equals it exactly.</summary>
    public PlayerRating(int overall)
        : this(Enumerable.Repeat(overall, PlayerAttributes.Count).ToArray(), Enumerable.Repeat(NeutralHidden, PlayerAttributes.HiddenCount).ToArray(), overall)
    {
    }

    public int this[PlayerAttribute attribute] => _visible[(int)attribute];

    public int Hidden(HiddenAttribute attribute) => _hidden[(int)attribute];

    /// <summary>Every visible attribute in sheet order.</summary>
    public IReadOnlyList<int> Visible => _visible;

    /// <summary>Every hidden attribute in declaration order.</summary>
    public IReadOnlyList<int> HiddenValues => _hidden;

    /// <summary>
    /// Current Ability: the weighted mean of the visible attributes. The number everything that has
    /// not learned the sheet still reads. Worked out once — the sheet is immutable — and never saved.
    /// </summary>
    public int Overall { get; }

    /// <summary>The ceiling development approaches. Never below where he is today in a valid sheet, but not enforced: a pack may state otherwise.</summary>
    public int PotentialAbility { get; }

    public PlayerBody? Body { get; }

    /// <summary>How at home he is at each position; a position missing from the map has not been assessed.</summary>
    public IReadOnlyDictionary<Position, PositionFamiliarity> Positions { get; }

    public IReadOnlyList<PlayerTrait> Traits { get; }

    /// <summary>Ball skills: handling, passing and vision together — what the engine reads as a handler's security.</summary>
    public int Playmaking => (this[PlayerAttribute.BallHandling] + this[PlayerAttribute.Passing] + this[PlayerAttribute.Vision]) / 3;

    /// <summary>Feet: speed, first step and lateral quickness together.</summary>
    public int Quickness => (this[PlayerAttribute.Speed] + this[PlayerAttribute.FirstStep] + this[PlayerAttribute.LateralQuickness]) / 3;

    /// <summary>The same sheet with one visible attribute changed.</summary>
    public PlayerRating With(PlayerAttribute attribute, int value)
    {
        var visible = _visible.ToArray();
        visible[(int)attribute] = Validated(value, nameof(value));
        return new PlayerRating(this, visible, _hidden, Body);
    }

    /// <summary>The same sheet with one hidden attribute changed.</summary>
    public PlayerRating With(HiddenAttribute attribute, int value)
    {
        var hidden = _hidden.ToArray();
        hidden[(int)attribute] = Validated(value, nameof(value));
        return new PlayerRating(this, _visible, hidden, Body);
    }

    /// <summary>The same sheet with measurements.</summary>
    public PlayerRating WithBody(PlayerBody? body) => new(this, _visible, _hidden, body);

    /// <summary>
    /// A new sheet <paramref name="delta"/> points from this one in every visible attribute, each
    /// clamped to the scale independently rather than throwing at an extreme. Hidden attributes,
    /// potential, body, positions and traits do not move with it.
    /// </summary>
    public PlayerRating Adjust(int delta) =>
        new(this, _visible.Select(value => Math.Clamp(value + delta, MinimumOverall, MaximumOverall)).ToArray(), _hidden, Body);

    public bool Equals(PlayerRating? other) =>
        other is not null &&
        _visible.AsSpan().SequenceEqual(other._visible) &&
        _hidden.AsSpan().SequenceEqual(other._hidden) &&
        PotentialAbility == other.PotentialAbility &&
        Body == other.Body &&
        Positions.Count == other.Positions.Count &&
        Positions.All(entry => other.Positions.TryGetValue(entry.Key, out var familiarity) && familiarity == entry.Value) &&
        Traits.SequenceEqual(other.Traits);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _visible)
        {
            hash.Add(value);
        }

        hash.Add(PotentialAbility);
        hash.Add(Body);
        return hash.ToHashCode();
    }

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
