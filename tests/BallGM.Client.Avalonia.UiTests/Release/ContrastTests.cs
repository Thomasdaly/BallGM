using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using BallGM.Client.Avalonia.Theming;
using BallGM.Client.Avalonia.UiTests.Harness;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>WCAG contrast for every text token and every fixture team (spec 06 section 3).</summary>
[Trait("Category", "UiRelease")]
public sealed class ContrastTests
{
    private static readonly string[] Surfaces = ["CanvasColor", "CanvasRaisedColor", "Surface1Color", "Surface2Color", "Surface3Color"];

    private static Color Token(string key)
    {
        Assert.True(global::Avalonia.Application.Current!.TryFindResource(key, out var value), $"Missing colour token '{key}'.");
        return Assert.IsType<Color>(value);
    }

    [AvaloniaFact]
    public void Contrast_Tokens()
    {
        var failures = new List<string>();
        void Require(string foreground, double minimum)
        {
            foreach (var surface in Surfaces)
            {
                var ratio = Wcag.Contrast(Token(foreground), Token(surface));
                if (ratio < minimum)
                {
                    failures.Add($"{foreground} on {surface}: {ratio:F2} < {minimum}");
                }
            }
        }

        foreach (var text in new[] { "TextPrimaryColor", "TextSecondaryColor", "TextMutedColor" })
        {
            Require(text, 4.5);
        }

        foreach (var semantic in new[] { "PositiveColor", "NegativeColor", "WarningColor", "InfoColor", "PositiveCvdColor", "NegativeCvdColor" })
        {
            Require(semantic, 4.5);
        }

        Require("AccentFallbackColor", 3.0);
        Require("FocusRingColor", 3.0);

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void Contrast_TeamColors()
    {
        Assert.Equal(30, FixtureTeams.All.Count);
        var failures = new List<string>();
        foreach (var team in FixtureTeams.All)
        {
            var theme = TeamTheme.From(team.Primary, team.Secondary);
            Check(failures, team.Key, "accent vs Surface2", Wcag.Contrast(theme.Accent, TeamTheme.Surface2), 3.0);
            Check(failures, team.Key, "OnAccent on accent", Wcag.Contrast(theme.OnAccent, theme.Accent), 4.5);
            Check(failures, team.Key, "OnPrimaryRaw on primary", Wcag.Contrast(theme.OnPrimaryRaw, theme.PrimaryRaw), 4.5);
            Check(failures, team.Key, "AccentAlt vs Surface2", Wcag.Contrast(theme.AccentAlt, TeamTheme.Surface2), 3.0);
            Assert.True(theme.OnAccent == Colors.White || theme.OnAccent == Colors.Black, $"{team.Key}: OnAccent must be pure white or black.");
        }

        foreach (var tier in RatingTiers.All)
        {
            Check(failures, tier.Name, "default chip text", Wcag.Contrast(Wcag.InkOn(tier.DefaultColour), tier.DefaultColour), 4.5);
            Check(failures, tier.Name, "CVD chip text", Wcag.Contrast(Wcag.InkOn(tier.CvdColour), tier.CvdColour), 4.5);
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void Contrast_FallbackThemeIsLegible()
    {
        var theme = TeamTheme.Fallback;
        Assert.True(Wcag.Contrast(theme.Accent, TeamTheme.Surface2) >= 3.0);
        Assert.True(Wcag.Contrast(theme.OnAccent, theme.Accent) >= 4.5);
        Assert.True(Wcag.Contrast(theme.OnPrimaryRaw, theme.PrimaryRaw) >= 4.5);
    }

    [Theory]
    [InlineData("#0B1D3A", "#19A7A0")] // navy primary: falls to the bright secondary, or lightens
    [InlineData("#5B2A86", "#C0C5CE")] // purple keeps its hue
    public void TeamAccent_KeepsTheTeamsHueWhereItCan(string primary, string secondary)
    {
        var theme = TeamTheme.From(primary, secondary);
        var primaryHue = Color.Parse(primary).ToHsl().H;
        var accentHue = theme.Accent.ToHsl().H;
        var keptPrimary = Math.Abs(primaryHue - accentHue) < 2;
        var usedSecondary = theme.Accent == Color.Parse(secondary);
        Assert.True(keptPrimary || usedSecondary, $"Accent {theme.Accent} neither keeps {primary}'s hue nor uses {secondary}.");
    }

    private static void Check(List<string> failures, string subject, string what, double ratio, double minimum)
    {
        if (ratio < minimum)
        {
            failures.Add($"{subject}: {what} {ratio:F2} < {minimum}");
        }
    }

    /// <summary>
    /// Stock text fields draw their placeholder and selected text at full contrast: Fluent's placeholder
    /// is half-opacity by default and its selection keeps white text on the accent (visual QA pass 3, S1).
    /// </summary>
    [AvaloniaFact]
    public void Contrast_PlaceholderAndSelectedText()
    {
        var box = new TextBox { PlaceholderText = "Player name", Width = 220 };
        var window = new Window { Content = box, Width = 400, Height = 200 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var placeholder = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box)
            .OfType<TextBlock>()
            .Single(block => block.Name == "PART_Placeholder");
        var field = Token("Surface3Color");
        var ink = Assert.IsAssignableFrom<ISolidColorBrush>(placeholder.Foreground).Color;
        var effective = Color.FromArgb(255, Blend(ink.R, field.R, placeholder.Opacity), Blend(ink.G, field.G, placeholder.Opacity), Blend(ink.B, field.B, placeholder.Opacity));
        Assert.True(Wcag.Contrast(effective, field) >= 4.5, $"Placeholder contrast {Wcag.Contrast(effective, field):F2} on Surface3 is below 4.5.");

        var selection = Assert.IsAssignableFrom<ISolidColorBrush>(box.SelectionBrush).Color;
        var selected = Assert.IsAssignableFrom<ISolidColorBrush>(box.SelectionForegroundBrush).Color;
        Assert.True(Wcag.Contrast(selected, selection) >= 4.5, $"Selected text contrast {Wcag.Contrast(selected, selection):F2} is below 4.5.");
        window.Close();
    }

    private static byte Blend(byte ink, byte background, double opacity) => (byte)Math.Round((ink * opacity) + (background * (1 - opacity)));
}
