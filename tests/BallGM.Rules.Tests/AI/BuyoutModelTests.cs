using BallGM.Domain.AI;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests.AI;

public sealed class BuyoutModelTests
{
    private static readonly Season Season = new(2031);
    private static readonly DateOnly Summer = new(2031, 7, 1);
    private static readonly BuyoutRules Rules = BuyoutRules.Create(40, 1, CapThresholdKind.FirstApron).Value;

    [Fact]
    public void ARebuildingTeamBuysOutAnOverpaidVeteranOutsideItsRotation()
    {
        var (team, roster, contracts) = Build(veteranOverall: 50, veteranSalary: 25_000_000, veteranBorn: 1998);

        var decision = BuyoutModel.Decide(team, roster, contracts, Season, SigningTestLeague.StandardRules(), SigningTestLeague.StandardThresholds, Rules, CompetitiveDirection.Rebuilding, Summer);

        Assert.NotNull(decision);
        Assert.Equal(new PlayerId("VETERAN"), decision!.PlayerId);
        Assert.InRange(decision.RetainedPercent, 60, 99);
    }

    [Fact]
    public void AContenderKeepsHisVeterans_AndNobodyBuysOutAGoodPlayerOrAYoungOne()
    {
        var (team, roster, contracts) = Build(veteranOverall: 50, veteranSalary: 25_000_000, veteranBorn: 1998);
        Assert.Null(BuyoutModel.Decide(team, roster, contracts, Season, SigningTestLeague.StandardRules(), SigningTestLeague.StandardThresholds, Rules, CompetitiveDirection.Contending, Summer));

        var (goodTeam, goodRoster, goodContracts) = Build(veteranOverall: 85, veteranSalary: 25_000_000, veteranBorn: 1998);
        Assert.Null(BuyoutModel.Decide(goodTeam, goodRoster, goodContracts, Season, SigningTestLeague.StandardRules(), SigningTestLeague.StandardThresholds, Rules, CompetitiveDirection.Rebuilding, Summer));

        var (youngTeam, youngRoster, youngContracts) = Build(veteranOverall: 50, veteranSalary: 25_000_000, veteranBorn: 2006);
        Assert.Null(BuyoutModel.Decide(youngTeam, youngRoster, youngContracts, Season, SigningTestLeague.StandardRules(), SigningTestLeague.StandardThresholds, Rules, CompetitiveDirection.Rebuilding, Summer));

        Assert.Null(BuyoutModel.Decide(team, roster, contracts, Season, SigningTestLeague.StandardRules(), SigningTestLeague.StandardThresholds, BuyoutRules.None, CompetitiveDirection.Rebuilding, Summer));
    }

    private static (Team Team, List<Player> Roster, List<Contract> Contracts) Build(int veteranOverall, long veteranSalary, int veteranBorn)
    {
        var roster = Enumerable.Range(0, 9)
            .Select(index => Player.Create(new PlayerId($"ROTATION-{index}"), $"Rotation {index}", Position.SmallForward, new PlayerRating(70), new DateOnly(2004, 1, 1), 4).Value)
            .Append(Player.Create(new PlayerId("VETERAN"), "Old Hand", Position.Center, new PlayerRating(veteranOverall), new DateOnly(veteranBorn, 1, 1), 10).Value)
            .ToList();
        var franchise = Franchise.Create(new FranchiseId("F"), "Franchise").Value;
        var team = Team.Create(new TeamId("TEAM"), franchise.Id, "Testfield", new RosterSizeLimits(5, 15), roster.Select(player => player.Id)).Value;
        var contracts = roster.Select(player =>
        {
            var salary = new Money(player.Id.Value == "VETERAN" ? veteranSalary : 5_000_000);
            return Contract.Create(new ContractId($"C-{player.Id.Value}"), team.Id, player.Id,
                [new ContractSeasonTerm(Season, salary, salary), new ContractSeasonTerm(new Season(2032), salary, salary)]).Value;
        }).ToList();
        return (team, roster, contracts);
    }
}
