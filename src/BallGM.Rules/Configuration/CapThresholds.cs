using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// Configurable financial thresholds for one league ruleset. Named generically rather than
/// after any one real-world league's current agreement, so a rule change is a new ruleset
/// file, not a code change: <see cref="SoftCap"/> is a spending target a team can exceed
/// using exceptions, <see cref="LuxuryTax"/> triggers a tax bill above it, <see cref="FirstApron"/>
/// and <see cref="SecondApron"/> are successively stricter transaction-restriction lines, and
/// <see cref="HardCap"/> is a ceiling a team cannot exceed at all. Enforcing what each threshold
/// actually restricts during a trade or signing is the trade engine's job (Milestone 5) — this
/// type only carries the configured amounts and guarantees they're internally consistent.
/// </summary>
public sealed record CapThresholds
{
    private const string DescendingThresholdsCode = "ruleset.cap_thresholds_out_of_order";

    private CapThresholds(Money softCap, Money luxuryTax, Money firstApron, Money secondApron, Money hardCap)
    {
        SoftCap = softCap;
        LuxuryTax = luxuryTax;
        FirstApron = firstApron;
        SecondApron = secondApron;
        HardCap = hardCap;
    }

    public static DomainOperationResult<CapThresholds> Create(
        Money softCap,
        Money luxuryTax,
        Money firstApron,
        Money secondApron,
        Money hardCap)
    {
        ArgumentNullException.ThrowIfNull(softCap);
        ArgumentNullException.ThrowIfNull(luxuryTax);
        ArgumentNullException.ThrowIfNull(firstApron);
        ArgumentNullException.ThrowIfNull(secondApron);
        ArgumentNullException.ThrowIfNull(hardCap);

        if (softCap > luxuryTax || luxuryTax > firstApron || firstApron > secondApron || secondApron > hardCap)
        {
            return DomainOperationResult<CapThresholds>.Failure(
                new DomainError(
                    DescendingThresholdsCode,
                    "Cap thresholds must be configured in non-decreasing order: soft cap, luxury tax, first apron, second apron, hard cap."));
        }

        return DomainOperationResult<CapThresholds>.Success(
            new CapThresholds(softCap, luxuryTax, firstApron, secondApron, hardCap));
    }

    public Money SoftCap { get; }

    public Money LuxuryTax { get; }

    public Money FirstApron { get; }

    public Money SecondApron { get; }

    public Money HardCap { get; }
}
