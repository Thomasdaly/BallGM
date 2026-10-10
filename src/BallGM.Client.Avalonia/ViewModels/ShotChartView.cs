using BallGM.Application.Players;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One zone of the shot chart: what the shooter did there, how that compares with the league from the
/// same spot, and the colour step that comparison maps to (−3 colder … 0 level … +3 hotter). The
/// figures are always printed; the step is never the only signal.
/// </summary>
public sealed record ShotZoneCell(ShotZone Zone, string Name, string Shooting, string Difference, string League, int Step)
{
    /// <summary>What the chart prints in the zone: short enough for the narrowest region, the table carrying the rest.</summary>
    public string Label => Step == 0 && !Difference.EndsWith("pts", StringComparison.Ordinal) ? Shooting : $"{Shooting}\n{Difference}";

    public string AccessibleName => $"{Name}: {Shooting}, {Difference} against the league's {League}";
}

/// <summary>The five zones as the profile's shot chart and its table show them.</summary>
public sealed record ShotChartView(IReadOnlyList<ShotZoneCell> Zones)
{
    /// <summary>Within this many percentage points of the league is level; the arms step every four points past it.</summary>
    public const double LevelBand = 2.0;

    /// <summary>The legend's words for the level band, from the band itself.</summary>
    public static string LevelLegend { get; } = $"League average (±{LevelBand:0} pts)";

    private const double StepWidth = 4.0;

    public static ShotChartView? For(ShotZones? player, ShotZones? league)
    {
        if (player is null || league is null || player.TotalAttempts == 0)
        {
            return null;
        }

        return new ShotChartView(ShotZones.All.Select(zone =>
        {
            var difference = StatFormulas.ZoneDifference(player, league, zone);
            var attempts = player.Attempts(zone);
            return new ShotZoneCell(
                zone,
                NameOf(zone),
                attempts < StatFormulas.MinimumZoneAttempts
                    ? $"{player.Made(zone)}/{attempts}"
                    : $"{StatColumns.Percent(StatFormulas.ZonePercent(player, zone))}% · {player.Made(zone)}/{attempts}",
                difference is { } points ? $"{StatColumns.Signed(points)} pts" : $"Fewer than {StatFormulas.MinimumZoneAttempts} attempts",
                $"{StatColumns.Percent(StatFormulas.ZonePercent(league, zone))}%",
                StepOf(difference));
        }).ToList());
    }

    /// <summary>0 within the level band, then one step per four points, three at most, signed.</summary>
    public static int StepOf(double? difference) => difference is not { } points || Math.Abs(points) <= LevelBand
        ? 0
        : Math.Sign(points) * Math.Min(3, 1 + (int)((Math.Abs(points) - LevelBand) / StepWidth));

    private static string NameOf(ShotZone zone) => zone switch
    {
        ShotZone.Rim => "At the rim",
        ShotZone.Paint => "In the paint",
        ShotZone.MidRange => "Mid-range",
        ShotZone.Corner3 => "Corner three",
        _ => "Above-the-break three",
    };
}
