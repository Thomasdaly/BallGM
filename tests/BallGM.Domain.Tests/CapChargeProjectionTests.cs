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
}
