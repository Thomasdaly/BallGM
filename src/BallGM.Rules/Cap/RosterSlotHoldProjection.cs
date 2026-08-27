using BallGM.Domain.Cap;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Cap;

/// <summary>
/// Charges a team for the roster spots it has not filled yet, so the room a cap sheet reports is
/// room the team can actually spend.
/// <para>
/// Without this, a team with eight players and $30m of space appears able to put all $30m on one
/// player, and only discovers afterwards that it cannot fill the roster to the league minimum. That
/// is a trap the UI would set for a human and the AI front office would walk straight into.
/// </para>
/// <para>
/// It lives in the rules layer and not beside <see cref="CapChargeProjection"/> deliberately.
/// That projection turns <em>contracts</em> into charges and needs no configuration to do it. A hold
/// has no contract behind it: its size comes from the league's compensation floor and its count from
/// the league's roster minimum, both of which are ruleset content. A projection that needs the
/// ruleset is a rules service, and putting it in Domain would mean handing Domain the ruleset.
/// </para>
/// </summary>
public static class RosterSlotHoldProjection
{
    /// <summary>
    /// One hold per unfilled roster spot, each at the compensation floor for a player with no
    /// service — the cheapest contract this league permits, which is the least the team can possibly
    /// spend to fill the spot.
    /// <para>
    /// One charge per spot rather than a single lumped figure, so the cap sheet can say how many
    /// spots are empty rather than only what they cost, and so the total is arrived at the same way
    /// every other payroll figure is: by adding up charges.
    /// </para>
    /// <para>
    /// A league with no compensation floor produces no holds at all, rather than holds of zero. The
    /// cheapest signing in such a league costs nothing that the rules can name, so there is no honest
    /// figure to reserve, and a row reading "unfilled roster spot: 0" teaches a GM nothing.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CapCharge> ForTeamSeason(
        TeamId teamId,
        Season season,
        int filledRosterSpots,
        RosterSizeLimits rosterLimits,
        CompensationFloorScale compensationFloor)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(rosterLimits);
        ArgumentNullException.ThrowIfNull(compensationFloor);

        if (filledRosterSpots < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filledRosterSpots),
                filledRosterSpots,
                "A roster cannot hold fewer than nought players.");
        }

        // Service of nought: a hold reserves what the league's cheapest available player would cost,
        // and the floor scale rises with service, so any other tier would reserve more than the team
        // is actually obliged to spend.
        var floor = compensationFloor.FloorFor(0);
        if (floor is null)
        {
            return [];
        }

        var unfilled = rosterLimits.MinimumPlayers - filledRosterSpots;
        if (unfilled <= 0)
        {
            return [];
        }

        return Enumerable
            .Range(0, unfilled)
            .Select(_ => CapCharge.RosterSlotHold(teamId, season, floor))
            .ToList();
    }
}
