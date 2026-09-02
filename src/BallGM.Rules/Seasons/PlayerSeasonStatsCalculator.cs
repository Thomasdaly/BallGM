using BallGM.Domain.Seasons;

namespace BallGM.Rules.Seasons;

/// <summary>
/// Sums every played game's <see cref="BoxScore"/> lines into one <see cref="PlayerSeasonStatLine"/>
/// per player who appeared. Pure and total: a season with no games played yet returns an empty list
/// rather than a line of zeroes for everyone on every roster — a player who has not played has no
/// stat line, the same "absence is not zero" reading `BandedScale` and the cap thresholds use.
/// <para>
/// Takes <see cref="GameResult"/>s directly rather than a whole <c>SeasonRun</c>, the same shape
/// <c>StandingsCalculator.Calculate</c> already takes its results in — a calculator that only reads
/// results should only need to be handed results.
/// </para>
/// </summary>
public static class PlayerSeasonStatsCalculator
{
    public static IReadOnlyList<PlayerSeasonStatLine> Calculate(IEnumerable<GameResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .Where(result => result.BoxScore is not null)
            .SelectMany(result => result.BoxScore!.Lines)
            .GroupBy(line => line.PlayerId.Value)
            .Select(group => new PlayerSeasonStatLine(
                group.First().PlayerId,
                GamesPlayed: group.Count(),
                TotalMinutes: group.Sum(line => line.Minutes),
                TotalPoints: group.Sum(line => line.Points),
                TotalRebounds: group.Sum(line => line.Rebounds),
                TotalAssists: group.Sum(line => line.Assists)))
            .OrderBy(line => line.PlayerId.Value, StringComparer.Ordinal)
            .ToArray();
    }
}
