using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// How much a prospect's true rating is obscured, and how scouting investment narrows it.
/// <see cref="BaseConfidence"/> is what an unscouted prospect is known to, and
/// <see cref="InvestmentConfidence"/> — a <see cref="BandedScale"/> keyed by invested scouting points —
/// adds to that as a team spends more looking at one prospect. Both together are clamped to 0-100 by
/// <c>BallGM.Rules.Draft.ScoutingModel</c>, which is the only thing that reads this table.
/// <para>
/// <see cref="None"/> means this league models no scouting uncertainty at all: every prospect's true
/// rating is simply known. That is <see cref="BaseConfidence"/> of 100 and a <see cref="MaxRangeWidth"/>
/// of zero, which collapses every scouting range onto the true value regardless of investment — the
/// same "absence is a real configuration, not a default standing in for one" reading every other table
/// in this ruleset uses.
/// </para>
/// </summary>
public sealed record ScoutingRules
{
    private const string InvalidConfidenceCode = "ruleset.invalid_scouting_confidence";
    private const string NegativeRangeWidthCode = "ruleset.negative_scouting_range_width";

    private ScoutingRules(int baseConfidence, int maxRangeWidth, BandedScale investmentConfidence)
    {
        BaseConfidence = baseConfidence;
        MaxRangeWidth = maxRangeWidth;
        InvestmentConfidence = investmentConfidence;
    }

    /// <summary>No modelled uncertainty: every prospect's true rating is known outright.</summary>
    public static ScoutingRules None { get; } = new(100, 0, BandedScale.None);

    public bool IsConfigured => MaxRangeWidth > 0;

    /// <summary>Confidence (0-100) in a prospect nobody has spent any scouting investment on yet.</summary>
    public int BaseConfidence { get; }

    /// <summary>The width of the scouting range at zero confidence — how wide "no idea" is.</summary>
    public int MaxRangeWidth { get; }

    /// <summary>Additional confidence bought by scouting investment, keyed by points invested.</summary>
    public BandedScale InvestmentConfidence { get; }

    public static DomainOperationResult<ScoutingRules> Create(
        int baseConfidence,
        int maxRangeWidth,
        BandedScale? investmentConfidence = null)
    {
        var errors = new List<DomainError>();

        if (baseConfidence < 0 || baseConfidence > 100)
        {
            errors.Add(new DomainError(
                InvalidConfidenceCode,
                $"Base scouting confidence must be between 0 and 100, but was {baseConfidence}."));
        }

        if (maxRangeWidth < 0)
        {
            errors.Add(new DomainError(
                NegativeRangeWidthCode,
                $"The scouting range width at zero confidence cannot be negative, but was {maxRangeWidth}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<ScoutingRules>.Failure(errors.ToArray())
            : DomainOperationResult<ScoutingRules>.Success(
                new ScoutingRules(baseConfidence, maxRangeWidth, investmentConfidence ?? BandedScale.None));
    }
}
