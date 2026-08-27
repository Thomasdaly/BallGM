using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Cap;

/// <summary>
/// Turns a set of contracts into the charges one team carries in one season. Deliberately the only
/// way charges are produced: a payroll figure that did not come through here has no contract behind
/// it, which is exactly the silent-mutation path the transaction ledger exists to prevent.
/// </summary>
public static class CapChargeProjection
{
    public static IReadOnlyList<CapCharge> ForTeamSeason(
        IEnumerable<Contract> contracts,
        TeamId teamId,
        Season season)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);

        return contracts
            .Where(contract => contract.TeamId == teamId)
            .Select(contract => contract.ChargeFor(season))
            .OfType<CapCharge>()
            // Dead money last, then largest first: a cap sheet reads top-down as "who is expensive",
            // and identifiers are minted per load, so ordering on them would reshuffle per launch.
            .OrderBy(charge => charge.Kind)
            .ThenByDescending(charge => charge.Amount.SmallestUnits)
            .ThenBy(charge => charge.PlayerId?.Value ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }
}
