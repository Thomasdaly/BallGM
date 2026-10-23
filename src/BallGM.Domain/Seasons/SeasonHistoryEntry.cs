using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
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
    IReadOnlyList<SeasonHistoryTeamRecord> FinalStandings)
{
    /// <summary>
    /// The awards this season handed out, in the order the league's rules list them (an all-league
    /// award's selections in team order). Empty for a league that hands out none, and for any season
    /// archived before awards were decided at the season boundary.
    /// </summary>
    public IReadOnlyList<SeasonAward> Awards { get; init; } = [];
}

/// <summary>
/// One award selection in a concluded season: the award, which selection this is (1 for a single
/// award; for an all-league award, the team it names — first, second, third), the player and the
/// team he finished the season with, the score that decided it, and the explanation a reader is shown.
/// </summary>
public sealed record SeasonAward(
    string AwardCode,
    string AwardName,
    int Selection,
    PlayerId PlayerId,
    TeamId? TeamId,
    int ScoreHundredths,
    string Explanation);
