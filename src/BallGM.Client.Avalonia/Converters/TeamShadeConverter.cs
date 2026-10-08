using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using BallGM.Client.Avalonia.Theming;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Converters;

/// <summary>
/// Draws a team in its own colour (<see cref="TeamPaint.Shade"/>): <c>wash</c> is a gradient from the
/// top-right corner that fades to nothing, for a cell or card about that team (<c>wash-left</c> from
/// the top-left, for the left-hand side of a pairing); <c>plate</c> is the solid colour, for a
/// badge; <c>ink</c> is the navy or white that reads on the plate. A team with no colour and no
/// logo gets a neutral plate and no wash. Shade marks who a game is against and never its result —
/// results keep the reserved status colours.
/// </summary>
public sealed class TeamShadeConverter : IValueConverter
{
    public static readonly TeamShadeConverter Instance = new();

    private static readonly ConcurrentDictionary<(Color Color, string Kind), IBrush> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = parameter as string ?? "plate";
        var shade = value is TeamArt art ? TeamPaint.Shade(art.Colours, art.LogoPath) : null;

        if (shade is not { } color)
        {
            return kind switch
            {
                "wash" or "wash-left" => Brushes.Transparent,
                "ink" => Resource("TextBrush"),
                _ => Resource("Bg4Brush"),
            };
        }

        return Cache.GetOrAdd((color, kind), key => key.Kind switch
        {
            "wash" or "wash-left" => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(key.Kind == "wash" ? 1 : 0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(key.Kind == "wash" ? 0 : 1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x59, key.Color.R, key.Color.G, key.Color.B), 0),
                    new GradientStop(Color.FromArgb(0x00, key.Color.R, key.Color.G, key.Color.B), 0.75),
                },
            }.ToImmutable(),
            "ink" => new ImmutableSolidColorBrush(TeamPaint.InkOn(key.Color)),
            _ => new ImmutableSolidColorBrush(key.Color),
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush? Resource(string key) =>
        global::Avalonia.Application.Current?.TryFindResource(key, out var resource) == true ? resource as IBrush : null;
}
