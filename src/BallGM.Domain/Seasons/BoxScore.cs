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
        int fieldGoalsAttempted,
        int fieldGoalsMade,
        int threePointsAttempted,
        int threePointsMade,
        int freeThrowsAttempted,
        int freeThrowsMade,
        bool started,
        int plusMinus,
        int steals,
        int turnovers,
        int blocks)
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
        ThrowIfNegative(steals, nameof(steals));
        ThrowIfNegative(blocks, nameof(blocks));
        ThrowIfNegative(turnovers, nameof(turnovers));

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

        if (threePointsMade > fieldGoalsMade)
        {
            throw new ArgumentOutOfRangeException(nameof(threePointsMade), threePointsMade, "A made three is a made field goal, so threes made cannot exceed field goals made.");
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
        FieldGoalsAttempted = fieldGoalsAttempted;
        FieldGoalsMade = fieldGoalsMade;
        ThreePointsAttempted = threePointsAttempted;
        ThreePointsMade = threePointsMade;
        FreeThrowsAttempted = freeThrowsAttempted;
        FreeThrowsMade = freeThrowsMade;
        Started = started;
        PlusMinus = plusMinus;
        Steals = steals;
        Blocks = blocks;
        Turnovers = turnovers;
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

    public int FieldGoalsAttempted { get; }

    public int FieldGoalsMade { get; }

    public int ThreePointsAttempted { get; }

    public int ThreePointsMade { get; }

    public int FreeThrowsAttempted { get; }

    public int FreeThrowsMade { get; }

    public bool Started { get; }

    /// <summary>
    /// The score difference while this player was on the floor: every point their side scored with them
    /// on it minus every point it allowed. Signed, so never checked for negativity; a team's lines sum
    /// to five times its margin, which <see cref="BoxScore.Create"/> enforces.
    /// </summary>
    public int PlusMinus { get; }

    /// <summary>Live-ball turnovers this player forced. A side's steals are never more than the other side's turnovers.</summary>
    public int Steals { get; }

    /// <summary>Possessions this player lost without a shot.</summary>
    public int Turnovers { get; }

    /// <summary>Opponent shots this player blocked. A side's blocks are never more than the other side's missed field goals.</summary>
    public int Blocks { get; }

    private static void ThrowIfNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A box-score figure cannot be negative.");
        }
    }
}

/// <summary>
/// What one team did in a game that belongs to no single player: rebounds credited to the team (a
/// ball out of bounds off the other side, a blocked shot that leaves the floor) and the free-throw
/// trips that ended in a live miss. Neither is a player's figure, yet without them the rebounds in a
/// box score cannot be reconciled with its misses — every live miss ends in exactly one rebound, and
/// a box score that drops some has silently lost part of the game.
/// </summary>
public sealed record TeamStatLine
{
    public TeamStatLine(TeamId teamId, int teamOffensiveRebounds, int teamDefensiveRebounds, int liveFreeThrowMisses)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentOutOfRangeException.ThrowIfNegative(teamOffensiveRebounds);
        ArgumentOutOfRangeException.ThrowIfNegative(teamDefensiveRebounds);
        ArgumentOutOfRangeException.ThrowIfNegative(liveFreeThrowMisses);

        TeamId = teamId;
        TeamOffensiveRebounds = teamOffensiveRebounds;
        TeamDefensiveRebounds = teamDefensiveRebounds;
        LiveFreeThrowMisses = liveFreeThrowMisses;
    }

    public TeamId TeamId { get; }

    /// <summary>The team's own misses it kept without a player securing the ball.</summary>
    public int TeamOffensiveRebounds { get; }

    /// <summary>The opponent's misses this team took possession of without a player securing the ball.</summary>
    public int TeamDefensiveRebounds { get; }

    /// <summary>Free-throw trips (including and-ones) whose last shot was missed and stayed live.</summary>
    public int LiveFreeThrowMisses { get; }

    public int TeamRebounds => TeamOffensiveRebounds + TeamDefensiveRebounds;
}

/// <summary>
/// Every player line from one game, plus each side's <see cref="TeamStatLine"/>. Team totals are not
/// stored: a box score whose totals could disagree with its lines is two accounts of one game, and
/// the derived answer is the one that cannot drift.
/// <para>
/// <b>The box-score identities are checked here, at construction,</b> not measured afterwards:
/// points add up to the result; usage shares add up to a whole team; every live miss is accounted for
/// by exactly one rebound (<c>(FGA − FGM) + live FT misses = own OREB + own team OREB + opponent DREB +
/// opponent team DREB</c>); and an assist is always on a teammate's basket (<c>AST ≤ team FGM − own FGM</c>).
/// A game that breaks one of these is a bug the season would otherwise inherit silently.
/// </para>
/// </summary>
public sealed class BoxScore
{
    private const string PointsDoNotMatchCode = "box_score.points_do_not_match_result";
    private const string UnknownTeamCode = "box_score.line_for_team_not_playing";
    private const string ReboundsDoNotAccountForMissesCode = "box_score.rebounds_do_not_account_for_misses";
    private const string AssistsExceedTeammateMakesCode = "box_score.assists_exceed_teammate_makes";
    private const string TeamLineForWrongTeamCode = "box_score.team_line_for_wrong_team";
    private const string MinutesDoNotFillTheGameCode = "box_score.minutes_do_not_fill_the_game";
    private const string PlusMinusDoesNotMatchMarginCode = "box_score.plus_minus_does_not_match_margin";
    private const string NegativeOvertimeCode = "box_score.negative_overtime";
    private const string StealsExceedTurnoversCode = "box_score.steals_exceed_opponent_turnovers";
    private const string BlocksExceedMissesCode = "box_score.blocks_exceed_opponent_misses";

