using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// What happened in one fixture: the final score, and the box score behind it.
/// <para>
/// A draw is refused rather than modelled. Basketball has no drawn games, every tie-break in the
/// standings assumes a winner exists, and a postseason series that could not be won would never
/// end — so "nobody won" has to fail at the point it is recorded, not at the point something tries
/// to read a winner out of it.
/// </para>
/// </summary>
public sealed record GameResult
{
    private const string DrawnGameCode = "game_result.drawn_game";
    private const string NegativeScoreCode = "game_result.negative_score";
    private const string BoxScoreMismatchCode = "game_result.box_score_is_for_another_game";

    private GameResult(
        GameId gameId,
        SeasonDay day,
        SeasonPhase phase,
        TeamId homeTeamId,
        TeamId awayTeamId,
        int homePoints,
        int awayPoints,
        BoxScore? boxScore)
    {
        GameId = gameId;
        Day = day;
        Phase = phase;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        HomePoints = homePoints;
        AwayPoints = awayPoints;
        BoxScore = boxScore;
    }

    /// <summary>
    /// Records a result against the fixture it belongs to. <paramref name="boxScore"/> is optional
    /// because a result can legitimately be stated without one — a test injecting standings, or a
    /// league whose games are entered rather than simulated — and where it is supplied it has to be
    /// for this game.
    /// </summary>
    public static DomainOperationResult<GameResult> Create(
        Fixture fixture,
        int homePoints,
        int awayPoints,
        BoxScore? boxScore = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var errors = new List<DomainError>();

        if (homePoints < 0 || awayPoints < 0)
        {
            errors.Add(new DomainError(
                NegativeScoreCode,
                $"Game '{fixture.Id.Value}' cannot finish {homePoints}-{awayPoints}: a score cannot be negative."));
        }

        if (homePoints == awayPoints)
        {
            errors.Add(new DomainError(
                DrawnGameCode,
                $"Game '{fixture.Id.Value}' finished level at {homePoints}. Every standings tie-break and every postseason series assumes each game has a winner, so a drawn game is refused here rather than read as a winner later."));
        }

        if (boxScore is not null && boxScore.GameId != fixture.Id)
        {
            errors.Add(new DomainError(
                BoxScoreMismatchCode,
                $"The box score supplied for game '{fixture.Id.Value}' is for game '{boxScore.GameId.Value}'."));
        }

        return errors.Count > 0
            ? DomainOperationResult<GameResult>.Failure(errors.ToArray())
            : DomainOperationResult<GameResult>.Success(new GameResult(
                fixture.Id,
                fixture.Day,
                fixture.Phase,
                fixture.HomeTeamId,
                fixture.AwayTeamId,
                homePoints,
                awayPoints,
                boxScore));
    }

    public GameId GameId { get; }

    public SeasonDay Day { get; }

    public SeasonPhase Phase { get; }

    public TeamId HomeTeamId { get; }

    public TeamId AwayTeamId { get; }

    public int HomePoints { get; }

    public int AwayPoints { get; }

    public BoxScore? BoxScore { get; }

    public TeamId WinnerId => HomePoints > AwayPoints ? HomeTeamId : AwayTeamId;

    public TeamId LoserId => HomePoints > AwayPoints ? AwayTeamId : HomeTeamId;

    public bool HomeWon => HomePoints > AwayPoints;

    public int Margin => Math.Abs(HomePoints - AwayPoints);

    public int PointsFor(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        if (teamId == HomeTeamId)
        {
            return HomePoints;
        }

        if (teamId == AwayTeamId)
        {
            return AwayPoints;
        }

        throw new ArgumentException($"Team '{teamId.Value}' did not play in game '{GameId.Value}'.", nameof(teamId));
    }

    public int PointsAgainst(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        if (teamId == HomeTeamId)
        {
            return AwayPoints;
        }

        if (teamId == AwayTeamId)
        {
            return HomePoints;
        }

        throw new ArgumentException($"Team '{teamId.Value}' did not play in game '{GameId.Value}'.", nameof(teamId));
    }
}
