using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using BallGM.Application.Leagues;
using BallGM.Client.Avalonia.Converters;
using BallGM.Client.Avalonia.Theming;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Tests;

public sealed class DesignSystemTests
{
    private static byte[] Pixels(params (Color Color, int Count)[] runs) =>
        runs.SelectMany(run => Enumerable.Repeat(new[] { run.Color.R, run.Color.G, run.Color.B, run.Color.A }, run.Count))
            .SelectMany(pixel => pixel)
            .ToArray();

    [Fact]
    public void TeamPaint_TakesTheLogosDominantStrongColour()
    {
        var red = Color.FromRgb(0xC8, 0x10, 0x2E);
        var white = Colors.White;
        var gold = Color.FromRgb(0xFD, 0xB9, 0x27);

        var paint = TeamPaint.Derive(Pixels((white, 2000), (red, 900), (gold, 200)));

        Assert.NotNull(paint);
        var hue = paint.Value.ToHsl().H;
        Assert.True(hue < 15 || hue > 345, $"Expected a red paint, got hue {hue}.");
    }

    [Fact]
    public void TeamPaint_GivesNoPaintForAMonochromeLogo()
    {
        var paint = TeamPaint.Derive(Pixels((Colors.Black, 1500), (Colors.White, 1500), (Color.FromRgb(0x80, 0x80, 0x80), 400)));

        Assert.Null(paint);
    }

    [Fact]
    public void TeamPaint_IgnoresTransparentPixels()
    {
        var paint = TeamPaint.Derive(Pixels((Color.FromArgb(0, 0x00, 0x7A, 0x33), 4000)));

        Assert.Null(paint);
    }

    [Theory]
    [InlineData("#002B5C")] // a navy that would vanish on the navy surfaces
    [InlineData("#552583")]
    [InlineData("#000000")]
    public void TeamPaint_LiftsADarkColourUntilItReadsOnTheSurface(string hex)
    {
        var paint = TeamPaint.Legible(Color.Parse(hex));

        Assert.True(TeamPaint.Contrast(paint, Color.Parse("#0D1522")) >= TeamPaint.MinimumContrastOnSurface - 0.2);
    }

    [Fact]
    public void TeamPaint_SetsTextOnPaintInWhicheverInkReads()
    {
        Assert.Equal(Color.Parse("#0D1522"), TeamPaint.InkOn(Color.Parse("#FDB927")));
        Assert.Equal(Colors.White, TeamPaint.InkOn(Color.Parse("#7A1FA2")));
    }

    [Fact]
    public void TeamPaint_TakesAPacksStatedPrimaryColour()
    {
        var paint = TeamPaint.FromStated(new TeamColours("#C8102E", "#FDB927"));

        Assert.Equal(Color.Parse("#C8102E"), paint);
    }

    [AvaloniaFact]
    public void TeamShade_PlatesATeamInItsStatedColourWithInkThatReads()
    {
        var art = TeamArt.For("Bravo Town", colours: new TeamColours("#FDB927"));

        var plate = Assert.IsAssignableFrom<ISolidColorBrush>(TeamShadeConverter.Instance.Convert(art, typeof(IBrush), "plate", CultureInfo.InvariantCulture));
        var ink = Assert.IsAssignableFrom<ISolidColorBrush>(TeamShadeConverter.Instance.Convert(art, typeof(IBrush), "ink", CultureInfo.InvariantCulture));

        Assert.Equal(Color.Parse("#FDB927"), plate.Color);
        Assert.Equal(Color.Parse("#0D1522"), ink.Color);
    }

    [AvaloniaFact]
    public void TeamShade_WashesNothingForATeamWithNoColourAndNoLogo()
    {
        var wash = TeamShadeConverter.Instance.Convert(TeamArt.For("Bravo Town"), typeof(IBrush), "wash", CultureInfo.InvariantCulture);

        Assert.Same(Brushes.Transparent, wash);
    }

    [Fact]
    public void TeamPaint_TakesTheSecondaryWhenThePrimaryIsGrey()
    {
        // A black-and-gold team paints in gold; black would lift to a featureless grey.
        var paint = TeamPaint.FromStated(new TeamColours("#000000", "#FFB81C"));

        Assert.Equal(Color.Parse("#FFB81C"), paint);
    }

    [Fact]
    public void TeamPaint_WithNoStatedColoursFallsBackToTheLogo()
    {
        Assert.Null(TeamPaint.FromStated(null));
    }

    [Theory]
    [InlineData("RegularSeason", "Regular season")]
    [InlineData("HeadToHeadRecord", "Head to head record")]
    [InlineData("Preseason", "Preseason")]
    [InlineData("", "")]
    public void DisplayText_TurnsIdentifiersIntoWords(string identifier, string expected)
    {
        Assert.Equal(expected, DisplayText.Words(identifier));
    }

    [Theory]
    [InlineData(1, "1 game")]
    [InlineData(0, "0 games")]
    [InlineData(449, "449 games")]
    public void DisplayText_CountsWithoutParentheticalPlurals(int count, string expected)
    {
        Assert.Equal(expected, DisplayText.Count(count, "game"));
    }

    [AvaloniaFact]
    public void Sidebar_PutsEverySectionUnderExactlyOneHeading()
    {
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();

        var grouped = viewModel.NavGroups.SelectMany(group => group.Sections).ToList();

        Assert.Equal(viewModel.Sections.OrderBy(section => section), grouped.OrderBy(section => section));
    }

    [AvaloniaFact]
    public void Sidebar_SelectionInOneGroupClearsTheOthersWithoutLosingTheScreen()
    {
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();
        var market = viewModel.NavGroups.Single(group => group.Name == "Market");
        var squad = viewModel.NavGroups.Single(group => group.Name == "Squad");

        market.Selected = market.Sections[0];
        squad.Selected = null; // what the squad list pushes when it loses the selection

        Assert.Equal(market.Sections[0], viewModel.SelectedSection);
        Assert.Equal(market.Sections[0], market.Selected);
        Assert.Null(squad.Selected);
    }

    [AvaloniaFact]
    public void TeamPaint_WithNoLogoRestoresTheFallbackAccent()
    {
        var application = global::Avalonia.Application.Current!;
        Assert.True(application.TryFindResource("AccentBrush", out var resource));
        var accent = Assert.IsType<SolidColorBrush>(resource);
        var fallback = accent.Color;

        TeamPaint.Apply(Path.Combine(Path.GetTempPath(), "ballgm-missing-" + Guid.NewGuid().ToString("N") + ".png"));

        Assert.Equal(fallback, accent.Color);
    }
}
