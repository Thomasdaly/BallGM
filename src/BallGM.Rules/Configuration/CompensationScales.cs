using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// The minimum per-season compensation a league permits, scaling with seasons of service. A typed
/// wrapper over <see cref="BandedScale"/> so callers hand in a service count and get
/// <see cref="Money"/> back, rather than passing longs around.
/// <para>
/// A league whose minimum salary does not vary by service is a league where every veteran signs for
/// the rookie minimum, and the free-agency market stops meaning anything — which is why this is a
/// table rather than a scalar. An unconfigured scale means the league has no minimum at all, and
/// with it goes the minimum-salary signing route, which would have nothing to pay.
/// </para>
/// </summary>
public sealed record CompensationFloorScale
{
    private CompensationFloorScale(BandedScale scale) => Scale = scale;

    /// <summary>A league that sets no minimum salary.</summary>
    public static CompensationFloorScale None { get; } = new(BandedScale.None);

    public BandedScale Scale { get; }

    public bool IsConfigured => !Scale.IsEmpty;

    /// <summary>
    /// Wraps a scale that has already been validated — the Application layer carries the table as a
    /// plain <see cref="BandedScale"/>, because it cannot reference the rules project, and this is
    /// where it comes back into the rules layer's own vocabulary.
    /// </summary>
    public static CompensationFloorScale From(BandedScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return new CompensationFloorScale(scale);
    }

    public static DomainOperationResult<CompensationFloorScale> Create(IEnumerable<ScaleBand>? bands)
    {
        var scaleResult = BandedScale.Create(bands);
        return scaleResult.IsFailure
            ? DomainOperationResult<CompensationFloorScale>.Failure(scaleResult.Errors.ToArray())
            : DomainOperationResult<CompensationFloorScale>.Success(new CompensationFloorScale(scaleResult.Value));
    }

    /// <summary>
    /// The minimum this league will pay a player with this much service, or <c>null</c> when the
    /// league sets no minimum. Null rather than <see cref="Money.Zero"/>: a minimum of nothing and
    /// no minimum are different rules, and only one of them is worth telling a GM about.
    /// </summary>
    public Money? FloorFor(int seasonsOfService)
    {
        var value = Scale.ValueFor(seasonsOfService);
        return value is null ? null : new Money(value.Value);
    }
}

/// <summary>
/// The highest per-season compensation any one player may be paid, as a share of the soft cap,
/// rising with seasons of service. Configured as a percentage rather than an amount so that a
/// league raising its cap raises every ceiling with it, without a second edit that can be forgotten.
/// </summary>
public sealed record CompensationCeilingScale
{
    private const string NonPositivePercentCode = "ruleset.non_positive_ceiling_percent";

    private CompensationCeilingScale(BandedScale scale) => Scale = scale;

    /// <summary>A league that sets no maximum salary.</summary>
    public static CompensationCeilingScale None { get; } = new(BandedScale.None);

    public BandedScale Scale { get; }

    public bool IsConfigured => !Scale.IsEmpty;

    /// <summary>Wraps a scale that has already been validated. See the floor scale's own remarks.</summary>
    public static CompensationCeilingScale From(BandedScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return new CompensationCeilingScale(scale);
    }

    public static DomainOperationResult<CompensationCeilingScale> Create(IEnumerable<ScaleBand>? bands)
    {
        var scaleResult = BandedScale.Create(bands);
        if (scaleResult.IsFailure)
        {
            return DomainOperationResult<CompensationCeilingScale>.Failure(scaleResult.Errors.ToArray());
        }

        // Zero is a legal band value in general — a scale of amounts may honestly contain one — but a
        // ceiling of nought percent says no player may be paid anything, which no ruleset means.
        var zeroBand = scaleResult.Value.Bands.FirstOrDefault(band => band.Value <= 0);
        if (zeroBand is not null)
        {
            return DomainOperationResult<CompensationCeilingScale>.Failure(new DomainError(
                NonPositivePercentCode,
                $"The compensation ceiling band starting at {zeroBand.MinimumKey} seasons of service is {zeroBand.Value}% of the soft cap. A ceiling of nothing bars every signing; leave the table out if this league has no maximum salary."));
        }

        return DomainOperationResult<CompensationCeilingScale>.Success(new CompensationCeilingScale(scaleResult.Value));
    }

    /// <summary>The ceiling as a percentage of the soft cap, or <c>null</c> when unconfigured.</summary>
    public int? PercentFor(int seasonsOfService)
    {
        var value = Scale.ValueFor(seasonsOfService);
        return value is null ? null : checked((int)value.Value);
    }

    /// <summary>
    /// The ceiling in money, given the league's soft cap. <c>null</c> when this league sets no
    /// ceiling — and, by construction, it cannot set one without a soft cap to take a share of:
    /// <see cref="NegotiationRules.Create"/> refuses that combination at load.
    /// <para>
    /// Truncates rather than rounds. A ceiling that rounds up is a ceiling a team can sit one unit
    /// above while the arithmetic still calls the contract legal.
    /// </para>
    /// </summary>
    public Money? CeilingFor(int seasonsOfService, Money? softCap)
    {
        var percent = PercentFor(seasonsOfService);
        return percent is null || softCap is null
            ? null
            : new Money(softCap.SmallestUnits * percent.Value / 100);
    }
}
