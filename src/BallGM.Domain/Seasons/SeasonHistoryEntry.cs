using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One team's line in a concluded season's archived table: where it finished, and what its record
/// was. Deliberately just enough to answer "who finished where" — season and career statistics are
/// Milestone 8's, and this is not a second standings table.
/// </summary>
public sealed record SeasonHistoryTeamRecord(
    TeamId TeamId,
    int Position,
    TeamRecord Record,
    int PointsFor,
    int PointsAgainst);

/// <summary>
/// What one concluded season leaves behind: who won it, and where everyone else finished.
/// <para>
/// <see cref="ChampionTeamId"/> is <c>null</c> for a league that holds no postseason — there is no
/// champion to name, the same way an unconfigured cap threshold is absent rather than zero.
/// </para>
/// </summary>
public sealed record SeasonHistoryEntry(
    Season Season,
    TeamId? ChampionTeamId,
    IReadOnlyList<SeasonHistoryTeamRecord> FinalStandings);
