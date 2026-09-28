using BallGM.Domain.Cap;
using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// One band of a tiered salary-matching rule: a team sending out up to <see cref="OutgoingUpTo"/>
/// (the last band has no upper bound) may take back <see cref="Percent"/>% of it plus
/// <see cref="Allowance"/>.
/// </summary>
public sealed record SalaryMatchingBand(Money? OutgoingUpTo, int Percent, Money Allowance);

/// <summary>
/// The cap mechanics that sit on top of the thresholds: what being over the tax line costs, how
/// salary matching tightens as a team climbs past each apron, and the smaller over-cap allowance a
/// team keeps once it has lost the standard one. Named for what each does, per
/// <c>docs/negotiation-mechanisms.md</c> → Naming rule; a data pack supplies any real league's values.
/// <para>
/// Every part is optional by absence, like every other rule in the file: a league with no tax bill
/// states no brackets, a league with flat matching states no bands, and <see cref="None"/> is a
/// league configuring none of it — in which case every behaviour falls back to exactly what the
/// ruleset did before this section existed.
/// </para>
/// </summary>
public sealed record CapMechanics
{
    private const string InvalidTaxScheduleCode = "ruleset.invalid_tax_schedule";
    private const string InvalidMatchingBandsCode = "ruleset.invalid_salary_matching_bands";
    private const string InvalidApronMatchCode = "ruleset.invalid_above_first_apron_match_percent";
    private const string InvalidReducedAllowanceCode = "ruleset.invalid_reduced_over_cap_allowance";

    private CapMechanics(
        Money? taxBracketSize,
        IReadOnlyList<int> taxRatesPercent,
        IReadOnlyList<int> repeaterTaxRatesPercent,
        int taxRateIncrementPercent,
        IReadOnlyList<SalaryMatchingBand> salaryMatchingBands,
        int? aboveFirstApronMatchPercent,
        bool secondApronBlocksAggregation,
        Money? reducedOverCapAllowance,
        CapThresholdKind? reducedOverCapAllowanceUnavailableAbove)
    {
        TaxBracketSize = taxBracketSize;
        TaxRatesPercent = taxRatesPercent;
        RepeaterTaxRatesPercent = repeaterTaxRatesPercent;
        TaxRateIncrementPercent = taxRateIncrementPercent;
        SalaryMatchingBands = salaryMatchingBands;
        AboveFirstApronMatchPercent = aboveFirstApronMatchPercent;
        SecondApronBlocksAggregation = secondApronBlocksAggregation;
        ReducedOverCapAllowance = reducedOverCapAllowance;
        ReducedOverCapAllowanceUnavailableAbove = reducedOverCapAllowanceUnavailableAbove;
    }

    public static CapMechanics None { get; } = new(null, [], [], 0, [], null, false, null, null);

    public bool IsConfigured => HasTaxBill || HasTieredMatching || AboveFirstApronMatchPercent is not null
        || SecondApronBlocksAggregation || HasReducedOverCapAllowance;

    /// <summary>Width of each tax bracket above the tax line.</summary>
    public Money? TaxBracketSize { get; }

    /// <summary>Rate per bracket, in percent of the salary in it (150 = $1.50 tax per $1 over).</summary>
    public IReadOnlyList<int> TaxRatesPercent { get; }

    /// <summary>The same schedule for a team that has paid tax often enough to be charged more; empty uses <see cref="TaxRatesPercent"/>.</summary>
    public IReadOnlyList<int> RepeaterTaxRatesPercent { get; }

    /// <summary>How much the rate rises for each bracket past the last one the schedule lists.</summary>
    public int TaxRateIncrementPercent { get; }

    public bool HasTaxBill => TaxBracketSize is not null && TaxRatesPercent.Count > 0;

    /// <summary>Tiered matching, by outgoing salary, replacing the flat percentage for teams under the first apron.</summary>
    public IReadOnlyList<SalaryMatchingBand> SalaryMatchingBands { get; }

    public bool HasTieredMatching => SalaryMatchingBands.Count > 0;

    /// <summary>The share of outgoing salary a team finishing above the first apron may take back, with no allowance.</summary>
    public int? AboveFirstApronMatchPercent { get; }

    /// <summary>Whether a team finishing above the second apron may combine several outgoing salaries to take back one.</summary>
    public bool SecondApronBlocksAggregation { get; }

    /// <summary>A smaller over-cap allowance that survives past the standard allowance's cut-off, up to its own.</summary>
    public Money? ReducedOverCapAllowance { get; }

