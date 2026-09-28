using System.Globalization;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Cap;

/// <summary>
/// Reads what a payroll costs and what it forbids: the luxury-tax bill bracket by bracket, and each
/// configured restriction the team's position triggers, explained. Pure — same payroll, same ruleset,
/// same answer — and silent about any rule the league does not configure, because a restriction a
/// league never stated is not one a GM should be told about.
/// </summary>
public static class CapStatusEvaluator
{
    public const string OverSoftCapCode = "cap_status.over_soft_cap";
    public const string OverTaxLineCode = "cap_status.over_tax_line";
    public const string StandardAllowanceLostCode = "cap_status.standard_allowance_unavailable";
    public const string ReducedAllowanceOnlyCode = "cap_status.reduced_allowance_only";
    public const string NoAllowanceCode = "cap_status.no_over_cap_allowance";
    public const string ApronMatchingCode = "cap_status.apron_salary_matching";
    public const string NoAggregationCode = "cap_status.no_salary_aggregation";
    public const string NoSalaryIncreaseCode = "cap_status.no_trade_salary_increase";
    public const string HardCapCode = "cap_status.at_hard_cap";

    public static CapStatus Evaluate(TeamId teamId, Money payroll, bool isRepeater, LeagueRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(payroll);
        ArgumentNullException.ThrowIfNull(ruleset);

        var thresholds = ruleset.CapThresholds;
        var mechanics = ruleset.CapMechanics;
        var negotiation = ruleset.NegotiationRules;
        var restrictions = new List<RuleFinding>();

        bool Above(Money? line) => line is not null && payroll > line;
        Money? Line(CapThresholdKind? kind) => kind switch
        {
            CapThresholdKind.SoftCap => thresholds.SoftCap,
            CapThresholdKind.LuxuryTax => thresholds.LuxuryTax,
            CapThresholdKind.FirstApron => thresholds.FirstApron,
            CapThresholdKind.SecondApron => thresholds.SecondApron,
            CapThresholdKind.HardCap => thresholds.HardCap,
            _ => null,
        };

        if (Above(thresholds.SoftCap))
        {
            restrictions.Add(new RuleFinding(OverSoftCapCode,
                $"Over the soft cap by {Show(new Money(payroll.SmallestUnits - thresholds.SoftCap!.SmallestUnits))}: a free agent can only be signed with an over-cap allowance or at the minimum salary.", teamId));
        }

        var taxBill = mechanics.HasTaxBill && thresholds.LuxuryTax is { } taxLine
            ? Bill(payroll, taxLine, mechanics, isRepeater)
            : null;
        if (taxBill is { TaxOwed.SmallestUnits: > 0 })
        {
            restrictions.Add(new RuleFinding(OverTaxLineCode,
                $"Over the luxury tax line by {Show(taxBill.AmountOverTaxLine)}: {Show(taxBill.TaxOwed)} owed at {(isRepeater ? "the repeater" : "the standard")} rates if the payroll ends the season here.", teamId));
        }

        var standardCutoff = Line(negotiation.StandardOverCapAllowanceUnavailableAbove);
        var reducedCutoff = Line(mechanics.ReducedOverCapAllowanceUnavailableAbove);
        if (negotiation.StandardOverCapAllowance is not null && Above(standardCutoff))
        {
            if (mechanics.HasReducedOverCapAllowance && !Above(reducedCutoff))
            {
                restrictions.Add(new RuleFinding(ReducedAllowanceOnlyCode,
                    $"Above the {Name(negotiation.StandardOverCapAllowanceUnavailableAbove)}: the standard over-cap allowance is gone; only the reduced allowance of {Show(mechanics.ReducedOverCapAllowance!)} remains.", teamId));
            }
            else
            {
                restrictions.Add(new RuleFinding(mechanics.HasReducedOverCapAllowance ? NoAllowanceCode : StandardAllowanceLostCode,
                    mechanics.HasReducedOverCapAllowance
                        ? $"Above the {Name(mechanics.ReducedOverCapAllowanceUnavailableAbove)}: no over-cap allowance at all — new players only at the minimum salary."
                        : $"Above the {Name(negotiation.StandardOverCapAllowanceUnavailableAbove)}: the standard over-cap allowance is unavailable.", teamId));
            }
        }

        if (mechanics.AboveFirstApronMatchPercent is { } apronPercent && Above(thresholds.FirstApron))
        {
            restrictions.Add(new RuleFinding(ApronMatchingCode,
                $"Above the first apron: a trade may take back at most {apronPercent}% of the salary sent out, with no extra allowance.", teamId));
        }

        if (Above(thresholds.SecondApron))
        {
            if (mechanics.SecondApronBlocksAggregation)
            {
                restrictions.Add(new RuleFinding(NoAggregationCode,
                    "Above the second apron: salaries cannot be combined in a trade — each incoming player must be matched by one outgoing salary.", teamId));
            }

            if (ruleset.TradeRules.SecondApronBlocksSalaryIncrease)
            {
                restrictions.Add(new RuleFinding(NoSalaryIncreaseCode,
                    "Above the second apron: a trade cannot add salary — the team must send out at least as much as it takes back.", teamId));
            }
        }

        if (thresholds.HardCap is { } hardCap && payroll >= hardCap)
        {
            restrictions.Add(new RuleFinding(HardCapCode, $"At the hard cap of {Show(hardCap)}: no move may add salary.", teamId));
        }

        return new CapStatus(taxBill, restrictions);
    }

    /// <summary>
    /// The tax on a payroll: each bracket-wide slice above the line at its own rate, and every slice
    /// past the listed schedule at the last listed rate plus the increment per extra bracket. Integer
    /// money throughout, rounded down per bracket, so a bill never depends on float rounding.
    /// </summary>
    public static LuxuryTaxBill Bill(Money payroll, Money taxLine, CapMechanics mechanics, bool isRepeater)
    {
        ArgumentNullException.ThrowIfNull(mechanics);
        if (!mechanics.HasTaxBill)
        {
            throw new InvalidOperationException("This league configures no tax schedule.");
        }

        var rates = isRepeater && mechanics.RepeaterTaxRatesPercent.Count > 0 ? mechanics.RepeaterTaxRatesPercent : mechanics.TaxRatesPercent;
        var over = Math.Max(0, payroll.SmallestUnits - taxLine.SmallestUnits);
        var size = mechanics.TaxBracketSize!.SmallestUnits;
        var brackets = new List<LuxuryTaxBracketCharge>();
        long remaining = over, total = 0;

        for (var bracket = 0; remaining > 0; bracket++)
        {
            var slice = Math.Min(remaining, size);
            var rate = bracket < rates.Count
                ? rates[bracket]
                : rates[^1] + ((bracket - rates.Count + 1) * mechanics.TaxRateIncrementPercent);
            var tax = slice * rate / 100;
            brackets.Add(new LuxuryTaxBracketCharge(bracket + 1, new Money(slice), rate, new Money(tax)));
            total += tax;
            remaining -= slice;
        }

        return new LuxuryTaxBill(taxLine, new Money(over), new Money(total), isRepeater, brackets);
    }

    private static string Name(CapThresholdKind? kind) => kind switch
    {
        CapThresholdKind.SoftCap => "soft cap",
        CapThresholdKind.LuxuryTax => "luxury tax line",
        CapThresholdKind.FirstApron => "first apron",
        CapThresholdKind.SecondApron => "second apron",
        CapThresholdKind.HardCap => "hard cap",
        _ => "configured line",
    };

    private static string Show(Money money) =>
        string.Create(CultureInfo.InvariantCulture, $"${money.SmallestUnits / 1_000_000d:0.00}M");
}
