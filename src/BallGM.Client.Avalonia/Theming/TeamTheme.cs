using Avalonia.Media;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// A team's interface colours, derived so the team keeps its identity and stays legible
/// (docs/ui/spec/01_DESIGN_LANGUAGE.md section 4). Derived against 3.1:1 so tests asserting 3.0 never
/// flake on rounding.
/// </summary>
public sealed record TeamTheme(Color Accent, Color OnAccent, Color AccentAlt, Color PrimaryRaw, Color SecondaryRaw, Color OnPrimaryRaw)
{
    public const double TargetContrast = 3.1;
    private const double LightenStep = 0.02;
    private const double LightenCap = 0.25;

    /// <summary>The surface the accent is measured against.</summary>
    public static readonly Color Surface2 = Color.Parse("#1A2331");

    public static readonly Color AccentFallback = Color.Parse("#3D8BFF");

    /// <summary>Team colours off, or a team with none: the fallback accent and a neutral band.</summary>
    public static TeamTheme Fallback { get; } = new(
        AccentFallback,
        Wcag.InkOn(AccentFallback),
        Wcag.Lighten(AccentFallback, 0.12),
        Color.Parse("#222D3E"),
        AccentFallback,
        Colors.White);

    public static TeamTheme Derive(Color primary, Color? secondary)
    {
        primary = Opaque(primary);
        var other = secondary is { } stated ? Opaque(stated) : (Color?)null;

        Color accent;
        bool fromSecondary = false;
        if (Wcag.Contrast(primary, Surface2) >= TargetContrast)
        {
            accent = primary;
        }
        else if (LightenUntilLegible(primary, LightenCap) is { } lifted)
        {
            accent = lifted;
        }
        else if (other is { } alt && Wcag.Contrast(alt, Surface2) >= TargetContrast)
        {
            accent = alt;
            fromSecondary = true;
        }
        else
        {
            accent = LightenUntilLegible(primary, 1.0) ?? Colors.White;
        }

        var alternate = fromSecondary ? primary : other;
        var accentAlt = alternate is { } candidate && Wcag.Contrast(candidate, Surface2) >= TargetContrast
            ? candidate
            : Wcag.Lighten(accent, 0.12);

        return new TeamTheme(accent, Wcag.InkOn(accent), accentAlt, primary, other ?? primary, Wcag.InkOn(primary));
    }

    /// <summary>Parses pack colours (#RRGGBB); null or unparseable gives the fallback theme.</summary>
    public static TeamTheme From(string? primary, string? secondary)
    {
        if (primary is null || !Color.TryParse(primary, out var parsedPrimary))
        {
            return Fallback;
        }

        return Derive(parsedPrimary, secondary is not null && Color.TryParse(secondary, out var parsedSecondary) ? parsedSecondary : null);
    }

    private static Color? LightenUntilLegible(Color color, double cap)
    {
        for (var amount = LightenStep; amount <= cap + 1e-9; amount += LightenStep)
        {
            var lifted = Wcag.Lighten(color, amount);
            if (Wcag.Contrast(lifted, Surface2) >= TargetContrast)
            {
                return lifted;
            }
        }

        return null;
    }

    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);
}
