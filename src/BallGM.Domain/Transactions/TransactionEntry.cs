using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Transactions;

/// <summary>
/// What kind of state change an entry records. Trades arrive with the trade engine (Milestone 5);
/// the kinds here are the cap-affecting and draft-asset events the game can currently produce.
/// Draft-asset history goes in this ledger rather than a second one — an asset trail kept apart
/// from the money trail is two accounts of the same trade that can disagree.
/// </summary>
public enum TransactionKind
{
    ContractSigned = 0,
    PlayerReleased = 1,
    OptionExercised = 2,
    OptionDeclined = 3,

    /// <summary>A pick changed hands.</summary>
    DraftPickTransferred = 4,

    /// <summary>A protection or a swap right was attached to a pick.</summary>
    DraftPickEncumbered = 5,

    /// <summary>A protected obligation came due and the pick went to the franchise it was owed to.</summary>
    DraftPickConveyed = 6,

    /// <summary>A protection held, so the obligation moved to the following draft.</summary>
    DraftPickRolledOver = 7,

    /// <summary>A protection schedule ran out under a converting fallback.</summary>
    DraftPickConverted = 8,

    /// <summary>A protection schedule ran out under an extinguishing fallback; the obligation is gone.</summary>
    DraftPickExtinguished = 9,

    /// <summary>A swap right was worth taking, and the two selections changed places.</summary>
    SwapRightExercised = 10,

    /// <summary>A swap right was not worth taking. The right is spent either way.</summary>
    SwapRightDeclined = 11,

    /// <summary>A player changed teams in a trade. Recorded against both teams, from each one's side.</summary>
    PlayerTraded = 12,
}

public sealed record TransactionId
{
    public TransactionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// One immutable line in the transaction ledger: what happened, to whom, for how much, when it was
/// recorded, and why. <see cref="Sequence"/> — not the timestamp and not the identifier — is the
/// ordering key, so entries recorded inside the same clock tick still read back in the order they
/// happened.
/// <para>
/// An entry names a team, a franchise, or both. Cap events belong to a team — the squad whose
/// payroll moved — while draft assets belong to a franchise, because a pick four drafts out
/// outlives any one season's squad. An entry naming neither is a caller error and throws.
/// </para>
/// </summary>
public sealed record TransactionEntry
{
    public TransactionEntry(
        TransactionId id,
        long sequence,
        DateTimeOffset recordedAt,
        TransactionKind kind,
        Season season,
        TeamId? teamId,
        PlayerId? playerId,
        ContractId? contractId,
        Money? amount,
        string reason,
        FranchiseId? franchiseId = null,
        FranchiseId? counterpartyFranchiseId = null,
        DraftPickId? draftPickId = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (teamId is null && franchiseId is null)
        {
            throw new ArgumentException(
                "A ledger entry must name the team or the franchise it happened to.",
                nameof(teamId));
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Ledger sequence cannot be negative.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Transaction kind must be a defined value.");
        }

        Id = id;
        Sequence = sequence;
        RecordedAt = recordedAt;
        Kind = kind;
        Season = season;
        TeamId = teamId;
        PlayerId = playerId;
        ContractId = contractId;
        Amount = amount;
        Reason = reason;
        FranchiseId = franchiseId;
        CounterpartyFranchiseId = counterpartyFranchiseId;
        DraftPickId = draftPickId;
    }

    public TransactionId Id { get; }

    public long Sequence { get; }

    public DateTimeOffset RecordedAt { get; }

    public TransactionKind Kind { get; }

    public Season Season { get; }

    public TeamId? TeamId { get; }

    public PlayerId? PlayerId { get; }

    public ContractId? ContractId { get; }

    /// <summary>The money the event moved, where the event moved money.</summary>
    public Money? Amount { get; }

    public string Reason { get; }

    /// <summary>The organisation the event happened to, on events that outlive a season's squad.</summary>
    public FranchiseId? FranchiseId { get; }

    /// <summary>The franchise on the other side of it — who the pick went to, or who it came from.</summary>
    public FranchiseId? CounterpartyFranchiseId { get; }

    public DraftPickId? DraftPickId { get; }
}
