using BallGM.Application.Seasons;
using BallGM.Domain.Common;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// Where a player stands in the league on one stat: a 0–100 percentile, always printed beside its bar
/// so the bar is never the only signal. Higher is always better — turnovers and fouls arrive flipped,
/// and say so. The order and the direction are the Application layer's, not this row's.
/// </summary>
public sealed record PercentileRow(string Stat, int Percentile, bool LowerIsBetter)
{
    public string Text => ExplanationText.Ordinal(Percentile);

    public string Tooltip => LowerIsBetter
        ? $"{Stat}: fewer than {Percentile}% of qualified players this regular season (lower is better, so the scale is flipped)."
        : $"{Stat}: ahead of {Percentile}% of qualified players this regular season.";

    public string AccessibleName => $"{Stat}, {Text} percentile";

    public static IReadOnlyList<PercentileRow> From(IReadOnlyList<StatPercentile>? percentiles) =>
        percentiles?.Select(line => new PercentileRow(line.Stat, line.Percentile, line.LowerIsBetter)).ToList() ?? [];
}
