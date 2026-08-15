using BallGM.Domain.Common;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Leagues;

/// <summary>
/// Aggregate root for league identity and league-level team membership.
/// Team roster membership is owned by the Team aggregate and referenced by identifier.
/// </summary>
public sealed class League
{
    private const string DuplicateTeamCode = "league.duplicate_team_membership";

    private readonly HashSet<TeamId> _teamIds;

    private League(LeagueId id, string name, IReadOnlyCollection<TeamId> teamIds)
    {
        Id = id;
        Name = name;
        _teamIds = new HashSet<TeamId>(teamIds);
    }

    /// <summary>
    /// Creates a league, validating structural arguments by throwing (a caller/programming
    /// error) and membership rules by returning a structured failure (a business rule that
    /// untrusted data-pack content can legitimately violate).
    /// </summary>
    public static DomainOperationResult<League> Create(LeagueId id, string name, IEnumerable<TeamId> teamIds)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(teamIds);

        var teamIdList = teamIds.ToArray();
        if (teamIdList.Any(teamId => teamId is null))
        {
            throw new ArgumentException("League team identifiers cannot contain null values.", nameof(teamIds));
        }

        if (teamIdList.Length != teamIdList.Distinct().Count())
        {
            return DomainOperationResult<League>.Failure(
                new DomainError(DuplicateTeamCode, "League team identifiers must be unique."));
        }

        return DomainOperationResult<League>.Success(new League(id, name, teamIdList));
    }

    public LeagueId Id { get; }

    public string Name { get; }

    public IReadOnlyCollection<TeamId> TeamIds => _teamIds.ToArray();
}
