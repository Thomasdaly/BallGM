using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// The client's one accent is the selected team's colour - its "paint". A pack that states team
/// colours (schema version 2) is taken at its word; otherwise the colour is read from the team's logo:
/// the most common strongly coloured hue. Either way it is lifted until it reads on the navy
/// surfaces. Presentation only; nothing a rule reads.
/// <para>
/// Applied by setting <see cref="SolidColorBrush.Color"/> on the existing Accent* brush instances
/// from <c>Tokens.axaml</c>, so every <c>StaticResource</c> user follows without a resource lookup
/// change. A team with no logo, or a monochrome one, gets the theme's fallback paint back.
/// </para>
/// </summary>
internal static class TeamPaint
{
    /// <summary>The floor a paint colour must clear against the darkest surface, so paint text stays legible.</summary>
    internal const double MinimumContrastOnSurface = 4.5;

    private static readonly Color Surface = Color.Parse("#0D1522");
    private static readonly Color DarkInk = Color.Parse("#0D1522");
    private static readonly Color LightInk = Color.Parse("#FFFFFF");

    private static Color? _fallback;
    private static Color? _bannerStart;

    /// <summary>
    /// The paint for a logo's pixels, or null when the logo has no strong colour to take (black,
    /// white, and greys only). Pixels are straight RGBA; transparent ones are ignored.
    /// </summary>
    internal static Color? Derive(ReadOnlySpan<byte> rgba)
    {
        // 24 hue buckets, weighted by saturation so a big pale area does not beat a bold mark.
        Span<double> weight = stackalloc double[24];
        Span<double> red = stackalloc double[24];
        Span<double> green = stackalloc double[24];
        Span<double> blue = stackalloc double[24];

        for (var index = 0; index + 3 < rgba.Length; index += 4)
        {
            if (rgba[index + 3] < 200)
            {
                continue;
            }

            var color = Color.FromRgb(rgba[index], rgba[index + 1], rgba[index + 2]);
            var hsl = color.ToHsl();
            if (hsl.S < 0.35 || hsl.L < 0.12 || hsl.L > 0.88)
            {
                continue;
            }

            var bucket = (int)(hsl.H / 15d) % 24;
            var w = hsl.S;
            weight[bucket] += w;
            red[bucket] += color.R * w;
            green[bucket] += color.G * w;
            blue[bucket] += color.B * w;
        }

        var best = -1;
        for (var bucket = 0; bucket < 24; bucket++)
        {
            if (weight[bucket] > 0 && (best < 0 || weight[bucket] > weight[best]))
            {
                best = bucket;
            }
        }

        // A stray anti-aliased edge is not a team colour.
        if (best < 0 || weight[best] < 12)
        {
            return null;
        }

        var mean = Color.FromRgb(
            (byte)Math.Round(red[best] / weight[best]),
            (byte)Math.Round(green[best] / weight[best]),
            (byte)Math.Round(blue[best] / weight[best]));

        return Legible(mean);
    }

    /// <summary>Lightens a colour, keeping its hue, until it clears <see cref="MinimumContrastOnSurface"/> on the darkest surface.</summary>
    internal static Color Legible(Color color)
    {
        var hsl = color.ToHsl();
        var lightness = hsl.L;
        var candidate = color;
        while (Contrast(candidate, Surface) < MinimumContrastOnSurface && lightness < 0.95)
        {
            lightness += 0.02;
            candidate = HslColor.ToRgb(hsl.H, hsl.S, lightness);
        }

        return candidate;
    }

    /// <summary>The ink for text set on a paint fill: whichever of navy or white contrasts more.</summary>
    internal static Color InkOn(Color paint) =>
        Contrast(DarkInk, paint) >= Contrast(LightInk, paint) ? DarkInk : LightInk;

    internal static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Repaints the accent brushes for a team's logo file; a missing or colourless logo restores the fallback.</summary>
    public static void Apply(string? logoPath) => Apply(null, logoPath);

