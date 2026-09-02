using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Development;

/// <summary>
/// Ages a player's rating by one season. Pure and total: given the same current rating, age, rules,
/// and <see cref="IRandomSource"/> state, always produces the same new rating.
/// <para>
/// Below <see cref="DevelopmentRules.PeakAgeStart"/> the rating moves by
/// <see cref="DevelopmentRules.GrowthCurve"/>'s figure for that age; above
/// <see cref="DevelopmentRules.PeakAgeEnd"/> it moves by the negative of
/// <see cref="DevelopmentRules.DeclineCurve"/>'s; inside the peak range it does not move at all before
/// variance. <see cref="DevelopmentRules.VarianceRange"/> then adds a uniform draw so two players the
/// same age do not track each other exactly, and the result is clamped to the rating scale by
/// <see cref="PlayerRating.Adjust"/> rather than this method having to reason about the edges itself.
/// </para>
/// </summary>
public static class PlayerDevelopmentModel
{
    public static DomainOperationResult<PlayerRating> Develop(
        PlayerRating currentRating,
        int age,
        DevelopmentRules rules,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(currentRating);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        if (!rules.IsConfigured)
        {
            return DomainOperationResult<PlayerRating>.Success(currentRating);
        }

        var curveDelta = age < rules.PeakAgeStart
            ? (int)(rules.GrowthCurve.ValueFor(age) ?? 0)
            : age > rules.PeakAgeEnd
                ? -(int)(rules.DeclineCurve.ValueFor(age) ?? 0)
                : 0;

        var variance = rules.VarianceRange > 0
            ? random.NextInt32(-rules.VarianceRange, rules.VarianceRange + 1)
            : 0;

        return DomainOperationResult<PlayerRating>.Success(currentRating.Adjust(curveDelta + variance));
    }
}
