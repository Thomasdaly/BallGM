using BallGM.Domain.AI;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;

namespace BallGM.Rules.Tests.AI;

public sealed class VeteranSaleModelTests
{
    private static readonly DateOnly Summer = new(2031, 7, 1);

    [Fact]
    public void ALosingTeamSellsItsAgeingStarToAContenderForPicksAndMatchingSalary()
    {
        var league = League(veteranBorn: new DateOnly(1998, 1, 1));

        var sale = Assert.Single(VeteranSaleModel.Find(league.Context(), Directions(), Records(sellerWins: 20), Summer));

        Assert.Equal(league.TeamOf("SELL").Id, sale.SellerTeamId);
        Assert.Equal(league.TeamOf("BUY").Id, sale.BuyerTeamId);
        Assert.Equal(league.PlayerOf("SELL", 0), sale.VeteranId);
        Assert.True(sale.IsStar);
        Assert.Equal(2, sale.Picks.Count);
        Assert.NotEmpty(sale.SalaryBack);
        Assert.DoesNotContain(sale.SalaryBack, id => Enumerable.Range(0, 4).Select(index => league.PlayerOf("BUY", index)).Contains(id));
        Assert.Contains("out of contention", sale.Rationale.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AStarInHisPrimeStaysWithTheTeamBuildingAroundHim()
    {
        var league = League(veteranBorn: new DateOnly(2002, 1, 1));

        Assert.Empty(VeteranSaleModel.Find(league.Context(), Directions(), Records(sellerWins: 20), Summer));
    }

    [Fact]
    public void AWinningTeamDoesNotSell()
    {
        var league = League(veteranBorn: new DateOnly(1998, 1, 1));

        Assert.Empty(VeteranSaleModel.Find(league.Context(), Directions(), Records(sellerWins: 45), Summer));
    }

    [Fact]
    public void APlayerSignedThisSummerIsNotSoldTheSameSummer()
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 10)
            .WithTeam("SELL", 20_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000)
            .WithTeam("BUY", 30_000_000, 25_000_000, 20_000_000, 15_000_000, 10_000_000, 8_000_000, 6_000_000, 4_000_000)
            .WithPlayer("SELL", 0, 85, new DateOnly(1998, 1, 1))
            .WithContractsSignedEarlier("BUY", 2);

        Assert.Empty(VeteranSaleModel.Find(league.Context(), Directions(), Records(sellerWins: 20), Summer));
    }

    [Fact]
    public void PricesFollowTheVeteransRankInTheLeague()
    {
        Assert.Equal((2, 0), VeteranSaleModel.PriceFor(10));
        Assert.Equal((1, 1), VeteranSaleModel.PriceFor(100));
        Assert.Equal((1, 0), VeteranSaleModel.PriceFor(200));
        Assert.Equal((0, 2), VeteranSaleModel.PriceFor(300));
        Assert.Null(VeteranSaleModel.PriceFor(500));
    }

    private static TradeTestLeague League(DateOnly veteranBorn)
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 10)
            .WithTeam("SELL", 20_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000)
            .WithTeam("BUY", 30_000_000, 25_000_000, 20_000_000, 15_000_000, 10_000_000, 8_000_000, 6_000_000, 4_000_000)
            .WithPlayer("SELL", 0, 85, veteranBorn)
            .WithContractsSignedEarlier("SELL", 2)
            .WithContractsSignedEarlier("BUY", 2);
        for (var index = 1; index < 6; index++)
        {
            league.WithPlayer("SELL", index, 60, new DateOnly(2008, 1, 1));
        }

        for (var index = 0; index < 8; index++)
        {
            league.WithPlayer("BUY", index, index < 5 ? 80 : 60, new DateOnly(2003, 1, 1));
        }

        return league;
    }

    private static Dictionary<TeamId, CompetitiveDirection> Directions() => new()
    {
        [new TeamId("TEAM-SELL")] = CompetitiveDirection.Rebuilding,
        [new TeamId("TEAM-BUY")] = CompetitiveDirection.Contending,
    };

    private static Dictionary<TeamId, TeamRecord> Records(int sellerWins) => new()
    {
        [new TeamId("TEAM-SELL")] = new TeamRecord(sellerWins, 82 - sellerWins),
        [new TeamId("TEAM-BUY")] = new TeamRecord(60, 22),
    };
}
