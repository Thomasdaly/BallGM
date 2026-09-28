using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>Upper-cases display text for the broadcast-style headings (Avalonia has no text-transform).</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public static readonly UpperCaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a team name into the initials on its crest: one letter per word for three or more words
/// ("Golden State Warriors" → GSW), otherwise the first three letters of the first word
/// ("Chicago Bulls" → CHI). A crest drawn from the name, so no pack has to ship artwork.
/// </summary>
public sealed class TeamInitialsConverter : IValueConverter
{
    public static readonly TeamInitialsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? Initials(name) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        var initials = words.Length >= 3
            ? string.Concat(words.Take(3).Select(word => word[0]))
            : words[0][..Math.Min(3, words[0].Length)];
        return initials.ToUpperInvariant();
    }
}

/// <summary>Maps a navigation section's title ("Cap sheet") to its icon resource ("Icon.CapSheet").</summary>
public sealed class SectionIconConverter : IValueConverter
{
    public static readonly SectionIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is string title ? IconKey(title) : "Icon.Default";
        var app = global::Avalonia.Application.Current;
        return app?.FindResource(key) as Geometry ?? app?.FindResource("Icon.Default") as Geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static string IconKey(string title) =>
        "Icon." + string.Concat(title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
