using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>
/// Colours an Overall rating by tier for its badge. Presentation only: the bands decide a colour,
/// never anything a rule reads, so they live here rather than in <c>BallGM.Rules</c>.
/// </summary>
public sealed class OverallTierConverter : IValueConverter
{
    public static readonly OverallTierConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is int overall ? TierKey(overall) : "TierFringeBrush";
        return global::Avalonia.Application.Current?.FindResource(key) as IBrush ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>The tier's brush key; tiers per docs/ui/spec/01_DESIGN_LANGUAGE.md section 12 (90/80/70/60/50).</summary>
    internal static string TierKey(int overall) => Theming.RatingTiers.For(overall).BrushKey;
}
