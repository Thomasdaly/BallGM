using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

/// <summary>
/// The guardrails that stop buyouts becoming a way to move good players every season
/// (<c>docs/franchise-realism.md</c> → F4): a team cannot buy a player out and bring him straight back,
/// and a team far over the cap cannot pick up a released high earner.
/// </summary>
public sealed class BuyoutGuardrailTests
{
    private static readonly BuyoutRules Rules = BuyoutRules.Create(40, 1, CapThresholdKind.FirstApron).Value;

    [Fact]
    public void ATeamCannotSignAPlayerItBoughtOutThisSeason()
    {
        var league = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000]);
        league.ReleasedFrom(league.Team.Id, 20_000_000);

        var assessment = new SigningValidator().Validate(league.Offer(2_000_000), league.Context() with { Buyouts = Rules }).Value;

        Assert.Contains(assessment.Violations, finding => finding.RuleCode == SigningValidator.ReleasedByThisTeamCode);
    }

    [Fact]
    public void AnotherTeamUnderTheLineMaySignHim_AndWithoutBuyoutRulesNothingIsBarred()
    {
        var league = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000]);
        league.ReleasedFrom(new TeamId("TEAM-ELSEWHERE"), 20_000_000);

        var guarded = new SigningValidator().Validate(league.Offer(2_000_000), league.Context() with { Buyouts = Rules }).Value;
        Assert.DoesNotContain(guarded.Violations, finding => finding.RuleCode.StartsWith("signing.released", StringComparison.Ordinal));

        var own = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000]);
        own.ReleasedFrom(own.Team.Id, 20_000_000);
        var unguarded = new SigningValidator().Validate(own.Offer(2_000_000), own.Context()).Value;
        Assert.DoesNotContain(unguarded.Violations, finding => finding.RuleCode.StartsWith("signing.released", StringComparison.Ordinal));
    }

    [Fact]
    public void ATeamOverTheFirstApronCannotSignAReleasedHighEarner_ButMaySignAReleasedRolePlayer()
    {
        // Payroll 129M + 2M signing = 131M, over the 130M first apron.
        var star = SigningTestLeague.Build([43_000_000, 43_000_000, 43_000_000]);
        star.ReleasedFrom(new TeamId("TEAM-ELSEWHERE"), 30_000_000);
        var starAssessment = new SigningValidator().Validate(star.Offer(2_000_000), star.Context() with { Buyouts = Rules }).Value;
        Assert.Contains(starAssessment.Violations, finding => finding.RuleCode == SigningValidator.ReleasedHighEarnerAboveLineCode);

        var rolePlayer = SigningTestLeague.Build([43_000_000, 43_000_000, 43_000_000]);
        rolePlayer.ReleasedFrom(new TeamId("TEAM-ELSEWHERE"), 8_000_000);
        var roleAssessment = new SigningValidator().Validate(rolePlayer.Offer(2_000_000), rolePlayer.Context() with { Buyouts = Rules }).Value;
        Assert.DoesNotContain(roleAssessment.Violations, finding => finding.RuleCode == SigningValidator.ReleasedHighEarnerAboveLineCode);
    }

    [Fact]
    public void ABuyoutLeavesOnlyTheRetainedShareAsDeadMoney()
    {
        var season = new BallGM.Domain.Leagues.Season(2031);
        var contract = Contract.Create(new ContractId("C"), new TeamId("T"), new PlayerId("P"),
            [new ContractSeasonTerm(season, new Money(20_000_000), new Money(20_000_000))]).Value;

        Assert.True(contract.BuyOut(season, 70).IsSuccess);

        var charge = contract.ChargeFor(season)!;
        Assert.True(charge.IsDeadMoney);
        Assert.Equal(14_000_000, charge.Amount.SmallestUnits);
        Assert.True(contract.IsBoughtOut);
        Assert.True(Contract.Create(new ContractId("D"), new TeamId("T"), new PlayerId("P"),
            [new ContractSeasonTerm(season, new Money(1), new Money(1))]).Value.BuyOut(season, 101).IsFailure);
    }
}