    public CapThresholdKind? ReducedOverCapAllowanceUnavailableAbove { get; }

    public bool HasReducedOverCapAllowance => ReducedOverCapAllowance is not null;

    public static DomainOperationResult<CapMechanics> Create(
        Money? taxBracketSize,
        IReadOnlyList<int>? taxRatesPercent,
        IReadOnlyList<int>? repeaterTaxRatesPercent,
        int? taxRateIncrementPercent,
        IReadOnlyList<SalaryMatchingBand>? salaryMatchingBands,
        int? aboveFirstApronMatchPercent,
        bool secondApronBlocksAggregation,
        Money? reducedOverCapAllowance,
        CapThresholdKind? reducedOverCapAllowanceUnavailableAbove)
    {
        var rates = taxRatesPercent ?? [];
        var repeaterRates = repeaterTaxRatesPercent ?? [];
        var bands = salaryMatchingBands ?? [];
        var errors = new List<DomainError>();

        if ((taxBracketSize is null) != (rates.Count == 0))
        {
            errors.Add(new DomainError(InvalidTaxScheduleCode, "A tax bill needs both a bracket size and at least one rate; state both or neither."));
        }

        if (taxBracketSize is { SmallestUnits: <= 0 })
        {
            errors.Add(new DomainError(InvalidTaxScheduleCode, "The tax bracket size must be positive."));
        }

        if (rates.Concat(repeaterRates).Any(rate => rate < 0) || taxRateIncrementPercent is < 0)
        {
            errors.Add(new DomainError(InvalidTaxScheduleCode, "Tax rates and the per-bracket increment cannot be negative."));
        }

        if (repeaterRates.Count > 0 && rates.Count == 0)
        {
            errors.Add(new DomainError(InvalidTaxScheduleCode, "A repeater schedule needs the standard schedule it replaces."));
        }

        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var isLast = index == bands.Count - 1;
            if (band.Percent < 100 || band.Allowance.SmallestUnits < 0)
            {
                errors.Add(new DomainError(InvalidMatchingBandsCode, $"Salary-matching band {index + 1} must allow at least 100% of outgoing salary and a non-negative allowance."));
            }

            if (isLast != (band.OutgoingUpTo is null))
            {
                errors.Add(new DomainError(InvalidMatchingBandsCode, "Every salary-matching band but the last states an upper bound, and the last states none."));
            }

            if (index > 0 && band.OutgoingUpTo is { } upper && bands[index - 1].OutgoingUpTo is { } previous && upper <= previous)
            {
                errors.Add(new DomainError(InvalidMatchingBandsCode, "Salary-matching bands must be listed in ascending order of their upper bound."));
            }
        }

        if (aboveFirstApronMatchPercent is < 100)
        {
            errors.Add(new DomainError(InvalidApronMatchCode, "Above the first apron a team must still be allowed to take back at least 100% of what it sends."));
        }

        if (reducedOverCapAllowance is { SmallestUnits: <= 0 })
        {
            errors.Add(new DomainError(InvalidReducedAllowanceCode, "The reduced over-cap allowance must be positive."));
        }

        if (reducedOverCapAllowance is null && reducedOverCapAllowanceUnavailableAbove is not null)
        {
            errors.Add(new DomainError(InvalidReducedAllowanceCode, "A cut-off for the reduced over-cap allowance needs the allowance itself."));
        }

        return errors.Count > 0
            ? DomainOperationResult<CapMechanics>.Failure(errors.ToArray())
            : DomainOperationResult<CapMechanics>.Success(new CapMechanics(
                taxBracketSize, rates, repeaterRates, taxRateIncrementPercent ?? 0, bands,
                aboveFirstApronMatchPercent, secondApronBlocksAggregation, reducedOverCapAllowance, reducedOverCapAllowanceUnavailableAbove));
    }

    /// <summary>
    /// The most a team over the cap may take back for <paramref name="outgoing"/> under the tiered
    /// bands, or <c>null</c> when this league states none (the flat percentage applies instead).
    /// </summary>
    public Money? TieredMatchingLimit(Money outgoing)
    {
        if (!HasTieredMatching)
        {
            return null;
        }

        var band = SalaryMatchingBands.First(candidate => candidate.OutgoingUpTo is null || outgoing <= candidate.OutgoingUpTo);
        return new Money((outgoing.SmallestUnits * band.Percent / 100) + band.Allowance.SmallestUnits);
    }
}
