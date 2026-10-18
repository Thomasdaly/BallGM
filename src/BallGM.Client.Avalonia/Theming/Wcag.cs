using Avalonia.Media;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>WCAG 2.x relative luminance and contrast, plus the HSL lightening the team accent uses.</summary>
public static class Wcag
{
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>Contrast ratio of two opaque colours, 1 to 21.</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Pure white or pure black, whichever contrasts more with <paramref name="background"/>.</summary>
    public static Color InkOn(Color background) =>
        Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;

    /// <summary>Raises HSL lightness by <paramref name="amount"/> (0-1), keeping hue and saturation.</summary>
    public static Color Lighten(Color color, double amount)
    {
        var hsl = color.ToHsl();
        return HslColor.ToRgb(hsl.H, hsl.S, Math.Clamp(hsl.L + amount, 0, 1), color.A / 255.0);
    }

    /// <summary>The colour at <paramref name="alpha"/> (0-1) composited over an opaque <paramref name="under"/>.</summary>
    public static Color Over(Color color, double alpha, Color under) => Color.FromRgb(
        (byte)Math.Round((color.R * alpha) + (under.R * (1 - alpha))),
        (byte)Math.Round((color.G * alpha) + (under.G * (1 - alpha))),
        (byte)Math.Round((color.B * alpha) + (under.B * (1 - alpha))));
}
