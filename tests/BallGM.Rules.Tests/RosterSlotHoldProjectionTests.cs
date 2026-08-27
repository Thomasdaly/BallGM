using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

/// <summary>
/// Roster-slot holds. Without them a team eight players deep with room to spend appears able to put
/// all of it on one player, and finds out afterwards that it still has to fill the roster — a trap
/// the UI would set for a human and the AI front office would walk into.
/// </summary>
public sealed class RosterSlotHoldProjectionTests
{
    private static readonly TeamId Team = new("TEAM-1");
    private static readonly Season Season = new(2031);
    private static readonly RosterSizeLimits Limits = new(minimumPlayers: 12, maximumPlayers: 15);

    private static readonly CompensationFloorScale Floor =
        CompensationFloorScale.Create([new ScaleBand(0, 1_000_000), new ScaleBand(3, 2_000_000)]).Value;

    /// <summary>
    /// One charge per unfilled spot rather than one lumped figure, so a cap sheet can say how many
    /// spots are empty rather than only what they cost.
    /// </summary>
    [Fact]
    public void OneHoldPerUnfilledSpot()
    {
        var holds = RosterSlotHoldProjection.ForTeamSeason(Team, Season, filledRosterSpots: 9, Limits, Floor);

        Assert.Equal(3, holds.Count);
        Assert.All(holds, hold =>
        {
            Assert.True(hold.IsRosterSlotHold);
            Assert.Equal(Team, hold.TeamId);
            Assert.Equal(Season, hold.Season);
        });
    }

    /// <summary>
    /// Priced at the floor for no service at all: the cheapest contract this league permits, which is
    /// the least the team can possibly spend to fill the spot. Reserving a veteran's minimum instead
    /// would charge a team for a signing it is not obliged to make.
    /// </summary>
    [Fact]
    public void AHoldIsPricedAtTheFloorForAPlayerWithNoService()
    {
        var holds = RosterSlotHoldProjection.ForTeamSeason(Team, Season, filledRosterSpots: 11, Limits, Floor);

        Assert.Equal(1_000_000, Assert.Single(holds).Amount.SmallestUnits);
    }

    [Fact]
    public void ATeamAtOrAboveTheRosterMinimumHoldsNothing()
    {
        Assert.Empty(RosterSlotHoldProjection.ForTeamSeason(Team, Season, filledRosterSpots: 12, Limits, Floor));
        Assert.Empty(RosterSlotHoldProjection.ForTeamSeason(Team, Season, filledRosterSpots: 15, Limits, Floor));
    }

    /// <summary>
    /// The cheapest signing in a league with no minimum salary costs nothing the rules can name, so
    /// there is no honest figure to reserve. No holds at all, rather than holds of nought: a row
    /// reading "unfilled roster spot: 0" teaches a GM nothing.
    /// </summary>
    [Fact]
    public void ALeagueWithNoCompensationFloorProducesNoHoldsRatherThanHoldsOfNothing()
    {
        var holds = RosterSlotHoldProjection.ForTeamSeason(
            Team,
            Season,
            filledRosterSpots: 4,
            Limits,
            CompensationFloorScale.None);

        Assert.Empty(holds);
    }

    /// <summary>
    /// A hold has no player and no contract behind it — it is a charge for a signing that has not
    /// happened — which is exactly the case the two optional identifiers on a cap charge exist for.
    /// </summary>
    [Fact]
    public void AHoldNamesNeitherAPlayerNorAContract()
    {
        var hold = Assert.Single(RosterSlotHoldProjection.ForTeamSeason(Team, Season, filledRosterSpots: 11, Limits, Floor));

        Assert.Null(hold.PlayerId);
        Assert.Null(hold.ContractId);
        Assert.Equal(CapChargeKind.RosterSlotHold, hold.Kind);
    }
}
