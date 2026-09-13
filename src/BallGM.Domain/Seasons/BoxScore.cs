using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One player's line in one game. Minutes are carried alongside the counting statistics because a
/// box score that does not say how long someone was on the floor cannot explain any of the rest of
/// it — and minutes are what fatigue accrues against.
/// <para>
/// <see cref="OffensiveRebounds"/> and <see cref="DefensiveRebounds"/> are the stored figures;
/// <see cref="Rebounds"/> is derived from them rather than stored separately, the same "re-derived,
/// never stored" reading <c>PlayerRating.Overall</c> already gives a value computable from state
/// already held — a combined total and its own split could otherwise silently disagree.
/// </para>
/// <para>
/// <see cref="Points"/> is checked against <see cref="FieldGoalsMade"/>/<see cref="ThreePointsMade"/>/
/// <see cref="FreeThrowsMade"/> at construction — <c>Points == 2×(FGM−3PM) + 3×3PM + FTM</c> — because
/// every figure it needs is already on this same line: a scoring total that disagreed with its own
/// shooting line would be a bug this type could have refused to hold. Attempts-vs-makes and
/// threes-vs-field-goals are checked the same way, for the same reason.
/// </para>
/// </summary>
public sealed record PlayerStatLine
{
    public PlayerStatLine(
        PlayerId playerId,
        TeamId teamId,
        int minutes,
        int points,
        int offensiveRebounds,
        int defensiveRebounds,
        int assists,
        int usagePercent,
        int fieldGoalsAttempted,
        int fieldGoalsMade,
        int threePointsAttempted,
        int threePointsMade,
        int freeThrowsAttempted,
        int freeThrowsMade,
        bool started)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(teamId);

        ThrowIfNegative(minutes, nameof(minutes));
        ThrowIfNegative(points, nameof(points));
        ThrowIfNegative(offensiveRebounds, nameof(offensiveRebounds));
        ThrowIfNegative(defensiveRebounds, nameof(defensiveRebounds));
        ThrowIfNegative(assists, nameof(assists));
        ThrowIfNegative(fieldGoalsAttempted, nameof(fieldGoalsAttempted));
        ThrowIfNegative(fieldGoalsMade, nameof(fieldGoalsMade));
        ThrowIfNegative(threePointsAttempted, nameof(threePointsAttempted));
        ThrowIfNegative(threePointsMade, nameof(threePointsMade));
        ThrowIfNegative(freeThrowsAttempted, nameof(freeThrowsAttempted));
        ThrowIfNegative(freeThrowsMade, nameof(freeThrowsMade));

        if (usagePercent < 0 || usagePercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(usagePercent), usagePercent, "Usage share must be between 0 and 100.");
        }

        if (fieldGoalsMade > fieldGoalsAttempted)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldGoalsMade), fieldGoalsMade, "A player cannot make more field goals than they attempted.");
        }

        if (threePointsAttempted > fieldGoalsAttempted)
        {
            throw new ArgumentOutOfRangeException(nameof(threePointsAttempted), threePointsAttempted, "Three-point attempts cannot exceed total field-goal attempts.");
        }

        if (threePointsMade > threePointsAttempted)
        {
            throw new ArgumentOutOfRangeException(nameof(threePointsMade), threePointsMade, "A player cannot make more threes than they attempted.");
        }

        if (freeThrowsMade > freeThrowsAttempted)
        {
            throw new ArgumentOutOfRangeException(nameof(freeThrowsMade), freeThrowsMade, "A player cannot make more free throws than they attempted.");
        }

        var pointsFromShooting = (2 * (fieldGoalsMade - threePointsMade)) + (3 * threePointsMade) + freeThrowsMade;
        if (points != pointsFromShooting)
        {
            throw new ArgumentException(
                $"Points ({points}) must equal 2×(FGM−3PM) + 3×3PM + FTM ({pointsFromShooting}).",
                nameof(points));
        }

        PlayerId = playerId;
        TeamId = teamId;
        Minutes = minutes;
        Points = points;
        OffensiveRebounds = offensiveRebounds;
        DefensiveRebounds = defensiveRebounds;
        Assists = assists;
        UsagePercent = usagePercent;
        FieldGoalsAttempted = fieldGoalsAttempted;
        FieldGoalsMade = fieldGoalsMade;
        ThreePointsAttempted = threePointsAttempted;
        ThreePointsMade = threePointsMade;
        FreeThrowsAttempted = freeThrowsAttempted;
        FreeThrowsMade = freeThrowsMade;
        Started = started;
    }

    public PlayerId PlayerId { get; }

    public TeamId TeamId { get; }

    public int Minutes { get; }

    public int Points { get; }

    public int OffensiveRebounds { get; }

    public int DefensiveRebounds { get; }

    /// <summary>The combined board total everything outside the rebound split still reads.</summary>
    public int Rebounds => OffensiveRebounds + DefensiveRebounds;

    public int Assists { get; }

    /// <summary>
    /// This player's share of their team's shots, 0-100. Every team's lines sum to exactly 100 —
    /// enforced by <see cref="BoxScore.Create"/>, the same way team points are enforced to sum to the
    /// final score — because a usage share that does not add up to a whole team is not usable by
    /// anything that reads it. Per game, not per lineup-stint: nothing in this engine tracks a
    /// possession's five-man unit separately from the game it belongs to.
    /// </summary>
    public int UsagePercent { get; }

    public int FieldGoalsAttempted { get; }

    public int FieldGoalsMade { get; }

    public int ThreePointsAttempted { get; }

    public int ThreePointsMade { get; }

    public int FreeThrowsAttempted { get; }

    public int FreeThrowsMade { get; }

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
    private const string UsagePercentDoesNotSumToWholeCode = "box_score.usage_percent_does_not_sum_to_whole";

    private readonly List<PlayerStatLine> _lines;

    private BoxScore(GameId gameId, TeamId homeTeamId, TeamId awayTeamId, List<PlayerStatLine> lines)
    {
        GameId = gameId;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        _lines = lines;
    }

    /// <summary>
    /// Builds a box score, refusing lines from a team that is not in the game, refusing totals that
    /// disagree with the stated result, and refusing a team whose usage shares do not sum to exactly
    /// 100 (a team with no lines at all is not checked — there is nothing to sum). The points check is
    /// the original point of the type: a game whose final score and whose player points differ is a
    /// bug the standings would inherit silently. Usage is the same shape of bug one level down.
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

        foreach (var teamId in new[] { homeTeamId, awayTeamId })
        {
            var teamLines = supplied.Where(line => line.TeamId == teamId).ToList();
            if (teamLines.Count == 0)
            {
                continue;
            }

            var usageTotal = teamLines.Sum(line => line.UsagePercent);
            if (usageTotal != 100)
            {
                errors.Add(new DomainError(
                    UsagePercentDoesNotSumToWholeCode,
                    $"Team '{teamId.Value}' in game '{gameId.Value}' has usage shares summing to {usageTotal}, not 100."));
            }
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
