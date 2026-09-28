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

    internal static string TierKey(int overall) => overall switch
    {
        >= 90 => "TierEliteBrush",
        >= 80 => "TierStarBrush",
        >= 72 => "TierStarterBrush",
        >= 64 => "TierRotationBrush",
        >= 56 => "TierBenchBrush",
        _ => "TierFringeBrush",
    };
}
