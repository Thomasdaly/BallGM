using BallGM.Domain.Common;

namespace BallGM.Domain.Cap;

/// <summary>
/// The configured financial boundaries, named generically rather than after any one real-world
/// league's current agreement. Mirrors <c>BallGM.Rules.Configuration.CapThresholds</c>, which owns
/// the amounts; this enum exists so a cap-sheet result can name which line it is talking about
/// without Domain depending on the Rules project.
/// </summary>
public enum CapThresholdKind
{
    SoftCap = 0,
    LuxuryTax = 1,
    FirstApron = 2,
    SecondApron = 3,
    HardCap = 4,
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

    public bool IsOver => Position == ThresholdPosition.Over;
}