    /// <summary>Repaints the accent brushes for a team: its stated colours first, then its logo, then the fallback.</summary>
    public static void Apply(TeamColours? colours, string? logoPath)
    {
        var application = global::Avalonia.Application.Current;
        if (application?.TryFindResource("AccentBrush", out var accentResource) != true || accentResource is not SolidColorBrush accent)
        {
            return;
        }

        _fallback ??= accent.Color;
        var stated = FromStated(colours);
        var paint = stated is { } statedColour ? Legible(statedColour) : ReadLogo(logoPath) ?? _fallback.Value;

        accent.Color = paint;
        Set(application, "AccentHoverBrush", Mix(paint, Colors.White, 0.18));
        Set(application, "AccentPressedBrush", Mix(paint, Colors.Black, 0.15));
        Set(application, "AccentSoftBrush", Color.FromArgb(0x33, paint.R, paint.G, paint.B));
        Set(application, "AccentFaintBrush", Color.FromArgb(0x14, paint.R, paint.G, paint.B));
        Set(application, "OnAccentBrush", InkOn(paint));

        if (application.TryFindResource("BannerBrush", out var bannerResource) && bannerResource is LinearGradientBrush banner && banner.GradientStops.Count == 3)
        {
            _bannerStart ??= banner.GradientStops[0].Color;
            // The band takes the stated colour as the team wears it, not the lifted text-safe version.
            var bandColour = stated ?? paint;
            banner.GradientStops[1].Color = Mix(_bannerStart.Value, bandColour, 0.14);
            banner.GradientStops[2].Color = Mix(_bannerStart.Value, bandColour, 0.42);
        }
    }

    /// <summary>
    /// The colour to paint with from a pack's stated pair: the primary, unless it is close to grey
    /// (black, white, silver) and the secondary carries more colour, as a black-and-gold team's gold.
    /// </summary>
    internal static Color? FromStated(TeamColours? colours)
    {
        if (colours is null || !Color.TryParse(colours.Primary, out var primary))
        {
            return null;
        }

        if (colours.Secondary is not null
            && Color.TryParse(colours.Secondary, out var secondary)
            && primary.ToHsl().S < 0.2
            && secondary.ToHsl().S > primary.ToHsl().S)
        {
            return secondary;
        }

        return primary;
    }

    /// <summary>
    /// Any team's colour as it wears it, for drawing that team rather than painting the client: the
    /// stated colours first, then its logo, else none. Not lifted — a caller setting text on it uses
    /// <see cref="InkOn"/>. Logo reads are cached, since a schedule draws the same opponents often.
    /// </summary>
    internal static Color? Shade(TeamColours? colours, string? logoPath)
    {
        if (FromStated(colours) is { } stated)
        {
            return stated;
        }

        return string.IsNullOrEmpty(logoPath) ? null : LogoShades.GetOrAdd(logoPath, ReadLogo);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Color?> LogoShades = new(StringComparer.Ordinal);

    private static Color? ReadLogo(string? logoPath)
    {
        if (string.IsNullOrEmpty(logoPath) || !File.Exists(logoPath))
        {
            return null;
        }

        try
        {
            using var source = new Bitmap(logoPath);
            using var small = source.CreateScaledBitmap(new PixelSize(64, 64));
            var stride = small.PixelSize.Width * 4;
            var buffer = new byte[stride * small.PixelSize.Height];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                small.CopyPixels(new PixelRect(small.PixelSize), handle.AddrOfPinnedObject(), buffer.Length, stride);
            }
            finally
            {
                handle.Free();
            }

            // Skia's native order on desktop is BGRA; swap to the RGBA Derive reads.
            if (small.Format is null || small.Format == PixelFormat.Bgra8888)
            {
                for (var index = 0; index + 3 < buffer.Length; index += 4)
                {
                    (buffer[index], buffer[index + 2]) = (buffer[index + 2], buffer[index]);
                }
            }

            return Derive(buffer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static void Set(global::Avalonia.Application application, string key, Color color)
    {
        if (application.TryFindResource(key, out var resource) && resource is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
