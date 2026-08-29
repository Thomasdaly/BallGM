using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One scheduled game: who plays whom, where, and on which season day. Immutable — a fixture that
/// moved would invalidate every identifier derived from its coordinates.
/// <para>
/// It carries its phase because the postseason's games sit in the same schedule as the regular
/// season's rather than in a second list. Standings count only <see cref="SeasonPhase.RegularSeason"/>
/// fixtures, and a screen showing "games remaining" has to be able to tell the two apart.
/// </para>
/// </summary>
public sealed record Fixture
{
    public Fixture(GameId id, SeasonDay day, TeamId homeTeamId, TeamId awayTeamId, SeasonPhase phase)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(homeTeamId);
        ArgumentNullException.ThrowIfNull(awayTeamId);

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Phase must be a defined season phase.");
        }

        if (homeTeamId == awayTeamId)
        {
            throw new ArgumentException($"Team '{homeTeamId.Value}' cannot play itself.", nameof(awayTeamId));
        }

        Id = id;
        Day = day;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        Phase = phase;
    }

    public GameId Id { get; }

    public SeasonDay Day { get; }

    public TeamId HomeTeamId { get; }

    public TeamId AwayTeamId { get; }

    public SeasonPhase Phase { get; }

    public bool Involves(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return HomeTeamId == teamId || AwayTeamId == teamId;
    }

    public TeamId OpponentOf(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        if (HomeTeamId == teamId)
        {
            return AwayTeamId;
        }

        if (AwayTeamId == teamId)
        {
            return HomeTeamId;
        }

        throw new ArgumentException($"Team '{teamId.Value}' does not play in fixture '{Id.Value}'.", nameof(teamId));
    }
}
