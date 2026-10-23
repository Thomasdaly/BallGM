using BallGM.Domain.AI;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;

namespace BallGM.Rules.Tests.AI;

public sealed class SalaryDumpModelTests
{
    [Fact]
    public void ATaxpayerPaysARebuildingTeamWithRoomInPicksToTakeAnOverpaidContract()
    {
        // 130M against a 120M tax line; the sixth man earns 25M and would ask about 6M on the market.
        var league = League([30_000_000, 30_000_000, 20_000_000, 15_000_000, 10_000_000, 25_000_000]);

        var dumps = SalaryDumpModel.Find(league.Context(), Directions(), SigningTestLeague.StandardRules(league.CapThresholds), scale: null);

        var dump = Assert.Single(dumps);
        Assert.Equal(league.TeamOf("TAX").Id, dump.FromTeamId);
        Assert.Equal(league.TeamOf("REB").Id, dump.ToTeamId);
        Assert.Equal(league.PlayerOf("TAX", 5), dump.PlayerId);
        Assert.Equal(2, dump.Picks.Count);

        // The league makes a franchise keep its first every other draft: the two firsts are two drafts apart.
        var years = dump.Picks.Select(id => league.DraftAssets.Pick(id)!).Select(pick => pick.DraftSeason.Year).Order().ToList();
        Assert.True(years[1] - years[0] >= 2);
        Assert.All(dump.Picks, id => Assert.Equal(1, league.DraftAssets.Pick(id)!.Round));
        Assert.Contains("rebuilding", dump.Rationale.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ATeamBarelyOverTheTaxDoesNotPayAFirstToGetUnderIt()
    {
        var league = League([30_000_000, 30_000_000, 20_000_000, 15_000_000, 10_000_000, 16_000_000]);

        Assert.Empty(SalaryDumpModel.Find(league.Context(), Directions(), SigningTestLeague.StandardRules(league.CapThresholds), scale: null));
    }

    [Fact]
    public void AContractSignedThisSummerIsNotDumpedTheSameSummer()
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 8)
            .WithTeam("TAX", 30_000_000, 30_000_000, 20_000_000, 15_000_000, 10_000_000, 25_000_000)
            .WithTeam("REB", 5_000_000, 5_000_000)
            .WithTeam("CON", 5_000_000, 5_000_000);

        Assert.Empty(SalaryDumpModel.Find(league.Context(), Directions(), SigningTestLeague.StandardRules(league.CapThresholds), scale: null));
    }

    [Fact]
    public void WithoutARebuildingTeam_NobodyTakesTheContract()
    {
        var league = League([30_000_000, 30_000_000, 20_000_000, 15_000_000, 10_000_000, 25_000_000]);
        var directions = Directions();
        directions[league.TeamOf("REB").Id] = CompetitiveDirection.Retooling;

        Assert.Empty(SalaryDumpModel.Find(league.Context(), directions, SigningTestLeague.StandardRules(league.CapThresholds), scale: null));
    }

    private static TradeTestLeague League(long[] taxpayer) => TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 8)
        .WithTeam("TAX", taxpayer)
        .WithContractsSignedEarlier("TAX", 1)
        .WithTeam("REB", 5_000_000, 5_000_000)
        .WithTeam("CON", 5_000_000, 5_000_000);

    private static Dictionary<TeamId, CompetitiveDirection> Directions() => new()
    {
        [new TeamId("TEAM-TAX")] = CompetitiveDirection.Contending,
        [new TeamId("TEAM-REB")] = CompetitiveDirection.Rebuilding,
        [new TeamId("TEAM-CON")] = CompetitiveDirection.Contending,
    };
}
