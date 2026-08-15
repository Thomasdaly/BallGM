using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;

namespace BallGM.Domain.Trades;

public sealed record TradeId
{
    public TradeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum TradeAssetKind
{
    Player = 0,
    DraftPick = 1,
}

/// <summary>
/// One asset going one way. Every movement names both ends, which is what makes a three-team trade
/// nothing more than a longer list: there is no "the other side" to be ambiguous about.
/// </summary>
public sealed record TradeAssetMovement
{
    private TradeAssetMovement(
        TradeAssetKind kind,
        PlayerId? playerId,
        DraftPickId? draftPickId,
        TeamId fromTeamId,
        TeamId toTeamId)
    {
        Kind = kind;
        PlayerId = playerId;
        DraftPickId = draftPickId;
        FromTeamId = fromTeamId;
        ToTeamId = toTeamId;
    }

    public TradeAssetKind Kind { get; }

    public PlayerId? PlayerId { get; }

    public DraftPickId? DraftPickId { get; }

    public TeamId FromTeamId { get; }

    public TeamId ToTeamId { get; }

    /// <summary>Identifies the asset regardless of kind, so a proposal can spot the same thing sent twice.</summary>
    public string AssetKey => Kind == TradeAssetKind.Player
        ? $"player:{PlayerId!.Value}"
        : $"pick:{DraftPickId!.Value}";

    public static TradeAssetMovement Player(PlayerId playerId, TeamId fromTeamId, TeamId toTeamId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(fromTeamId);
        ArgumentNullException.ThrowIfNull(toTeamId);

        return new TradeAssetMovement(TradeAssetKind.Player, playerId, null, fromTeamId, toTeamId);
    }

    public static TradeAssetMovement DraftPick(DraftPickId draftPickId, TeamId fromTeamId, TeamId toTeamId)
    {
        ArgumentNullException.ThrowIfNull(draftPickId);
        ArgumentNullException.ThrowIfNull(fromTeamId);
        ArgumentNullException.ThrowIfNull(toTeamId);

        return new TradeAssetMovement(TradeAssetKind.DraftPick, null, draftPickId, fromTeamId, toTeamId);
    }
}

/// <summary>
/// How much of the league's history the proposal was built against — the ledger's length at the
/// moment it was assembled.
/// <para>
/// Every state change worth knowing about leaves a ledger entry, so a proposal whose token no longer
/// matches was assembled against a league that has since moved: a player in it may already have been
/// traded, a pick may already have conveyed. Cheaper and more honest than hashing the world, and it
/// is the reason the ledger is append-only.
/// </para>
/// </summary>
public sealed record LeagueStateToken(long LedgerSequence)
{
    public static LeagueStateToken From(TransactionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return new LeagueStateToken(ledger.Count);
    }
}

/// <summary>
/// A proposed exchange, before anybody has decided whether it is legal. Structurally coherent by
/// construction — real teams, assets that move between participants, nothing sent twice — and
/// carrying no judgement about the rules, which is <c>BallGM.Rules.Trades.TradeValidator</c>'s job.
/// </summary>
public sealed class TradeProposal
{
    private const string TooFewParticipantsCode = "trade.too_few_participants";
    private const string DuplicateParticipantCode = "trade.duplicate_participant";
    private const string NoAssetsCode = "trade.no_assets";
    private const string SelfMovementCode = "trade.asset_sent_to_itself";
    private const string UnknownParticipantCode = "trade.movement_outside_participants";
    private const string DuplicateAssetCode = "trade.asset_moved_twice";
    private const string IdleParticipantCode = "trade.participant_sends_and_receives_nothing";

    private readonly List<TeamId> _participants;
    private readonly List<TradeAssetMovement> _movements;

    private TradeProposal(
        TradeId id,
        Season season,
        List<TeamId> participants,
        List<TradeAssetMovement> movements,
        LeagueStateToken stateToken)
    {
        Id = id;
        Season = season;
        _participants = participants;
        _movements = movements;
        StateToken = stateToken;
    }

    public static DomainOperationResult<TradeProposal> Create(
        TradeId id,
        Season season,
        IEnumerable<TeamId> participants,
        IEnumerable<TradeAssetMovement> movements,
        LeagueStateToken stateToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(stateToken);

        var participantList = participants.ToList();
        var movementList = movements.ToList();

        if (participantList.Any(team => team is null) || movementList.Any(movement => movement is null))
        {
            throw new ArgumentException("A trade proposal cannot contain null participants or movements.", nameof(participants));
        }

        var errors = new List<DomainError>();

        if (participantList.Count < 2)
        {
            errors.Add(new DomainError(
                TooFewParticipantsCode,
                "A trade needs at least two teams. A team cannot trade with itself."));
        }

        if (participantList.Count != participantList.Distinct().Count())
        {
            errors.Add(new DomainError(
                DuplicateParticipantCode,
                "A team cannot appear twice in the same trade."));
        }

        if (movementList.Count == 0)
        {
            errors.Add(new DomainError(NoAssetsCode, "A trade must move at least one asset."));
        }

        var participantSet = participantList.ToHashSet();

        foreach (var movement in movementList)
        {
            if (movement.FromTeamId == movement.ToTeamId)
            {
                errors.Add(new DomainError(
                    SelfMovementCode,
                    $"An asset cannot be sent from team '{movement.FromTeamId.Value}' to itself."));
            }

            if (!participantSet.Contains(movement.FromTeamId) || !participantSet.Contains(movement.ToTeamId))
            {
                errors.Add(new DomainError(
                    UnknownParticipantCode,
                    $"An asset moves between teams that are not both participants in this trade ('{movement.FromTeamId.Value}' to '{movement.ToTeamId.Value}')."));
            }
        }

        var duplicateAssets = movementList
            .GroupBy(movement => movement.AssetKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var assetKey in duplicateAssets)
        {
            errors.Add(new DomainError(
                DuplicateAssetCode,
                $"Asset '{assetKey}' is sent more than once in the same trade."));
        }

        // A participant that neither sends nor receives is not in the trade — it is a team somebody
        // put in the form by accident, and letting it through would make the rule checks below run
        // against a team with nothing at stake.
        foreach (var participant in participantList.Where(participant =>
                     movementList.All(movement => movement.FromTeamId != participant && movement.ToTeamId != participant)))
        {
            errors.Add(new DomainError(
                IdleParticipantCode,
                $"Team '{participant.Value}' takes part in this trade without sending or receiving anything."));
        }

        return errors.Count > 0
            ? DomainOperationResult<TradeProposal>.Failure(errors.ToArray())
            : DomainOperationResult<TradeProposal>.Success(
                new TradeProposal(id, season, participantList, movementList, stateToken));
    }

    public TradeId Id { get; }

    public Season Season { get; }

    public IReadOnlyList<TeamId> Participants => _participants;

    public IReadOnlyList<TradeAssetMovement> Movements => _movements;

    /// <summary>The league's ledger length when this proposal was assembled. See <see cref="LeagueStateToken"/>.</summary>
    public LeagueStateToken StateToken { get; }

    public IReadOnlyList<TradeAssetMovement> SentBy(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _movements.Where(movement => movement.FromTeamId == teamId).ToList();
    }

    public IReadOnlyList<TradeAssetMovement> ReceivedBy(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _movements.Where(movement => movement.ToTeamId == teamId).ToList();
    }

    /// <summary>Whether the league has moved on since this proposal was assembled.</summary>
    public bool IsStaleAgainst(TransactionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return LeagueStateToken.From(ledger) != StateToken;
    }
}
