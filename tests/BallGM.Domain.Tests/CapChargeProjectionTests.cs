using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class CapChargeProjectionTests
{
    private static readonly Season Season2031 = new(2031);

    [Fact]
    public void Projection_TakesOnlyTheChargesBelongingToTheTeamAndSeasonAsked()
    {
        var team = new TeamId(SortableId.NewId());
        var otherTeam = new TeamId(SortableId.NewId());

        var contracts = new[]
        {
            Contract(team, 2031, 20_000_000),
            Contract(team, 2031, 12_000_000),
            Contract(otherTeam, 2031, 30_000_000),
            Contract(team, 2029, 25_000_000),
        };

        var charges = CapChargeProjection.ForTeamSeason(contracts, team, Season2031);

        Assert.Equal(2, charges.Count);
        Assert.Equal(32_000_000, charges.Sum(charge => charge.Amount.SmallestUnits));
        Assert.All(charges, charge => Assert.Equal(team, charge.TeamId));
        Assert.All(charges, charge => Assert.Equal(Season2031, charge.Season));
    }

    [Fact]
    public void Projection_ListsLiveContractsBeforeDeadMoneyAndTheExpensiveFirst()
    {
        var team = new TeamId(SortableId.NewId());
        var released = Contract(team, 2031, 9_000_000);
        Assert.True(released.Terminate(Season2031).IsSuccess);

        var contracts = new[]
        {
            Contract(team, 2031, 12_000_000),
            released,
            Contract(team, 2031, 20_000_000),
        };

        var charges = CapChargeProjection.ForTeamSeason(contracts, team, Season2031);

        Assert.Equal(
            [20_000_000, 12_000_000, 9_000_000],
            charges.Select(charge => charge.Amount.SmallestUnits));
        Assert.Equal([false, false, true], charges.Select(charge => charge.IsDeadMoney));
    }

    [Fact]
    public void Projection_KeepsDeadMoneyOnTheBooksOfTheTeamThatOwesIt()
    {
        var team = new TeamId(SortableId.NewId());
        var released = Contract(team, 2031, 9_000_000);
        Assert.True(released.Terminate(Season2031).IsSuccess);

        var charge = Assert.Single(CapChargeProjection.ForTeamSeason([released], team, Season2031));

        Assert.True(charge.IsDeadMoney);
        Assert.Equal(released.Id, charge.ContractId);
        Assert.Equal(released.PlayerId, charge.PlayerId);
    }

    [Fact]
    public void Projection_ReturnsNothingForATeamWithNoContracts()
    {
        var team = new TeamId(SortableId.NewId());

        Assert.Empty(CapChargeProjection.ForTeamSeason([], team, Season2031));
    }

    private static Contract Contract(TeamId teamId, int year, long compensation)
    {
        var result = Domain.Contracts.Contract.Create(
            new ContractId(SortableId.NewId()),
            teamId,
            new PlayerId(SortableId.NewId()),
            [new ContractSeasonTerm(new Season(year), new Money(compensation), new Money(compensation))]);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>
    /// A roster-slot hold is a charge for a player who does not exist yet, so it carries neither a
    /// player nor a contract. The two identifiers are optional per kind rather than optional in
    /// general — the factories for the other two kinds still require both.
    /// </summary>
    [Fact]
    public void ARosterSlotHoldCarriesNeitherAPlayerNorAContract()
    {
        var hold = CapCharge.RosterSlotHold(
            new TeamId(SortableId.NewId()),
            new Season(2031),
            new Money(1_150_000));

        Assert.Equal(CapChargeKind.RosterSlotHold, hold.Kind);
        Assert.Null(hold.PlayerId);
        Assert.Null(hold.ContractId);
        Assert.True(hold.IsRosterSlotHold);
        Assert.False(hold.IsDeadMoney);
        Assert.Equal(1_150_000, hold.Amount.SmallestUnits);
    }

    [Fact]
    public void AnActiveContractChargeStillRequiresBothIdentifiers()
    {
        Assert.Throws<ArgumentNullException>(() => CapCharge.ActiveContract(
            new TeamId(SortableId.NewId()),
            new Season(2031),
            playerId: null!,
            new ContractId(SortableId.NewId()),
            new Money(1_000_000)));
    }
}
