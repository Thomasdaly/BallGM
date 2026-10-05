using System.Globalization;
using BallGM.Domain.Players;

namespace BallGM.Rules.Negotiations;

/// <summary>
/// Formatting for the sentences the market writes to a GM. Explanations are read by a person, so
/// money is in millions, counts take real plurals, and positions are words, not enum names.
/// </summary>
internal static class MarketText
{
    /// <summary>"$2.54M", "$10.0M", "$0.04M": one decimal always, a second only when it says something.</summary>
    public static string Money(long smallestUnits) =>
        string.Create(CultureInfo.InvariantCulture, $"${smallestUnits / 1_000_000d:0.0#}M");

    /// <summary>"1 season", "3 seasons".</summary>
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

    /// <summary>"PointGuard" → "point guard".</summary>
    public static string Position(Position position) => position switch
    {
        Domain.Players.Position.PointGuard => "point guard",
        Domain.Players.Position.ShootingGuard => "shooting guard",
        Domain.Players.Position.SmallForward => "small forward",
        Domain.Players.Position.PowerForward => "power forward",
        Domain.Players.Position.Center => "centre",
        _ => position.ToString(),
    };
}
