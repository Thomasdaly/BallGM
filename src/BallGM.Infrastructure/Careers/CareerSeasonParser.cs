using BallGM.Application.Players;

namespace BallGM.Infrastructure.Careers;

/// <summary>
/// The one rule for what counts as a readable career season, shared by every file format that states
/// one (a data pack's history, a save's concluded seasons), so the two can never drift apart: a
/// label, five non-negative totals that are actually present, and metrics that are each a stat code
/// and a finite number. Text is trimmed and a blank team is no team.
/// </summary>
internal static class CareerSeasonParser
{
    public const string Requirement =
        "A career season needs a season label, non-negative gamesPlayed, minutes, points, rebounds, and assists, and metrics that are each a stat code and a finite number.";

    public static CareerSeasonLine? TryParse(CareerSeasonEnvelope? season)
    {
        if (season is not
            {
                Season: { } label,
                GamesPlayed: >= 0 and var games,
                Minutes: >= 0 and var minutes,
                Points: >= 0 and var points,
                Rebounds: >= 0 and var rebounds,
                Assists: >= 0 and var assists,
            } ||
            string.IsNullOrWhiteSpace(label) ||
            season.Metrics?.Any(metric => string.IsNullOrWhiteSpace(metric.Key) || !double.IsFinite(metric.Value)) == true)
        {
            return null;
        }

        return new CareerSeasonLine(
            label.Trim(),
            string.IsNullOrWhiteSpace(season.Team) ? null : season.Team.Trim(),
            games,
            minutes,
            points,
            rebounds,
            assists,
            Metrics: season.Metrics is { Count: > 0 } stated ? new Dictionary<string, double>(stated, StringComparer.Ordinal) : null,
            IsPostseason: season.Postseason == true);
    }

    public static CareerSeasonEnvelope ToEnvelope(CareerSeasonLine line) =>
        new(
            line.Season,
            line.TeamName,
            line.GamesPlayed,
            line.Minutes,
            line.Points,
            line.Rebounds,
            line.Assists,
            line.Metrics is { Count: > 0 } metrics
                ? metrics.OrderBy(metric => metric.Key, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal)
                : null,
            line.IsPostseason ? true : null);
}
