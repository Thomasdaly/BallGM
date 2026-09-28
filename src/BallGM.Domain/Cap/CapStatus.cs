using BallGM.Domain.Common;

namespace BallGM.Domain.Cap;

/// <summary>
/// Where a payroll leaves a team beyond the plain threshold comparison: the luxury-tax bill it would
/// owe, and every configured restriction its position triggers. Produced by the cap ledger
/// (<c>BallGM.Rules.Cap</c>); this type is only the shape the answer travels in.
/// </summary>
public sealed record CapStatus(LuxuryTaxBill? TaxBill, IReadOnlyList<RuleFinding> Restrictions)
{
    public static CapStatus None { get; } = new(null, []);
}

/// <summary>
/// A luxury-tax bill, bracket by bracket, so a GM can see not just the total but which slice of the
/// payroll is costing what. <see cref="Brackets"/> is empty when the payroll is under the tax line.
/// </summary>
public sealed record LuxuryTaxBill(
    Money TaxLine,
    Money AmountOverTaxLine,
    Money TaxOwed,
    bool IsRepeater,
    IReadOnlyList<LuxuryTaxBracketCharge> Brackets);

/// <summary>One slice of the payroll above the tax line and the rate charged on it (percent: 150 = $1.50 per $1).</summary>
public sealed record LuxuryTaxBracketCharge(int Bracket, Money SalaryInBracket, int RatePercent, Money Tax);
