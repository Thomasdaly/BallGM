using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>An integer change as ▲3 / ▼2 (blank when none): direction is never shown by colour alone.</summary>
public sealed class DeltaTextConverter : IValueConverter
{
    public static readonly DeltaTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int delta when delta > 0 => "▲" + delta.ToString(CultureInfo.InvariantCulture),
        int delta when delta < 0 => "▼" + (-delta).ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The positive or negative brush (following the colour-vision setting) for a change.</summary>
public sealed class DeltaBrushConverter : IValueConverter
{
    public static readonly DeltaBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            int delta when delta > 0 => "PositiveBrush",
            int delta when delta < 0 => "NegativeBrush",
            double delta when delta > 0 => "PositiveBrush",
            double delta when delta < 0 => "NegativeBrush",
            _ => "TextMutedBrush",
        };
        return global::Avalonia.Application.Current?.FindResource(key) as IBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
