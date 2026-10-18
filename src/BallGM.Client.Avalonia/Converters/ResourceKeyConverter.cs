using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>Looks a resource up by its key string - an icon geometry named by a view model, say.</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public static readonly ResourceKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && global::Avalonia.Application.Current is { } application && application.TryFindResource(key, out var resource)
            ? resource
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
