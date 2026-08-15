using System.Globalization;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// Formats money for display. Presentation only: the Application read model carries smallest
/// units as <see cref="long"/> and never a pre-formatted string, so cap figures stay exact
/// everywhere except the pixel they are drawn on.
/// </summary>
internal static class MoneyDisplay
{
    public static string ToMillions(long smallestUnits)
    {
        var millions = smallestUnits / 1_000_000d;
        return string.Create(CultureInfo.InvariantCulture, $"${millions:0.0}M");
    }
}
