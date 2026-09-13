using BallGM.Domain.Players;

namespace BallGM.Domain.AI;

/// <summary>
/// Which factor a valuation reading belongs to. Kept as a closed, typed set — rather than a bare
/// rule-code string — so a caller can ask for one factor by name the way <c>PreferenceContribution</c>
/// already lets a market screen ask for one negotiation factor by name.
/// </summary>
public enum ValuationFactorKind
{
    Production = 1,
    Trajectory = 2,
    Cost = 3,
    Control = 4,
}

/// <summary>
/// One factor's reading in an asset valuation, 0-100, with the rule code and sentence that produced
/// it. The same "never a total" shape <c>OfferPreference</c>/<c>PreferenceContribution</c> use for a
/// player's read of a contract offer, applied here to a front office's read of an asset: a GM asking
/// "why does the AI value this player so highly" deserves an answer per factor, not a blended number
/// with the reasoning baked in and unrecoverable. Ranking two valuations against each other — the
/// ordered, materiality-banded walk <c>PreferenceRanking</c> does for offers — is deliberately not
/// built here; nothing consumes a ranking yet, and it belongs with whichever later slice (trade or
/// free-agent targeting) is the first actual consumer.
/// </summary>
public sealed record ValuationContribution(ValuationFactorKind Factor, int Reading, string RuleCode, string Explanation)
{
    public const int MinimumReading = 0;
    public const int MaximumReading = 100;

    /// <summary>Clamps a computed reading into the scale. Every factor's arithmetic ends here.</summary>
    public static int Clamp(int reading) => Math.Clamp(reading, MinimumReading, MaximumReading);
}

/// <summary>
/// A front office's read of one player, decomposed into the factors that produced it. See
/// <see cref="ValuationContribution"/> for why there is no overall figure.
/// </summary>
public sealed record PlayerValuation(PlayerId PlayerId, IReadOnlyList<ValuationContribution> Contributions)
{
    /// <summary>This valuation's reading on one factor, or <c>null</c> if the model did not produce one.</summary>
    public ValuationContribution? Factor(ValuationFactorKind kind) =>
        Contributions.FirstOrDefault(contribution => contribution.Factor == kind);
}
