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

        // No minimum is enforced here, deliberately. The roster minimum is an obligation a team has
        // to meet, not a shape a team is incapable of being in: a squad three players short is the
        // ordinary state of a team in the middle of free agency, and it is precisely the state a
        // roster-slot hold exists to price — see BallGM.Rules.Cap.RosterSlotHoldProjection. A league
        // that could not express "three spots still to fill" could not have holds at all, and the
        // cap sheet would go back to reporting room the team is not free to spend.
        //
        // The maximum stays a hard refusal, because it is a different kind of rule: a team over its
        // limit is not a team with something left to do, it is a team in a state the league forbids.

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

    /// <summary>
    /// Applies both sides of a trade at once: players leaving and players arriving, checked against
    /// the roster limits as one net result.
    /// <para>
    /// Deliberately not <see cref="RemovePlayer"/> followed by <see cref="AddPlayer"/>. A legal
    /// one-for-one trade by a team sitting on the roster minimum would fail halfway through that
    /// sequence, and a team at the maximum would fail the other ordering — the transient state is an
    /// artefact of the steps, not a rule anybody wrote. The aggregate judges where the roster ends
    /// up, and mutates nothing unless the whole movement is legal.
    /// </para>
    /// </summary>
    public DomainOperationResult ApplyTrade(
        IReadOnlyCollection<PlayerId> outgoingPlayers,
        IReadOnlyCollection<PlayerId> incomingPlayers)
    {
        ArgumentNullException.ThrowIfNull(outgoingPlayers);
        ArgumentNullException.ThrowIfNull(incomingPlayers);

        var errors = new List<DomainError>();

        foreach (var playerId in outgoingPlayers.Where(playerId => !_playerIds.Contains(playerId)))
        {
            errors.Add(new DomainError(
                MissingPlayerCode,
                $"Player '{playerId.Value}' is not on team '{Id.Value}' and cannot be traded away by it."));
        }

        foreach (var playerId in incomingPlayers.Where(playerId => _playerIds.Contains(playerId)))
        {
            errors.Add(new DomainError(
                DuplicatePlayerCode,
                $"Player '{playerId.Value}' is already on team '{Id.Value}'."));
        }

        var resulting = new HashSet<PlayerId>(_playerIds);
        resulting.ExceptWith(outgoingPlayers);
        resulting.UnionWith(incomingPlayers);

        if (resulting.Count > RosterLimits.MaximumPlayers)
        {
            errors.Add(new DomainError(
                MaximumRosterCode,
                $"The trade would leave team '{Id.Value}' with {resulting.Count} players, above the configured maximum of {RosterLimits.MaximumPlayers}."));
        }

        // A trade may not take a team below the roster minimum — but a team already below it is not
        // barred from trading, only from digging itself further in. Refusing every trade a short
        // squad tries to make would punish it for a state free agency is meant to let it fix, and
        // the reason given would be about the roster it already had rather than about this trade.
        if (resulting.Count < RosterLimits.MinimumPlayers && resulting.Count < _playerIds.Count)
        {
            errors.Add(new DomainError(
                MinimumRosterCode,
                $"The trade would leave team '{Id.Value}' with {resulting.Count} players, below the configured minimum of {RosterLimits.MinimumPlayers}."));
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult.Failure(errors.ToArray());
        }

        _playerIds.Clear();
        _playerIds.UnionWith(resulting);
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Puts the roster back exactly as it was. Used to unwind a partially applied trade, which is
    /// why it takes no view on the roster limits: the state it restores was legal when it was left.
    /// </summary>
    public void RestoreRoster(IReadOnlyCollection<PlayerId> playerIds)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        _playerIds.Clear();
        _playerIds.UnionWith(playerIds);
    }

    public DomainOperationResult RemovePlayer(PlayerId playerId) =>
        RemovePlayerCore(playerId, enforceMinimum: true);

    /// <summary>
    /// Drops a player whose contract has run its course, without enforcing the roster minimum.
    /// <para>
    /// A voluntary release through <see cref="RemovePlayer"/> is a GM's choice, and the minimum is
    /// there to stop a GM cutting a roster below what the league requires. A contract's natural
    /// expiry at a season boundary is not that choice — it is the ordinary state of a team between
    /// seasons, which <c>docs/domain-language.md</c> → "Team aggregate, on the roster minimum" is
    /// explicit about: the minimum is an obligation to meet, not an invariant the roster can never
    /// leave. The only caller is <c>BallGM.Rules.Seasons.SeasonConclusion</c>.
    /// </para>
    /// </summary>
    public DomainOperationResult ReleaseExpiredPlayer(PlayerId playerId) =>
        RemovePlayerCore(playerId, enforceMinimum: false);

    private DomainOperationResult RemovePlayerCore(PlayerId playerId, bool enforceMinimum)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        if (!_playerIds.Contains(playerId))
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    MissingPlayerCode,
                    $"Player '{playerId.Value}' is not on team '{Id.Value}'."));
        }

        if (enforceMinimum && _playerIds.Count <= RosterLimits.MinimumPlayers)
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
