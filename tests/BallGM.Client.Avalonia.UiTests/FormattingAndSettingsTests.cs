using BallGM.Client.Avalonia.Formatting;
using BallGM.Client.Avalonia.Settings;

namespace BallGM.Client.Avalonia.UiTests;

public sealed class FormattingAndSettingsTests
{
    [Theory]
    [InlineData(24_500_000, "$24.5M")]
    [InlineData(850_000, "$850K")]
    [InlineData(1_000_000, "$1.0M")]
    [InlineData(0, "$0")]
    [InlineData(-3_100_000, "-$3.1M")]
    public void Money_PrintsInTheHouseFormat(long amount, string expected) => Assert.Equal(expected, Fmt.Money(amount));

    [Fact]
    public void Records_StreaksAndOrdinals_UseTheHouseFormats()
    {
        Assert.Equal("24–11", Fmt.Record(24, 11));
        Assert.Equal(".686", Fmt.WinPercentage(24, 11));
        Assert.Equal("1.000", Fmt.WinPercentage(5, 0));
        Assert.Equal("W4", Fmt.Streak(true, 4));
        Assert.Equal("L2", Fmt.Streak(false, 2));
        Assert.Equal("1st", Fmt.Ordinal(1));
        Assert.Equal("12th", Fmt.Ordinal(12));
        Assert.Equal("22nd", Fmt.Ordinal(22));
        Assert.Equal("47.3%", Fmt.Percent(0.473));
        Assert.Equal("+4.8", Fmt.SignedOneDecimal(4.8));
    }

    [Fact]
    public void HeightsAndWeights_FollowTheUnitsSetting()
    {
        var previous = Fmt.Units;
        try
        {
            Fmt.Units = UnitSystem.Imperial;
            Assert.Equal("6'7\"", Fmt.Height(201));
            Assert.Equal("216 lb", Fmt.Weight(98));
            Fmt.Units = UnitSystem.Metric;
            Assert.Equal("201 cm", Fmt.Height(201));
            Assert.Equal("98 kg", Fmt.Weight(98));
        }
        finally
        {
            Fmt.Units = previous;
        }
    }

    [Fact]
    public void Settings_RoundTripThroughTheStore()
    {
        var paths = new UiPaths(Directory.CreateTempSubdirectory("ballgm-settings-").FullName);
        var store = new UiSettingsStore(paths);
        var settings = new UiSettings
        {
            UiScalePercent = 125,
            Density = Density.Compact,
            ReducedMotion = true,
            TeamColours = false,
            RatingPalette = RatingPalette.ColourVisionSafe,
            Units = UnitSystem.Metric,
            Ticker = false,
            ShowEntityIds = true,
            Window = new WindowPlacement(10, 20, 1600, 900, false, false),
            Columns = new Dictionary<string, IReadOnlyList<string>> { ["roster"] = ["OVR", "PTS"] },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(125, loaded.UiScalePercent);
        Assert.Equal(Density.Compact, loaded.Density);
        Assert.True(loaded.ReducedMotion);
        Assert.False(loaded.TeamColours);
        Assert.Equal(RatingPalette.ColourVisionSafe, loaded.RatingPalette);
        Assert.Equal(UnitSystem.Metric, loaded.Units);
        Assert.False(loaded.Ticker);
        Assert.True(loaded.ShowEntityIds);
        Assert.Equal(settings.Window, loaded.Window);
        Assert.Equal(["OVR", "PTS"], loaded.Columns["roster"]);
    }

    [Fact]
    public void Settings_ACorruptFileFallsBackToDefaults()
    {
        var paths = new UiPaths(Directory.CreateTempSubdirectory("ballgm-settings-").FullName);
        File.WriteAllText(paths.SettingsFile, "{ not json");

        Assert.Equal(100, new UiSettingsStore(paths).Load().UiScalePercent);
    }

    [Fact]
    public void Settings_ScaleIsClampedToTheSupportedRange()
    {
        Assert.Equal(150, new UiSettings { UiScalePercent = 400 }.Normalised().UiScalePercent);
        Assert.Equal(90, new UiSettings { UiScalePercent = 10 }.Normalised().UiScalePercent);
    }
}
