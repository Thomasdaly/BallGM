using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using BallGM.Client.Avalonia.Theming;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// The intentional, vector fallbacks of spec 05 section 6, drawn on a 100 x 100 grid and themed by the
/// entity's team: a head-and-shoulders silhouette on the team gradient (jersey number from L up), a
/// monogram plate (abbreviation on the raw primary with a secondary stripe), and a league text badge.
/// Cached per entity look; text is turned into geometry so the result is pure vector.
/// </summary>
public static class FallbackArt
{
    private const double Grid = 100;
    private static readonly Color Neutral = Color.Parse("#222D3E");
    private static readonly Color Silhouette = Color.Parse("#A9B4C4");
    private static readonly ConcurrentDictionary<string, IImage> Cache = new(StringComparer.Ordinal);
    private const string SilhouettePath =
        "M50 14C61 14 69 23 69 35C69 47 61 57 50 57C39 57 31 47 31 35C31 23 39 14 50 14ZM14 100C14 78 29 64 50 64C71 64 86 78 86 100Z";

    public static IImage For(AssetEntry? entry, AssetKey key, AssetSlot slot)
    {
        var label = entry?.Label ?? string.Empty;
        var primary = Parse(entry?.PrimaryColour) ?? Neutral;
        var secondary = Parse(entry?.SecondaryColour) ?? Wcag.Lighten(primary, 0.2);
        var showNumber = slot.Kind == AssetSlotKind.Headshot && slot.SizeDips >= 96 && !string.IsNullOrEmpty(entry?.Number);
        var cacheKey = $"{key.Kind}|{slot.Kind}|{label}|{primary}|{secondary}|{(showNumber ? entry!.Number : string.Empty)}";
        return Cache.GetOrAdd(cacheKey, _ => slot.Kind switch
        {
            AssetSlotKind.Headshot => PlayerSilhouette(primary, secondary, showNumber ? entry!.Number : null),
            AssetSlotKind.LeagueLogo => LeagueBadge(label),
            _ => Monogram(label, primary, secondary),
        });
    }

    private static DrawingImage PlayerSilhouette(Color primary, Color secondary, string? number)
    {
        var group = new DrawingGroup();
        var backdrop = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Wcag.Lighten(primary, 0.06), 0),
                new GradientStop(Wcag.Over(primary, 0.55, Color.Parse("#0A0E14")), 1),
            },
        };
        group.Children.Add(new GeometryDrawing { Brush = backdrop, Geometry = new RectangleGeometry(new Rect(0, 0, Grid, Grid)) });
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(secondary, 0.35), Geometry = Stripe(62, 100, 14) });
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Silhouette, 0.72), Geometry = StreamGeometry.Parse(SilhouettePath) });
        if (number is not null)
        {
            var ink = Wcag.InkOn(Wcag.Over(Silhouette, 0.72, primary));
            group.Children.Add(TextDrawing(number, 18, new SolidColorBrush(ink, 0.85), centreX: 50, baselineY: 92));
        }

        return new DrawingImage(group);
    }

    private static DrawingImage Monogram(string label, Color primary, Color secondary)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(primary), Geometry = new RectangleGeometry(new Rect(0, 0, Grid, Grid)) });
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(secondary), Geometry = Stripe(70, 100, 12) });
        var text = string.IsNullOrWhiteSpace(label) ? "?" : label.ToUpperInvariant();
        var size = text.Length <= 2 ? 48 : text.Length == 3 ? 40 : 30;
        group.Children.Add(TextDrawing(text, size, new SolidColorBrush(Wcag.InkOn(primary)), centreX: 50, baselineY: 50 + (size * 0.36)));
        return new DrawingImage(group);
    }

    private static DrawingImage LeagueBadge(string label)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.Parse("#1A2331")), Geometry = new RectangleGeometry(new Rect(0, 0, Grid, Grid)) });
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(TeamTheme.AccentFallback), Geometry = new RectangleGeometry(new Rect(0, 0, 6, Grid)) });
        var text = string.IsNullOrWhiteSpace(label) ? "LG" : label.ToUpperInvariant();
        group.Children.Add(TextDrawing(text, text.Length <= 3 ? 36 : 24, new SolidColorBrush(Color.Parse("#F2F5F9")), centreX: 53, baselineY: 62));
        return new DrawingImage(group);
    }

    /// <summary>A diagonal band from the lower-left across, leaning like the interface's 12-degree shapes.</summary>
    private static Geometry Stripe(double fromY, double toY, double thickness)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, toY), isFilled: true);
            context.LineTo(new Point(0, toY - thickness));
            context.LineTo(new Point(Grid, fromY - thickness - 30));
            context.LineTo(new Point(Grid, fromY - 30));
            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static GeometryDrawing TextDrawing(string text, double size, IBrush brush, double centreX, double baselineY)
    {
        var typeface = new Typeface(new FontFamily("fonts:BallGM#Barlow Condensed"), FontStyle.Normal, FontWeight.ExtraBold);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        var origin = new Point(centreX - (formatted.Width / 2), baselineY - formatted.Baseline);
        return new GeometryDrawing { Brush = brush, Geometry = formatted.BuildGeometry(origin) ?? new RectangleGeometry() };
    }

    private static Color? Parse(string? hex) => hex is not null && Color.TryParse(hex, out var colour) ? Color.FromRgb(colour.R, colour.G, colour.B) : null;
}