    /// <summary>The players on the floor for a side at once; a side's minutes and plus-minus are counted in fives.</summary>
    public const int PlayersOnFloor = 5;

    /// <summary>Minutes in regulation, and in each overtime period.</summary>
    public const int RegulationMinutes = 48;

    /// <inheritdoc cref="RegulationMinutes"/>
    public const int OvertimeMinutes = 5;

    /// <summary>
    /// The minutes a side's lines must add up to: the game's length for each player on the floor —
    /// five, or everyone on a side short-handed below five. The engine credits minutes to this total
    /// and <see cref="Create"/> refuses anything else, from the one formula.
    /// </summary>
    public static int GameMinutes(int lines, int overtimePeriods) =>
        Math.Min(lines, PlayersOnFloor) * (RegulationMinutes + (OvertimeMinutes * Math.Max(0, overtimePeriods)));

    private readonly List<PlayerStatLine> _lines;

    private BoxScore(GameId gameId, TeamStatLine home, TeamStatLine away, List<PlayerStatLine> lines, int overtimePeriods)
    {
        GameId = gameId;
        OvertimePeriods = overtimePeriods;
        HomeTeamId = home.TeamId;
        AwayTeamId = away.TeamId;
        Home = home;
        Away = away;
        _lines = lines;
    }

    /// <summary>
    /// Builds a box score, refusing anything that breaks a box-score identity (see the type summary).
    /// A team with no player lines is not checked for usage — there is nothing to sum.
    /// </summary>
    public static DomainOperationResult<BoxScore> Create(
        GameId gameId,
        int homePoints,
        int awayPoints,
        IEnumerable<PlayerStatLine> lines,
        TeamStatLine home,
        TeamStatLine away,
        int overtimePeriods)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(away);

        var supplied = lines.ToList();
        if (supplied.Any(line => line is null))
        {
            throw new ArgumentException("A box score cannot contain null player lines.", nameof(lines));
        }

        var errors = new List<DomainError>();

        if (overtimePeriods < 0)
        {
            errors.Add(new DomainError(NegativeOvertimeCode, $"Game '{gameId.Value}' cannot have {overtimePeriods} overtime periods."));
        }

        if (home.TeamId == away.TeamId)
        {
            errors.Add(new DomainError(TeamLineForWrongTeamCode, $"Game '{gameId.Value}' names team '{home.TeamId.Value}' on both sides."));
        }

        // One pass over the lines builds both teams' totals; every identity below reads them.
        var totals = new TeamTotals[2];
        foreach (var line in supplied)
        {
            var side = line.TeamId == home.TeamId ? 0 : line.TeamId == away.TeamId ? 1 : -1;
            if (side < 0)
            {
                errors.Add(new DomainError(
                    UnknownTeamCode,
                    $"Player '{line.PlayerId.Value}' has a line in game '{gameId.Value}' for team '{line.TeamId.Value}', which is not playing in it."));
                continue;
            }

            totals[side].Add(line);
        }

        if (totals[0].Points != homePoints || totals[1].Points != awayPoints)
        {
            errors.Add(new DomainError(
                PointsDoNotMatchCode,
                $"Game '{gameId.Value}' finished {homePoints}-{awayPoints} but its player lines add up to {totals[0].Points}-{totals[1].Points}."));
        }

