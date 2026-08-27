using BallGM.Domain.Cap;
using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// Configurable financial thresholds for one league ruleset. Named generically rather than
/// after any one real-world league's current agreement, so a rule change is a new ruleset
/// file, not a code change: <see cref="PayrollFloor"/> is a minimum total payroll a team must
/// reach, <see cref="SoftCap"/> is a spending target a team can exceed using exceptions,
/// <see cref="LuxuryTax"/> triggers a tax bill above it, <see cref="FirstApron"/> and
/// <see cref="SecondApron"/> are successively stricter transaction-restriction lines, and
/// <see cref="HardCap"/> is a ceiling a team cannot exceed at all. Enforcing what each threshold
/// actually restricts during a trade or signing is the trade engine's job — this type only carries
/// the configured amounts and guarantees they're internally consistent.
/// <para>
/// Every threshold is optional, and absence means the league does not have that line — not that the
/// line is set to zero. A cap system of zero and no cap system at all are different leagues, and
/// telling a GM in an uncapped league that they are "over the soft cap by their entire payroll" is
/// the silent-wrong-rule failure this whole type exists to refuse. A league with none of them is
/// <see cref="Uncapped"/>.
/// </para>
/// </summary>
public sealed record CapThresholds
{
    private const string DescendingThresholdsCode = "ruleset.cap_thresholds_out_of_order";

    private CapThresholds(
        Money? payrollFloor,
        Money? softCap,
        Money? luxuryTax,
        Money? firstApron,
        Money? secondApron,
        Money? hardCap)
    {
        PayrollFloor = payrollFloor;
        SoftCap = softCap;
        LuxuryTax = luxuryTax;
        FirstApron = firstApron;
        SecondApron = secondApron;
        HardCap = hardCap;
    }

    /// <summary>
    /// A league with no cap system of any kind. Payrolls are real and roster limits still apply;
    /// there is simply no line to measure them against.
    /// </summary>
    public static CapThresholds Uncapped { get; } = new(null, null, null, null, null, null);

    /// <summary>
    /// The configured thresholds in ascending order of amount, each paired with the kind it is.
    /// This fixed sequence is the single definition of the ordering: the consistency check below and
    /// the cap sheet's row order both read it, so the two cannot disagree.
    /// </summary>
    public IReadOnlyList<(CapThresholdKind Kind, Money Amount)> Configured =>
        new[]
        {
            (CapThresholdKind.PayrollFloor, PayrollFloor),
            (CapThresholdKind.SoftCap, SoftCap),
            (CapThresholdKind.LuxuryTax, LuxuryTax),
            (CapThresholdKind.FirstApron, FirstApron),
            (CapThresholdKind.SecondApron, SecondApron),
            (CapThresholdKind.HardCap, HardCap),
        }
        .Where(entry => entry.Item2 is not null)
        .Select(entry => (entry.Item1, entry.Item2!))
        .ToList();

    public bool IsUncapped => Configured.Count == 0;

    /// <summary>
    /// Builds a threshold set. The non-decreasing consistency check applies to the thresholds that
    /// are <em>present</em>, in the fixed sequence above — so a league configuring only a soft cap
    /// and a tax line is checked on those two, and the payroll floor extends the chain downward
    /// rather than being inserted into it.
    /// </summary>
    public static DomainOperationResult<CapThresholds> Create(
        Money? payrollFloor = null,
        Money? softCap = null,
        Money? luxuryTax = null,
        Money? firstApron = null,
        Money? secondApron = null,
        Money? hardCap = null)
    {
        var candidate = new CapThresholds(payrollFloor, softCap, luxuryTax, firstApron, secondApron, hardCap);
        var configured = candidate.Configured;

        for (var index = 1; index < configured.Count; index++)
        {
            if (configured[index - 1].Amount <= configured[index].Amount)
            {
                continue;
            }

            return DomainOperationResult<CapThresholds>.Failure(
                new DomainError(
                    DescendingThresholdsCode,
                    "Cap thresholds must be configured in non-decreasing order: payroll floor, soft cap, luxury tax, first apron, second apron, hard cap. " +
                    $"The configured {Describe(configured[index - 1].Kind)} of {configured[index - 1].Amount.SmallestUnits} is above the configured {Describe(configured[index].Kind)} of {configured[index].Amount.SmallestUnits}."));
        }

        return DomainOperationResult<CapThresholds>.Success(candidate);
    }

    /// <summary>The minimum total payroll a team must reach. Reporting only — the penalty is a later milestone.</summary>
    public Money? PayrollFloor { get; }

    public Money? SoftCap { get; }

    public Money? LuxuryTax { get; }

    public Money? FirstApron { get; }

    public Money? SecondApron { get; }

    public Money? HardCap { get; }

    private static string Describe(CapThresholdKind kind) => kind switch
    {
        CapThresholdKind.PayrollFloor => "payroll floor",
        CapThresholdKind.SoftCap => "soft cap",
        CapThresholdKind.LuxuryTax => "luxury tax",
        CapThresholdKind.FirstApron => "first apron",
        CapThresholdKind.SecondApron => "second apron",
        CapThresholdKind.HardCap => "hard cap",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cap threshold."),
    };
}
