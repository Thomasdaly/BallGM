using BallGM.Client.Avalonia.ViewModels;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.Tests;

public sealed class ShotChartViewTests
{
    [Fact]
    public void ADifferenceWithinTwoPointsIsLevelAndTheArmsStepEveryFourPoints()
    {
        Assert.Equal(0, ShotChartView.StepOf(null));
        Assert.Equal(0, ShotChartView.StepOf(1.9));
        Assert.Equal(0, ShotChartView.StepOf(-2.0));
        Assert.Equal(1, ShotChartView.StepOf(2.5));
        Assert.Equal(2, ShotChartView.StepOf(6.5));
        Assert.Equal(3, ShotChartView.StepOf(25));
        Assert.Equal(-3, ShotChartView.StepOf(-25));
    }

    [Fact]
    public void EachZonePrintsItsShootingAndDifferenceAndAThinZoneSaysSo()
    {
        var player = ShotZones.None with { RimAttempts = 20, RimMade = 15, Corner3Attempts = 3, Corner3Made = 2 };
        var league = ShotZones.None with { RimAttempts = 1000, RimMade = 640, Corner3Attempts = 1000, Corner3Made = 400 };

        var chart = ShotChartView.For(player, league)!;
        var rim = chart.Zones.Single(zone => zone.Zone == ShotZone.Rim);
        var corner = chart.Zones.Single(zone => zone.Zone == ShotZone.Corner3);

        Assert.Equal("75.0% · 15/20", rim.Shooting);
        Assert.Equal("+11.0 pts", rim.Difference);
        Assert.Equal(3, rim.Step);
        Assert.Equal("2/3", corner.Shooting);
        Assert.Equal("Fewer than 5 attempts", corner.Difference);
        Assert.Equal(0, corner.Step);
        Assert.Null(ShotChartView.For(ShotZones.None, league));
    }
}
