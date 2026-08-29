using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Simulation.Seasons;

/// <summary>
/// One team as the season engine needs to see it: who is available to play, and how many players
/// the roster holds.
/// <para>
/// <see cref="RosterCount"/> is separate from <see cref="Players"/> because they answer different
/// questions. The roster is what the league's minimum is measured against; the available list is
/// who can take the floor tonight, which is smaller whenever anybody is hurt. Collapsing them would
/// make an injured team look like a team that had released people.
/// </para>
/// </summary>
public sealed record SeasonTeam(
    TeamId TeamId,
    string TeamName,
    int RosterCount,
    IReadOnlyList<AvailablePlayer> Players);

/// <summary>
/// Everything the season engine reads about a league, gathered by the adapter rather than reached
/// for. The engine never loads anything: it is handed a league, a ruleset, and the squads, and it
/// returns what it worked out.
/// </summary>
public sealed record SeasonContext(
    Season Season,
    League League,
    LeagueRuleset Ruleset,
    IReadOnlyList<SeasonTeam> Teams)
{
    public SeasonTeam? Team(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return Teams.FirstOrDefault(team => team.TeamId == teamId);
    }

    public IReadOnlyDictionary<TeamId, string> TeamNames =>
        Teams.ToDictionary(team => team.TeamId, team => team.TeamName);
}
