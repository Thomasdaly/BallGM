using System.Globalization;
using BallGM.Domain.Players;

namespace BallGM.Domain.Common;

/// <summary>
/// Formatting for the sentences rule findings and ledger entries carry to a GM. Explanations are
/// read by a person, so money is in millions, counts take real plurals, and positions are words.
/// Rule codes stay the machine-readable half; this is only the human half.
/// </summary>
public static class ExplanationText
{
    /// <summary>"$2.54M", "$10.0M", "$0.04M", "-$3.1M": one decimal always, a second only when it says something.</summary>
    public static string Money(long smallestUnits) =>
        string.Create(CultureInfo.InvariantCulture, $"{(smallestUnits < 0 ? "-" : string.Empty)}${Math.Abs(smallestUnits / 1_000_000d):0.0#}M");

    /// <inheritdoc cref="Money(long)" />
    public static string Money(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return Money(money.SmallestUnits);
    }

    /// <summary>"1 season", "3 seasons"; pass <paramref name="plural"/> where adding "s" is wrong.</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural ?? singular + "s")}");

    /// <summary>"1st", "2nd", "3rd", "11th".</summary>
    public static string Ordinal(int value)
    {
        var suffix = (value % 100) is 11 or 12 or 13
            ? "th"
            : (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

        return string.Create(CultureInfo.InvariantCulture, $"{value}{suffix}");
    }

    /// <summary>"point guard", "centre".</summary>
    public static string Position(Position position) => position switch
    {
        Players.Position.PointGuard => "point guard",
        Players.Position.ShootingGuard => "shooting guard",
        Players.Position.SmallForward => "small forward",
        Players.Position.PowerForward => "power forward",
        Players.Position.Center => "centre",
        _ => position.ToString(),
    };
}
