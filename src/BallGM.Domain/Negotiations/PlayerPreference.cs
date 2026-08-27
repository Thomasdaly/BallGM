using BallGM.Domain.Teams;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// What a free agent weighs. Four factors, each answering its own question and none of them able to
/// speak for the others — a player who signed "because the score was 68" has not been given a reason.
/// </summary>
public enum PreferenceFactorKind
{
    /// <summary>What it pays, measured against what this league permits anyone to pay them.</summary>
    Money = 0,

    /// <summary>How many seasons, measured against how many they want at this age and service.</summary>
    Term = 1,

    /// <summary>Whether the squad has room for them at their position, and how good it is.</summary>
    TeamFit = 2,

    /// <summary>How this offer stands against the rest of the market for them.</summary>
    MarketDemand = 3,
}

/// <summary>
/// One factor's say on one offer. <paramref name="Score"/> is that factor's own 0–100 reading, never
/// a share of anything: nothing adds these up.
/// <para>
/// <paramref name="MaterialityBand"/> is how much better one offer has to be on this factor before
/// the difference means anything to the player. It is what makes "genuinely indifferent" a definable
/// state rather than an exact-tie coincidence, and it is therefore what bounds where a seeded draw
/// is allowed to decide anything.
/// </para>
/// </summary>
public sealed record PreferenceContribution(
    PreferenceFactorKind Factor,
    int Score,
    int MaterialityBand,
    string RuleCode,
    string Explanation)
{
    public const int MinimumScore = 0;
    public const int MaximumScore = 100;

    /// <summary>Clamps a computed reading into the scale. Every factor's arithmetic ends here.</summary>
    public static int Clamp(int score) => Math.Clamp(score, MinimumScore, MaximumScore);
}

/// <summary>
/// How one player reads one offer: every factor's contribution, kept apart, plus whether the offer
/// clears what they will accept at all.
/// <para>
/// There is deliberately no total. Ranking is <see cref="PreferenceRanking"/>'s job and it compares
/// factor by factor, so the answer to "why did he take that one" is always a sentence about a
/// specific factor rather than a number nobody can argue with.
/// </para>
/// </summary>
public sealed record OfferPreference(
    OfferId OfferId,
    TeamId TeamId,
    IReadOnlyList<PreferenceContribution> Contributions,
    bool MeetsReservation,
    string ReservationRuleCode,
    string ReservationExplanation)
{
    /// <summary>This offer's reading on one factor, or <c>null</c> if the model did not produce one.</summary>
    public PreferenceContribution? Factor(PreferenceFactorKind kind) =>
        Contributions.FirstOrDefault(contribution => contribution.Factor == kind);
}

/// <summary>
/// Which of two offers a player prefers. <paramref name="Sign"/> is positive when the left offer
/// wins, negative when the right does, and zero when the model genuinely cannot separate them —
/// which is the only circumstance in which anything random is allowed to decide.
/// </summary>
public sealed record PreferenceComparison(int Sign, PreferenceFactorKind? DecidedBy, string Explanation)
{
    public bool IsIndifferent => Sign == 0;
}

/// <summary>
/// Ranks offers without ever forming a score.
/// <para>
/// The comparison walks the factors in a fixed order and stops at the first one where the two offers
/// differ by more than that factor's materiality band. A factor inside its band has no opinion and
/// hands over to the next; a player does not move towns over $200k, and a model that lets him is a
/// model where money quietly decides everything.
/// </para>
/// <para>
/// Money leads the order because it is the factor a GM is actually bidding with — a market where the
/// biggest cheque loses to a marginal fit reading is a market a GM cannot play. Term follows, then
/// fit, then how the offer stands against the field. Nothing here is stochastic: identical inputs
/// give an identical order every run, and the seeded draw only ever sees offers this comparison has
/// declared it cannot separate.
/// </para>
/// </summary>
public static class PreferenceRanking
{
    /// <summary>The order factors get their say in. Stated once, read by everything that ranks.</summary>
    public static IReadOnlyList<PreferenceFactorKind> FactorOrder { get; } =
    [
        PreferenceFactorKind.Money,
        PreferenceFactorKind.Term,
        PreferenceFactorKind.TeamFit,
        PreferenceFactorKind.MarketDemand,
    ];

    public static PreferenceComparison Compare(OfferPreference left, OfferPreference right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        foreach (var kind in FactorOrder)
        {
            var leftFactor = left.Factor(kind);
            var rightFactor = right.Factor(kind);

            if (leftFactor is null || rightFactor is null)
            {
                continue;
            }

            var difference = leftFactor.Score - rightFactor.Score;

            // The wider of the two bands, because indifference has to be symmetric: if A cannot tell
            // B apart from itself, B must not be able to tell A apart either, or the comparison stops
            // being an ordering at all.
            var band = Math.Max(leftFactor.MaterialityBand, rightFactor.MaterialityBand);

            if (Math.Abs(difference) <= band)
            {
                continue;
            }

            var winner = difference > 0 ? leftFactor : rightFactor;

            return new PreferenceComparison(
                Math.Sign(difference),
                kind,
                $"{Describe(kind)} decided it: {winner.Explanation}");
        }

        return new PreferenceComparison(
            0,
            null,
            "Nothing separates these offers by more than this player would notice on any factor.");
    }

    public static string Describe(PreferenceFactorKind kind) => kind switch
    {
        PreferenceFactorKind.Money => "Money",
        PreferenceFactorKind.Term => "Term",
        PreferenceFactorKind.TeamFit => "Team fit",
        PreferenceFactorKind.MarketDemand => "Market demand",
        _ => kind.ToString(),
    };
}
