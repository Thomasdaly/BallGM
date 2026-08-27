using BallGM.Domain.Common;

namespace BallGM.Domain.Cap;

/// <summary>
/// The configured financial boundaries, named generically rather than after any one real-world
/// league's current agreement. Mirrors <c>BallGM.Rules.Configuration.CapThresholds</c>, which owns
/// the amounts; this enum exists so a cap-sheet result can name which line it is talking about
/// without Domain depending on the Rules project.
/// <para>
/// The numbers are identity, not order. <see cref="PayrollFloor"/> sits <em>below</em>
/// <see cref="SoftCap"/> in money terms but is appended here so the existing members keep the
/// values they already had; the order a cap sheet presents them in comes from the cap ledger.
/// </para>
/// </summary>
public enum CapThresholdKind
{
    SoftCap = 0,
    LuxuryTax = 1,
    FirstApron = 2,
    SecondApron = 3,
    HardCap = 4,

    /// <summary>
    /// The minimum total payroll a team must reach. The only threshold a team breaches by being
    /// <em>under</em> it — see <see cref="ThresholdStanding.IsBreached"/>.
    /// </summary>
    PayrollFloor = 5,
}

/// <summary>Where a payroll sits relative to one threshold.</summary>
public enum ThresholdPosition
{
    Under = 0,
    At = 1,
    Over = 2,
}

/// <summary>
/// A team's payroll measured against one threshold, carrying the machine-readable rule code and the
/// human explanation together. A GM who is told only "you are capped" has not been told anything —
/// the explanation is the product, the number is the evidence.
/// </summary>
public sealed record ThresholdStanding
{
    public ThresholdStanding(
        CapThresholdKind kind,
        Money amount,
        long signedDistanceSmallestUnits,
        ThresholdPosition position,
        string ruleCode,
        string explanation)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);

        Kind = kind;
        Amount = amount;
        SignedDistanceSmallestUnits = signedDistanceSmallestUnits;
        Position = position;
        RuleCode = ruleCode;
        Explanation = explanation;
    }

    public CapThresholdKind Kind { get; }

    public Money Amount { get; }

    /// <summary>
    /// Threshold minus payroll: positive is room left, negative is the amount over. Signed on
    /// purpose — an unlabelled absolute value reads as room even when the team is over the line.
    /// </summary>
    public long SignedDistanceSmallestUnits { get; }

    public ThresholdPosition Position { get; }

    public string RuleCode { get; }

    public string Explanation { get; }

    /// <summary>Literally over the amount. For a floor that is compliance, not a problem — see <see cref="IsBreached"/>.</summary>
    public bool IsOver => Position == ThresholdPosition.Over;

    /// <summary>Whether this threshold is a minimum rather than a maximum.</summary>
    public bool IsFloor => Kind == CapThresholdKind.PayrollFloor;

    /// <summary>
    /// Whether the team is on the wrong side of this line. A ceiling is breached by being over it;
    /// the payroll floor is breached by being under it. Screens key off this rather than off
    /// <see cref="IsOver"/>, because a team comfortably above the floor is complying with it.
    /// </summary>
    public bool IsBreached => IsFloor
        ? Position == ThresholdPosition.Under
        : Position == ThresholdPosition.Over;
}
