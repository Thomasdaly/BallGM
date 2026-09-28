using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>
/// Loads a pack-supplied image (logo or portrait) from a file path the data source has already
/// validated. Decoded once per path and cached, since the same crest and faces are drawn on every
/// screen. A path that cannot be decoded yields no image, and the view falls back to initials.
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public static readonly ImagePathConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path && path.Length > 0 ? Cache.GetOrAdd(path, Load) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Bitmap? Load(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
