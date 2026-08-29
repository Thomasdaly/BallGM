using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One player's line in one game. Minutes are carried alongside the counting statistics because a
/// box score that does not say how long someone was on the floor cannot explain any of the rest of
/// it — and minutes are what fatigue accrues against.
/// </summary>
public sealed record PlayerStatLine
{
    public PlayerStatLine(
        PlayerId playerId,
        TeamId teamId,
        int minutes,
        int points,
        int rebounds,
        int assists,
        bool started)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(teamId);

        ThrowIfNegative(minutes, nameof(minutes));
        ThrowIfNegative(points, nameof(points));
        ThrowIfNegative(rebounds, nameof(rebounds));
        ThrowIfNegative(assists, nameof(assists));

        PlayerId = playerId;
        TeamId = teamId;
        Minutes = minutes;
        Points = points;
        Rebounds = rebounds;
        Assists = assists;
        Started = started;
    }

    public PlayerId PlayerId { get; }

    public TeamId TeamId { get; }

    public int Minutes { get; }

    public int Points { get; }

    public int Rebounds { get; }

    public int Assists { get; }

    public bool Started { get; }

    private static void ThrowIfNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A box-score figure cannot be negative.");
        }
    }
}

/// <summary>
/// Every player line from one game, and nothing else. The team totals are not stored: a box score
/// whose totals could disagree with its lines is two accounts of one game, and the derived answer
/// is the one that cannot drift.
/// </summary>
public sealed class BoxScore
{
    private const string PointsDoNotMatchCode = "box_score.points_do_not_match_result";
    private const string UnknownTeamCode = "box_score.line_for_team_not_playing";

    private readonly List<PlayerStatLine> _lines;

    private BoxScore(GameId gameId, TeamId homeTeamId, TeamId awayTeamId, List<PlayerStatLine> lines)
    {
        GameId = gameId;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        _lines = lines;
    }

    /// <summary>
    /// Builds a box score, refusing lines from a team that is not in the game and refusing totals
    /// that disagree with the stated result. The second check is the point of the type: a game whose
    /// final score and whose player points differ is a bug the standings would inherit silently.
    /// </summary>
    public static DomainOperationResult<BoxScore> Create(
        GameId gameId,
        TeamId homeTeamId,
        TeamId awayTeamId,
        int homePoints,
        int awayPoints,
        IEnumerable<PlayerStatLine> lines)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        ArgumentNullException.ThrowIfNull(homeTeamId);
        ArgumentNullException.ThrowIfNull(awayTeamId);
        ArgumentNullException.ThrowIfNull(lines);

        var supplied = lines.ToList();
        if (supplied.Any(line => line is null))
        {
            throw new ArgumentException("A box score cannot contain null player lines.", nameof(lines));
        }

        var errors = new List<DomainError>();

        foreach (var line in supplied.Where(line => line.TeamId != homeTeamId && line.TeamId != awayTeamId))
        {
            errors.Add(new DomainError(
                UnknownTeamCode,
                $"Player '{line.PlayerId.Value}' has a line in game '{gameId.Value}' for team '{line.TeamId.Value}', which is not playing in it."));
        }

        var homeLineTotal = supplied.Where(line => line.TeamId == homeTeamId).Sum(line => line.Points);
        var awayLineTotal = supplied.Where(line => line.TeamId == awayTeamId).Sum(line => line.Points);

        if (homeLineTotal != homePoints || awayLineTotal != awayPoints)
        {
            errors.Add(new DomainError(
                PointsDoNotMatchCode,
                $"Game '{gameId.Value}' finished {homePoints}-{awayPoints} but its player lines add up to {homeLineTotal}-{awayLineTotal}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<BoxScore>.Failure(errors.ToArray())
            : DomainOperationResult<BoxScore>.Success(new BoxScore(gameId, homeTeamId, awayTeamId, supplied));
    }

    public GameId GameId { get; }

    public TeamId HomeTeamId { get; }

    public TeamId AwayTeamId { get; }

    public IReadOnlyList<PlayerStatLine> Lines => _lines;

    public IReadOnlyList<PlayerStatLine> LinesFor(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        return _lines
            .Where(line => line.TeamId == teamId)
            .OrderByDescending(line => line.Started)
            .ThenByDescending(line => line.Minutes)
            .ThenBy(line => line.PlayerId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public int PointsFor(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _lines.Where(line => line.TeamId == teamId).Sum(line => line.Points);
    }
}
