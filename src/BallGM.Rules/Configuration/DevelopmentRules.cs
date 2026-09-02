using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// How a player's rating moves with age, one season at a time. <see cref="GrowthCurve"/> is a
/// <see cref="BandedScale"/> keyed by age answering how many points a player below
/// <see cref="PeakAgeStart"/> gains that season; <see cref="DeclineCurve"/> is the same shape for a
/// player above <see cref="PeakAgeEnd"/>, read as a magnitude and applied as a loss. Between the two,
/// a player neither grows nor declines — the flat peak years every curve needs so growth does not run
/// straight into decline with no plateau between them.
/// <para>
/// Both curves are banded scales rather than a single growth-rate constant because real development
/// is not linear: a curve can front-load growth in a player's teens and taper it as they approach
/// their peak, and decline can accelerate rather than stay constant every year past it. Values are
/// always non-negative — <see cref="BandedScale"/> enforces that — and the sign each curve's number is
/// applied with is decided once, by which side of the peak the age falls on, not carried in the table.
/// </para>
/// <para>
/// This curve moves a single <see cref="Domain.Players.PlayerRating.Overall"/>, not a per-attribute
/// breakdown — <c>PlayerRating</c> is single-attribute today (see <c>docs/architecture.md</c>), and
/// per-attribute development curves are deferred to when that expansion actually happens rather than
/// approximated here against a field that does not exist yet.
/// </para>
/// </summary>
public sealed record DevelopmentRules
{
    private const string InvalidPeakRangeCode = "ruleset.invalid_development_peak_range";
    private const string NegativeVarianceCode = "ruleset.negative_development_variance";

    private DevelopmentRules(int peakAgeStart, int peakAgeEnd, BandedScale growthCurve, BandedScale declineCurve, int varianceRange)
    {
        PeakAgeStart = peakAgeStart;
        PeakAgeEnd = peakAgeEnd;
        GrowthCurve = growthCurve;
        DeclineCurve = declineCurve;
        VarianceRange = varianceRange;
    }

    /// <summary>A league that models no ageing at all: every player's rating stays exactly where it started.</summary>
    public static DevelopmentRules None { get; } = new(0, 0, BandedScale.None, BandedScale.None, 0);

    public bool IsConfigured => PeakAgeStart > 0;

    /// <summary>The first age a player is considered at their peak. Below this, <see cref="GrowthCurve"/> applies.</summary>
    public int PeakAgeStart { get; }

    /// <summary>The last age a player is considered at their peak. Above this, <see cref="DeclineCurve"/> applies.</summary>
    public int PeakAgeEnd { get; }

    /// <summary>Rating points gained that season, keyed by age, for a player below <see cref="PeakAgeStart"/>.</summary>
    public BandedScale GrowthCurve { get; }

    /// <summary>Rating points lost that season, keyed by age, for a player above <see cref="PeakAgeEnd"/>.</summary>
    public BandedScale DeclineCurve { get; }

    /// <summary>
    /// The seeded draw's spread: a player's actual movement is the curve's figure plus a uniform draw
    /// in <c>[-VarianceRange, VarianceRange]</c>, so two players of the same age do not develop in
    /// lockstep. Zero means development is exactly what the curve states, every time.
    /// </summary>
    public int VarianceRange { get; }

    public static DomainOperationResult<DevelopmentRules> Create(
        int peakAgeStart,
        int peakAgeEnd,
        BandedScale? growthCurve,
        BandedScale? declineCurve,
        int varianceRange)
    {
        var errors = new List<DomainError>();

        if (peakAgeStart <= 0 || peakAgeEnd < peakAgeStart)
        {
            errors.Add(new DomainError(
                InvalidPeakRangeCode,
                $"The peak age range must start above zero and end no earlier than it starts, but was {peakAgeStart}-{peakAgeEnd}."));
        }

        if (varianceRange < 0)
        {
            errors.Add(new DomainError(
                NegativeVarianceCode,
                $"Development variance cannot be negative, but was {varianceRange}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<DevelopmentRules>.Failure(errors.ToArray())
            : DomainOperationResult<DevelopmentRules>.Success(new DevelopmentRules(
                peakAgeStart, peakAgeEnd, growthCurve ?? BandedScale.None, declineCurve ?? BandedScale.None, varianceRange));
    }
}
