using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.Converters;

namespace BallGM.Client.Avalonia.Tests;

public sealed class ThemeAndConverterTests
{
    [Theory]
    [InlineData(95, "TierEliteBrush")]
    [InlineData(90, "TierEliteBrush")]
    [InlineData(89, "TierStarBrush")]
    [InlineData(72, "TierStarterBrush")]
    [InlineData(64, "TierRotationBrush")]
    [InlineData(56, "TierBenchBrush")]
    [InlineData(40, "TierFringeBrush")]
    public void OverallBadge_ColoursEachRatingByItsTier(int overall, string expectedKey)
    {
        Assert.Equal(expectedKey, OverallTierConverter.TierKey(overall));
    }

    [Theory]
    [InlineData("Golden State Warriors", "GSW")]
    [InlineData("Chicago Bulls", "CHI")]
    [InlineData("Heat", "HEA")]
    [InlineData("Ox", "OX")]
    [InlineData("", "")]
    public void TeamCrest_IsDrawnFromTheTeamName(string teamName, string expected)
    {
        Assert.Equal(expected, TeamInitialsConverter.Initials(teamName));
    }

    [Theory]
    [InlineData("Roster", "Icon.Roster")]
    [InlineData("Cap sheet", "Icon.CapSheet")]
    [InlineData("Free agency board", "Icon.FreeAgencyBoard")]
    public void NavigationSection_MapsToItsIconResource(string section, string expectedKey)
    {
        Assert.Equal(expectedKey, SectionIconConverter.IconKey(section));
    }

    [AvaloniaFact]
    public void EveryNavigationSection_HasAnIconInTheTheme()
    {
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();

        Assert.NotEmpty(viewModel.Sections);
        foreach (var section in viewModel.Sections)
        {
            var key = SectionIconConverter.IconKey(section);
            Assert.True(global::Avalonia.Application.Current!.TryFindResource(key, out _), $"No icon resource '{key}' for section '{section}'.");
        }
    }

    // Only the missing-file and no-path cases: the headless platform's bitmap stub "decodes" any
    // bytes as a 1x1 image, so a corrupt file cannot be told apart from a real one here.
    [AvaloniaFact]
    public void PackImage_ThatIsMissingOrUnstated_DrawsNothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ballgm-" + Guid.NewGuid().ToString("N") + ".png");

        Assert.Null(ImagePathConverter.Instance.Convert(missing, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(ImagePathConverter.Instance.Convert(null, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void DesignTokens_LoadWithTheApplication()
    {
        foreach (var key in new[] { "AccentBrush", "Bg0Brush", "TextBrush", "TierEliteBrush", "BannerBrush" })
        {
            Assert.True(global::Avalonia.Application.Current!.TryFindResource(key, out var value) && value is not null, $"Missing design token '{key}'.");
        }
    }
}
