namespace BallGM.Infrastructure.Careers;

/// <summary>
/// One season of a career, as every file format states it: a data pack's history and a save's
/// concluded seasons share this one shape, and <see cref="CareerSeasonParser"/> is the one rule for
/// reading it. Nullable throughout because a file is untrusted: a missing total must be refused, not
/// read as zero. <see cref="Postseason"/> marks a playoff line, kept apart from the regular season.
/// </summary>
public sealed record CareerSeasonEnvelope(
    string? Season,
    string? Team,
    int? GamesPlayed,
    int? Minutes,
    int? Points,
    int? Rebounds,
    int? Assists,
    IReadOnlyDictionary<string, double>? Metrics = null,
    bool? Postseason = null);

/// <summary>One player's past seasons, as a save holds them.</summary>
public sealed record PlayerCareerEnvelope(
    string? PlayerId,
    IReadOnlyList<CareerSeasonEnvelope>? Seasons);
