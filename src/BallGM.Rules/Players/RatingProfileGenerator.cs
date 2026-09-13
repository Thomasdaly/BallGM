using BallGM.Domain.Players;
using BallGM.Domain.Randomness;

namespace BallGM.Rules.Players;

/// <summary>
/// Turns one already-decided "how good is this player" number into a real <see cref="PlayerRating"/>
/// with five attributes that vary against each other rather than moving in lockstep — the shared
/// generator behind both <c>BallGM.Rules.Draft.ProspectGenerator</c> and the shipped league's initial
/// rosters (<c>BallGM.Infrastructure.Fixtures.FixtureLeagueDataSource</c>), so the correlation
/// structure the sim audit measures (<c>first_pc_variance_share</c>) is real everywhere a player is
/// generated, not just in one of the two places players come from.
/// <para>
/// Two independent draws, not one: a <em>frame</em> — a big-vs-small archetype axis, drawn once and
/// applied with a different sign and weight per attribute — and per-attribute <em>noise</em>. The
/// frame is what gives <see cref="PlayerRating.Height"/> a real, structural negative correlation with
/// <see cref="PlayerRating.LateralQuickness"/> and <see cref="PlayerRating.Speed"/> (and a positive one
/// with <see cref="PlayerRating.Strength"/>) — two of the six couplings
/// <c>.claude/skills/sim-audit/targets.json</c> names, grounded in generation rather than asserted.
/// <see cref="PlayerRating.Passing"/> gets no frame contribution at all: it is not a size trait.
/// </para>
/// <para>
/// Every attribute clamps to the caller's own <c>lowerBound</c>/<c>upperBound</c>, not to
/// <see cref="PlayerRating.MinimumOverall"/>/<see cref="PlayerRating.MaximumOverall"/> — a draft
/// class stating a 30-85 true-rating band should never produce an 90-rated attribute just because the
/// frame pushed it there, or the ruleset's own stated bound would stop meaning anything.
/// </para>
/// </summary>
public static class RatingProfileGenerator
{
    /// <summary>
    /// The frame and noise magnitude, in points either side of zero. Tuned empirically (the same
    /// iterate-and-measure workflow <c>FixtureLeagueDataSource.TeamStrengthOffsets</c> already
    /// documents for the same reason) against a large generated sample's <c>first_pc_variance_share</c>
    /// — measured at ≈0.447 at this value (n=20,000, talent uniform over 40-90), against a target of
    /// 0.45 and a band of [0.20, 0.70]. A shared `talent` value dominates the covariance by
    /// construction (it feeds all five attributes identically), so the spread needed to bring the
    /// first principal component down to band is larger than it looks at first glance — 9 (the
    /// original guess) still measured 0.86, well outside band. Re-measure before changing it.
    /// </summary>
    public const int DefaultSpread = 28;

    public static PlayerRating Generate(int talent, int lowerBound, int upperBound, IRandomSource random) =>
        Generate(talent, lowerBound, upperBound, DefaultSpread, random);

    public static PlayerRating Generate(int talent, int lowerBound, int upperBound, int spread, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (lowerBound > upperBound)
        {
            throw new ArgumentException($"Lower bound ({lowerBound}) cannot exceed upper bound ({upperBound}).", nameof(lowerBound));
        }

        if (spread < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spread), spread, "Spread cannot be negative.");
        }

        var frame = spread == 0 ? 0 : random.NextInt32(-spread, spread + 1);

        var height = talent + frame;
        var strength = talent + (frame / 2);
        var speed = talent - ((frame * 6) / 10);
        var lateralQuickness = talent - ((frame * 8) / 10);
        var passing = talent;

        return new PlayerRating(
            Clamp(height + Noise(spread, random), lowerBound, upperBound),
            Clamp(speed + Noise(spread, random), lowerBound, upperBound),
            Clamp(strength + Noise(spread, random), lowerBound, upperBound),
            Clamp(passing + Noise(spread, random), lowerBound, upperBound),
            Clamp(lateralQuickness + Noise(spread, random), lowerBound, upperBound));
    }

    private static int Noise(int spread, IRandomSource random) =>
        spread == 0 ? 0 : random.NextInt32(-spread, spread + 1);

    private static int Clamp(int value, int lowerBound, int upperBound) => Math.Clamp(value, lowerBound, upperBound);
}
