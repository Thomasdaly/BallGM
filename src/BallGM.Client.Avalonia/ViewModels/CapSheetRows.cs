using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One threshold row on the cap sheet. <see cref="Distance"/> says "over" or "under" in words
/// rather than by sign character: an unlabelled figure reads as room under the cap even when the
/// payroll is over it, which is exactly the number a GM would misread.
/// </summary>
public sealed record ThresholdRow(string Name, string Amount, string Distance, string Explanation, bool IsOver)
{
    public static ThresholdRow From(ThresholdStandingSummary standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new ThresholdRow(
            standing.ThresholdName,
            MoneyDisplay.ToMillions(standing.ThresholdAmount),
            standing.SignedDistance switch
            {
                > 0 => $"{MoneyDisplay.ToMillions(standing.SignedDistance)} under",
                < 0 => $"{MoneyDisplay.ToMillions(Math.Abs(standing.SignedDistance))} over",
                _ => "exactly at the line",
            },
            standing.Explanation,
            standing.IsOver);
    }
}

/// <summary>One line of what the payroll is made of: a live contract, or money owed to someone who is gone.</summary>
public sealed record ChargeRow(string PlayerName, string Kind, string Amount, bool IsDeadMoney)
{
    public static ChargeRow From(CapChargeLine charge)
    {
        ArgumentNullException.ThrowIfNull(charge);
        return new ChargeRow(charge.PlayerName, charge.Kind, MoneyDisplay.ToMillions(charge.Amount), charge.IsDeadMoney);
    }
}

/// <summary>One entry from the transaction ledger — the audit trail behind the figures above it.</summary>
public sealed record LedgerRow(string RecordedAt, string Kind, string Amount, string Reason)
{
    public static LedgerRow From(TransactionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new LedgerRow(
            line.RecordedAt,
            line.Kind,
            line.Amount is null ? string.Empty : MoneyDisplay.ToMillions(line.Amount.Value),
            line.Reason);
    }
}