        TeamStatLine[] teams = [home, away];
        for (var side = 0; side < 2; side++)
        {
            var team = teams[side];
            var opponent = teams[1 - side];
            var own = totals[side];

            // Every minute and every point is shared by the players on the floor — five, or everyone on
            // a side short-handed below five — so a side's minutes are the game's length times that
            // many, and its plus-minus that many times its margin.
            if (own.Lines > 0)
            {
                var onFloor = Math.Min(own.Lines, PlayersOnFloor);
                var gameMinutes = GameMinutes(own.Lines, overtimePeriods);
                if (own.Minutes != gameMinutes)
                {
                    errors.Add(new DomainError(
                        MinutesDoNotFillTheGameCode,
                        $"Team '{team.TeamId.Value}' in game '{gameId.Value}' has {own.Minutes} minutes; a game with {overtimePeriods} overtime periods has {gameMinutes}."));
                }

                var margin = own.Points - totals[1 - side].Points;
                if (own.PlusMinus != onFloor * margin)
                {
                    errors.Add(new DomainError(
                        PlusMinusDoesNotMatchMarginCode,
                        $"Team '{team.TeamId.Value}' in game '{gameId.Value}' has plus-minus summing to {own.PlusMinus}, not {onFloor} times its margin of {margin}."));
                }
            }

            // A steal is the other side's turnover, so a side cannot steal the ball more often than
            // the other side lost it.
            if (own.Steals > totals[1 - side].Turnovers)
            {
                errors.Add(new DomainError(
                    StealsExceedTurnoversCode,
                    $"Team '{team.TeamId.Value}' in game '{gameId.Value}' has {own.Steals} steals, more than the {totals[1 - side].Turnovers} turnovers its opponent committed."));
            }

            // A block is an opponent's missed field goal, so a side cannot block more shots than the
            // other side missed.
            var opponentMisses = totals[1 - side].FieldGoalsMissed;
            if (own.Blocks > opponentMisses)
            {
                errors.Add(new DomainError(
                    BlocksExceedMissesCode,
                    $"Team '{team.TeamId.Value}' in game '{gameId.Value}' has {own.Blocks} blocks, more than the {opponentMisses} field goals its opponent missed."));
            }

            var liveMisses = own.FieldGoalsMissed + team.LiveFreeThrowMisses;
            var rebounds = own.OffensiveRebounds + team.TeamOffensiveRebounds + totals[1 - side].DefensiveRebounds + opponent.TeamDefensiveRebounds;
            if (liveMisses != rebounds)
            {
                errors.Add(new DomainError(
                    ReboundsDoNotAccountForMissesCode,
                    $"Team '{team.TeamId.Value}' in game '{gameId.Value}' had {liveMisses} live misses but {rebounds} rebounds were recorded off them."));
            }
        }

        foreach (var line in supplied)
        {
            var side = line.TeamId == home.TeamId ? 0 : line.TeamId == away.TeamId ? 1 : -1;
            if (side >= 0 && line.Assists > totals[side].FieldGoalsMade - line.FieldGoalsMade)
            {
                errors.Add(new DomainError(
                    AssistsExceedTeammateMakesCode,
                    $"Player '{line.PlayerId.Value}' has {line.Assists} assists in game '{gameId.Value}', more than the {totals[side].FieldGoalsMade - line.FieldGoalsMade} baskets made by teammates."));
            }
        }

        return errors.Count > 0
            ? DomainOperationResult<BoxScore>.Failure(errors.ToArray())
            : DomainOperationResult<BoxScore>.Success(new BoxScore(gameId, home, away, supplied, overtimePeriods));
    }

    public GameId GameId { get; }

    public TeamId HomeTeamId { get; }

    public TeamId AwayTeamId { get; }

    public int OvertimePeriods { get; }

    public TeamStatLine Home { get; }

    public TeamStatLine Away { get; }

    public IReadOnlyList<PlayerStatLine> Lines => _lines;

    public TeamStatLine TeamLineFor(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return teamId == HomeTeamId ? Home : teamId == AwayTeamId ? Away
            : throw new ArgumentException($"Team '{teamId.Value}' did not play in game '{GameId.Value}'.", nameof(teamId));
    }

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

    /// <summary>
    /// One team's counting totals for this game, as a box score's team row reads: one game, however
    /// many players took part; every player line plus the team rebounds no player secured, so a
    /// team's rebounds account for every miss it took back (and the rates built on them — rebound
    /// shares, possessions, ratings, the four factors — count every board); and its plus-minus is its
    /// margin (the players' plus-minus sums to that times the five on the floor).
    /// </summary>
    public CountingStats TotalsFor(TeamId teamId)
    {
        var team = TeamLineFor(teamId);
        var opponent = teamId == HomeTeamId ? AwayTeamId : HomeTeamId;
        var own = CountingStats.Sum(_lines.Where(line => line.TeamId == teamId));
        return own with
        {
            Games = 1,
            Starts = 0,
            OffensiveRebounds = own.OffensiveRebounds + team.TeamOffensiveRebounds,
            DefensiveRebounds = own.DefensiveRebounds + team.TeamDefensiveRebounds,
            PlusMinus = own.Points - PointsFor(opponent),
        };
    }

    public int PointsFor(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _lines.Where(line => line.TeamId == teamId).Sum(line => line.Points);
    }

    private struct TeamTotals
    {
        public int Lines;
        public int Points;
        public int Minutes;
        public int PlusMinus;
        public int Steals;
        public int Blocks;

        public readonly int FieldGoalsMissed => FieldGoalsAttempted - FieldGoalsMade;
        public int Turnovers;
        public int FieldGoalsAttempted;
        public int FieldGoalsMade;
        public int OffensiveRebounds;
        public int DefensiveRebounds;

        public void Add(PlayerStatLine line)
        {
            Lines++;
            Points += line.Points;
            Minutes += line.Minutes;
            PlusMinus += line.PlusMinus;
            Steals += line.Steals;
            Blocks += line.Blocks;
            Turnovers += line.Turnovers;
            FieldGoalsAttempted += line.FieldGoalsAttempted;
            FieldGoalsMade += line.FieldGoalsMade;
            OffensiveRebounds += line.OffensiveRebounds;
            DefensiveRebounds += line.DefensiveRebounds;
        }
    }
}
