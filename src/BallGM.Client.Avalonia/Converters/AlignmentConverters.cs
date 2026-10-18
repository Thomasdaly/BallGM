using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>Numbers align right, text aligns left (spec 01 section 5).</summary>
public sealed class RightAlignConverter : IValueConverter
{
    public static readonly RightAlignConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var right = value is true;
        if (targetType == typeof(TextAlignment))
        {
            return right ? TextAlignment.Right : TextAlignment.Left;
        }

        return right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
