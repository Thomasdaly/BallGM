using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// Writes runtime theme values into a resource dictionary: the application's (the user's team, the
/// rating palette) or a page root's (a page about another team scopes its own colours, spec 02
/// section 3). Every key written here is referenced with DynamicResource.
/// </summary>
public static class ThemeResources
{
    /// <summary>Whether the user's team colours drive the interface (Settings, "Team colours in interface").</summary>
    public static bool TeamColoursEnabled { get; set; } = true;

    /// <summary>Applies a team's theme; with team colours off this is always the fallback.</summary>
    public static void ApplyTeam(IResourceDictionary target, TeamTheme theme, bool includeBaseThemeAccent)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(theme);
        if (!TeamColoursEnabled)
        {
            theme = TeamTheme.Fallback;
        }

        target["AccentColor"] = theme.Accent;
        target["AccentBrush"] = new SolidColorBrush(theme.Accent);
        target["AccentHoverBrush"] = new SolidColorBrush(Wcag.Lighten(theme.Accent, 0.06));
        target["AccentPressedBrush"] = new SolidColorBrush(Wcag.Lighten(theme.Accent, -0.06));
        target["AccentSoftBrush"] = new SolidColorBrush(theme.Accent, 0.2);
        target["AccentFaintBrush"] = new SolidColorBrush(theme.Accent, 0.08);
        target["OnAccentBrush"] = new SolidColorBrush(theme.OnAccent);
        target["AccentAltBrush"] = new SolidColorBrush(theme.AccentAlt);
        target["TeamPrimaryRawBrush"] = new SolidColorBrush(theme.PrimaryRaw);
        target["TeamSecondaryRawBrush"] = new SolidColorBrush(theme.SecondaryRaw);
        target["OnPrimaryRawBrush"] = new SolidColorBrush(theme.OnPrimaryRaw);
        target["TeamGradientBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.3, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.7, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(theme.PrimaryRaw, 0),
                new GradientStop(Color.FromArgb(0, theme.PrimaryRaw.R, theme.PrimaryRaw.G, theme.PrimaryRaw.B), 1),
            },
        };

        if (includeBaseThemeAccent)
        {
            // Fluent's own accent, so untouched stock controls follow the team too.
            target["SystemAccentColor"] = theme.Accent;
            target["SystemAccentColorLight1"] = Wcag.Lighten(theme.Accent, 0.08);
            target["SystemAccentColorLight2"] = Wcag.Lighten(theme.Accent, 0.16);
            target["SystemAccentColorLight3"] = Wcag.Lighten(theme.Accent, 0.24);
            target["SystemAccentColorDark1"] = Wcag.Lighten(theme.Accent, -0.08);
            target["SystemAccentColorDark2"] = Wcag.Lighten(theme.Accent, -0.16);
            target["SystemAccentColorDark3"] = Wcag.Lighten(theme.Accent, -0.24);
        }
    }

    /// <summary>The user's team, application-wide.</summary>
    public static void ApplyUserTeam(TeamTheme theme)
    {
        if (global::Avalonia.Application.Current is { } application)
        {
            ApplyTeam(application.Resources, theme, includeBaseThemeAccent: true);
        }
    }

    /// <summary>Scopes another team's colours to a page root and its subtree.</summary>
    public static void ScopeTeam(Control root, TeamTheme theme)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyTeam(root.Resources, theme, includeBaseThemeAccent: false);
    }

    /// <summary>Rating tiers and the positive/negative pair: default or colour-vision-safe (spec 01 section 12).</summary>
    public static void ApplyRatingPalette(IResourceDictionary target, bool colourVisionSafe)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var tier in RatingTiers.All)
        {
            var colour = colourVisionSafe ? tier.CvdColour : tier.DefaultColour;
            target[tier.BrushKey] = new SolidColorBrush(colour);
            target[tier.BrushKey.Replace("Brush", "OnBrush", StringComparison.Ordinal)] = new SolidColorBrush(Wcag.InkOn(colour));
        }

        var positive = Color.Parse(colourVisionSafe ? "#5AAEFF" : "#2BD67B");
        var negative = Color.Parse(colourVisionSafe ? "#FF9F43" : "#FF6B78");
        target["PositiveBrush"] = new SolidColorBrush(positive);
        target["NegativeBrush"] = new SolidColorBrush(negative);
        target["PositiveSoftBrush"] = new SolidColorBrush(positive, 0.16);
        target["NegativeSoftBrush"] = new SolidColorBrush(negative, 0.16);
    }
}
