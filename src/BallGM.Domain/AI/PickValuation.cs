using BallGM.Domain.DraftAssets;

namespace BallGM.Domain.AI;

/// <summary>
/// Which factor a draft-pick valuation reading belongs to. Kept separate from
/// <see cref="ValuationFactorKind"/> because a pick and a player are valued on different axes —
/// there is no "cost" or "control" reading for an asset that has not been drafted yet.
/// </summary>
public enum PickValuationFactorKind
{
    Round = 1,
    YearsOut = 2,
}

/// <summary>One factor's reading in a draft-pick valuation. See <see cref="ValuationContribution"/> for the shape this mirrors.</summary>
public sealed record PickValuationContribution(PickValuationFactorKind Factor, int Reading, string RuleCode, string Explanation)
{
    public const int MinimumReading = 0;
    public const int MaximumReading = 100;

    public static int Clamp(int reading) => Math.Clamp(reading, MinimumReading, MaximumReading);
}

/// <summary>
/// A front office's read of one draft pick, decomposed into the factors that produced it. Deliberately
/// generic against round and distance only — no selection number exists until a lottery has run, and
/// no protection is weighed in yet; see <c>BallGM.Rules.AI.AssetValuationModel</c> for what is deferred
/// and why.
/// </summary>
public sealed record PickValuation(DraftPickId PickId, IReadOnlyList<PickValuationContribution> Contributions)
{
    public PickValuationContribution? Factor(PickValuationFactorKind kind) =>
        Contributions.FirstOrDefault(contribution => contribution.Factor == kind);
}
