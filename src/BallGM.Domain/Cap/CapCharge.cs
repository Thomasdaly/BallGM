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

    /// <summary>
    /// A placeholder for an unfilled roster spot, so a team cannot count room it will have to spend
    /// filling the roster to its minimum. The only kind with neither a player nor a contract behind
    /// it: it is a charge for a player who does not exist yet.
    /// </summary>
    RosterSlotHold = 2,
}

/// <summary>
/// One amount charged to one team for one season, with the reason attached. A derived value, not an
/// entity: charges are projected from contracts (see <see cref="Contract.ChargeFor"/>) rather than
/// stored and mutated, so they cannot drift out of step with the agreements behind them.
/// <para>
/// <see cref="PlayerId"/> and <see cref="ContractId"/> are nullable because
/// <see cref="CapChargeKind.RosterSlotHold"/> has neither. They are optional per kind rather than
/// optional in general: the factories below require both for the two kinds that have them and
/// forbid both for the kind that does not, so "nullable" never means "unchecked". Holds live on the
/// same charge type rather than in a sibling collection so that a payroll is one sum — a hold that
/// has to be added in three separate places is a hold one of them will eventually forget.
/// </para>
/// </summary>
public sealed record CapCharge
{
    private CapCharge(
        TeamId teamId,
        Season season,
        PlayerId? playerId,
        ContractId? contractId,
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

    /// <summary>The player the charge is for, or <c>null</c> for a roster-slot hold.</summary>
    public PlayerId? PlayerId { get; }

    /// <summary>
    /// The contract the charge came from — terminated, in the dead-money case, and <c>null</c> for a
    /// roster-slot hold, which has no agreement behind it.
    /// </summary>
    public ContractId? ContractId { get; }

    public CapChargeKind Kind { get; }

    public Money Amount { get; }

    public bool IsDeadMoney => Kind == CapChargeKind.DeadMoney;

    public bool IsRosterSlotHold => Kind == CapChargeKind.RosterSlotHold;

    public static CapCharge ActiveContract(
        TeamId teamId,
        Season season,
        PlayerId playerId,
        ContractId contractId,
        Money amount)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(contractId);

        return Create(teamId, season, playerId, contractId, CapChargeKind.ActiveContract, amount);
    }

    public static CapCharge DeadMoney(
        TeamId teamId,
        Season season,
        PlayerId playerId,
        ContractId contractId,
        Money amount)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(contractId);

        return Create(teamId, season, playerId, contractId, CapChargeKind.DeadMoney, amount);
    }

    /// <summary>
    /// A charge for an unfilled roster spot. Takes no player and no contract because it has neither:
    /// this is the case the two nullable identifiers exist for.
    /// </summary>
    public static CapCharge RosterSlotHold(TeamId teamId, Season season, Money amount) =>
        Create(teamId, season, playerId: null, contractId: null, CapChargeKind.RosterSlotHold, amount);

    private static CapCharge Create(
        TeamId teamId,
        Season season,
        PlayerId? playerId,
        ContractId? contractId,
        CapChargeKind kind,
        Money amount)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(amount);

        return new CapCharge(teamId, season, playerId, contractId, kind, amount);
    }
}
