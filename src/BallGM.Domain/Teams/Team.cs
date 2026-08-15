using BallGM.Domain.Common;
using BallGM.Domain.Franchises;
using BallGM.Domain.Players;

namespace BallGM.Domain.Teams;

/// <summary>
/// Aggregate root for one competitive squad's roster membership.
/// League-wide transaction, cap, and eligibility rules are validated outside this aggregate.
/// </summary>
public sealed class Team
{
    private const string DuplicatePlayerCode = "roster.player_already_on_team";
    private const string MissingPlayerCode = "roster.player_not_on_team";
    private const string MaximumRosterCode = "roster.maximum_exceeded";
    private const string MinimumRosterCode = "roster.minimum_required";

    private const string DuplicateInitialRosterCode = "roster.initial_duplicate_players";

    private readonly HashSet<PlayerId> _playerIds;

    private Team(
        TeamId id,
        FranchiseId franchiseId,
        string name,
        RosterSizeLimits rosterLimits,
        IReadOnlyCollection<PlayerId> initialPlayers)
    {
        Id = id;
        FranchiseId = franchiseId;
        Name = name;
        RosterLimits = rosterLimits;
        _playerIds = new HashSet<PlayerId>(initialPlayers);
    }

    /// <summary>
    /// Creates a team, validating structural arguments by throwing (a caller/programming
    /// error) and roster-composition rules by returning a structured failure (a business
    /// rule that untrusted data-pack content can legitimately violate).
    /// </summary>
    public static DomainOperationResult<Team> Create(
        TeamId id,
        FranchiseId franchiseId,
        string name,
        RosterSizeLimits rosterLimits,
        IEnumerable<PlayerId>? initialPlayers = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(franchiseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rosterLimits);

        var playerIdList = (initialPlayers ?? Array.Empty<PlayerId>()).ToArray();
        if (playerIdList.Any(playerId => playerId is null))
        {
            throw new ArgumentException("Initial roster cannot contain null player identifiers.", nameof(initialPlayers));
        }

        var errors = new List<DomainError>();

        if (playerIdList.Length != playerIdList.Distinct().Count())
        {
            errors.Add(new DomainError(
                DuplicateInitialRosterCode,
                "Initial roster cannot contain duplicate players."));
        }

        if (playerIdList.Length < rosterLimits.MinimumPlayers)
        {
            errors.Add(new DomainError(
                MinimumRosterCode,
                $"Initial roster must include at least {rosterLimits.MinimumPlayers} players."));
        }

        if (playerIdList.Length > rosterLimits.MaximumPlayers)
        {
            errors.Add(new DomainError(
                MaximumRosterCode,
                $"Initial roster cannot exceed {rosterLimits.MaximumPlayers} players."));
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<Team>.Failure(errors.ToArray());
        }

        return DomainOperationResult<Team>.Success(
            new Team(id, franchiseId, name, rosterLimits, playerIdList));
    }

    public TeamId Id { get; }

    public FranchiseId FranchiseId { get; }

    public string Name { get; }

    public RosterSizeLimits RosterLimits { get; }

    public int RosterCount => _playerIds.Count;

    public IReadOnlyCollection<PlayerId> PlayerIds => _playerIds.ToArray();

    public DomainOperationResult AddPlayer(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        if (_playerIds.Contains(playerId))
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    DuplicatePlayerCode,
                    $"Player '{playerId.Value}' is already on team '{Id.Value}'."));
        }

        if (_playerIds.Count >= RosterLimits.MaximumPlayers)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    MaximumRosterCode,
                    $"Team '{Id.Value}' cannot exceed the configured roster maximum of {RosterLimits.MaximumPlayers} players."));
        }

        _playerIds.Add(playerId);
        return DomainOperationResult.Success;
    }

    public DomainOperationResult RemovePlayer(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        if (!_playerIds.Contains(playerId))
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    MissingPlayerCode,
                    $"Player '{playerId.Value}' is not on team '{Id.Value}'."));
        }

        if (_playerIds.Count <= RosterLimits.MinimumPlayers)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    MinimumRosterCode,
                    $"Team '{Id.Value}' cannot fall below the configured roster minimum of {RosterLimits.MinimumPlayers} players."));
        }

        _playerIds.Remove(playerId);
        return DomainOperationResult.Success;
    }
}
