using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Cap;

/// <summary>Why an amount is on a team's books for a season.</summary>
public enum CapChargeKind
{
    /// <summary>A live contract's compensation for the season.</summary>
    ActiveContract = 0,

    /// <summary>
    /// Guaranteed money still owed to a released player. The contract behind it is terminated —
    /// this is the case that makes a cap charge a concept of its own rather than a contract field.
    /// </summary>
    DeadMoney = 1,
}

/// <summary>
/// One amount charged to one team for one season, with the reason attached. A derived value, not an
/// entity: charges are projected from contracts (see <see cref="Contract.ChargeFor"/>) rather than
/// stored and mutated, so they cannot drift out of step with the agreements behind them.
/// </summary>
public sealed record CapCharge
{
    private CapCharge(
        TeamId teamId,
        Season season,
        PlayerId playerId,
        ContractId contractId,
        CapChargeKind kind,
        Money amount)
    {
        TeamId = teamId;
        Season = season;
        PlayerId = playerId;
        ContractId = contractId;
        Kind = kind;
        Amount = amount;
    }

    public TeamId TeamId { get; }

    public Season Season { get; }

    public PlayerId PlayerId { get; }

    /// <summary>The contract the charge came from — terminated, in the dead-money case.</summary>
    public ContractId ContractId { get; }

    public CapChargeKind Kind { get; }

    public Money Amount { get; }

    public bool IsDeadMoney => Kind == CapChargeKind.DeadMoney;

    public static CapCharge ActiveContract(
        TeamId teamId,
        Season season,
        PlayerId playerId,
        ContractId contractId,
        Money amount) =>
        Create(teamId, season, playerId, contractId, CapChargeKind.ActiveContract, amount);

    public static CapCharge DeadMoney(
        TeamId teamId,
        Season season,
        PlayerId playerId,
        ContractId contractId,
        Money amount) =>
        Create(teamId, season, playerId, contractId, CapChargeKind.DeadMoney, amount);

    private static CapCharge Create(
        TeamId teamId,
        Season season,
        PlayerId playerId,
        ContractId contractId,
        CapChargeKind kind,
        Money amount)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(contractId);
        ArgumentNullException.ThrowIfNull(amount);

        return new CapCharge(teamId, season, playerId, contractId, kind, amount);
    }
}
