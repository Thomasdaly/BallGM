using System.Globalization;

namespace BallGM.Client.Avalonia.Formatting;

/// <summary>Units for heights and weights (Settings, "Units and formats").</summary>
public enum UnitSystem
{
    Imperial,
    Metric,
}

/// <summary>
/// Every number the interface prints, in the house formats of spec 01 section 13: $24.5M, $850K,
/// 6'7", 215 lb, 47.3%, 24–11 (en dash), W4 / L2, 1st. Invariant culture throughout, so figures never
/// change shape with the machine's locale. Presentation only.
/// </summary>
public static class Fmt
{
    public const string EnDash = "–";
    public const string Dash = "—";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Set from UI settings; read by every height and weight.</summary>
    public static UnitSystem Units { get; set; } = UnitSystem.Imperial;

    /// <summary>Set from UI settings: a .NET date pattern for in-game dates.</summary>
    public static string DatePattern { get; set; } = "ddd d MMM";

    /// <summary>Money from smallest units: $24.5M from a million up, $850K below, $0 at zero; minus before the sign.</summary>
    public static string Money(long smallestUnits)
    {
        var sign = smallestUnits < 0 ? "-" : string.Empty;
        var amount = Math.Abs(smallestUnits);
        return amount switch
        {
            >= 1_000_000 => string.Create(Invariant, $"{sign}${amount / 1_000_000d:0.0}M"),
            >= 1_000 => string.Create(Invariant, $"{sign}${amount / 1_000d:0}K"),
            _ => string.Create(Invariant, $"{sign}${amount}"),
        };
    }

    /// <summary>A signed money delta: +$1.2M, -$850K.</summary>
    public static string MoneyDelta(long smallestUnits) => (smallestUnits > 0 ? "+" : string.Empty) + Money(smallestUnits);

    public static string Height(int centimetres)
    {
        if (Units == UnitSystem.Metric)
        {
            return string.Create(Invariant, $"{centimetres} cm");
        }

        var inches = (int)Math.Round(centimetres / 2.54);
        return string.Create(Invariant, $"{inches / 12}'{inches % 12}\"");
    }

    public static string Weight(int kilograms) => Units == UnitSystem.Metric
        ? string.Create(Invariant, $"{kilograms} kg")
        : string.Create(Invariant, $"{(int)Math.Round(kilograms * 2.20462)} lb");

    /// <summary>A percentage from a 0-1 share, one decimal: 47.3%.</summary>
    public static string Percent(double share) => string.Create(Invariant, $"{share * 100:0.0}%");

    /// <summary>A win-loss record with an en dash: 24–11.</summary>
    public static string Record(int wins, int losses) => string.Create(Invariant, $"{wins}{EnDash}{losses}");

    /// <summary>Winning percentage in the sport's style: .686.</summary>
    public static string WinPercentage(int wins, int losses)
    {
        if (wins + losses == 0)
        {
            return ".000";
        }

        var text = ((double)wins / (wins + losses)).ToString("0.000", Invariant);
        return text.StartsWith('0') ? text[1..] : text;
    }

    /// <summary>A streak: W4 or L2; empty when there is none.</summary>
    public static string Streak(bool won, int length) => length <= 0 ? string.Empty : string.Create(Invariant, $"{(won ? 'W' : 'L')}{length}");

    public static string Ordinal(int number)
    {
        var suffix = (number % 100) is >= 11 and <= 13 ? "th" : (number % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return string.Create(Invariant, $"{number}{suffix}");
    }

    /// <summary>A one-decimal figure, as stat tables print it.</summary>
    public static string OneDecimal(double value) => value.ToString("0.0", Invariant);

    /// <summary>A signed one-decimal figure: +4.8, -1.2, 0.0.</summary>
    public static string SignedOneDecimal(double value) => (value > 0 ? "+" : string.Empty) + value.ToString("0.0", Invariant);

    public static string Date(DateOnly date) => date.ToString(DatePattern, Invariant);

    /// <summary>A count with its noun, never a parenthetical plural: 1 game, 3 games.</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        string.Create(Invariant, $"{count} {(count == 1 ? singular : plural ?? singular + "s")}");
}
