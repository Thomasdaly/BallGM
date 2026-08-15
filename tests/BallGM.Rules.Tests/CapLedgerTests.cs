using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class CapLedgerTests
{
    private const long SoftCap = 141_000_000;
    private const long LuxuryTax = 172_000_000;
    private const long FirstApron = 179_000_000;
    private const long SecondApron = 190_000_000;
    private const long HardCap = 205_000_000;

    private static readonly Season Season2031 = new(2031);
    private static readonly TeamId Team = new(SortableId.NewId());

    [Fact]
    public void CapSheet_SeparatesLiveContractMoneyFromDeadMoneyAndTotalsBoth()
    {
        var sheet = Evaluate(
            ActiveCharge(80_000_000),
            ActiveCharge(45_000_000),
            DeadCharge(6_200_000));

        Assert.Equal(125_000_000, sheet.CommittedSalary.SmallestUnits);
        Assert.Equal(6_200_000, sheet.DeadMoney.SmallestUnits);
        Assert.Equal(131_200_000, sheet.TotalPayroll.SmallestUnits);
    }

    [Fact]
    public void CapSheet_ReportsAPayrollWithNoChargesAsEmptyRatherThanFailing()
    {
        var sheet = Evaluate();

        Assert.Equal(0, sheet.TotalPayroll.SmallestUnits);
        Assert.Equal(SoftCap, sheet.StandingFor(CapThresholdKind.SoftCap).SignedDistanceSmallestUnits);
    }

    [Theory]
    [InlineData(SoftCap - 1, CapThresholdKind.SoftCap, "cap.under_soft_cap")]
    [InlineData(SoftCap, CapThresholdKind.SoftCap, "cap.at_soft_cap")]
    [InlineData(SoftCap + 1, CapThresholdKind.SoftCap, "cap.over_soft_cap")]
    [InlineData(LuxuryTax - 1, CapThresholdKind.LuxuryTax, "cap.under_luxury_tax")]
    [InlineData(LuxuryTax, CapThresholdKind.LuxuryTax, "cap.at_luxury_tax")]
    [InlineData(LuxuryTax + 1, CapThresholdKind.LuxuryTax, "cap.over_luxury_tax")]
    [InlineData(FirstApron - 1, CapThresholdKind.FirstApron, "cap.under_first_apron")]
    [InlineData(FirstApron, CapThresholdKind.FirstApron, "cap.at_first_apron")]
    [InlineData(FirstApron + 1, CapThresholdKind.FirstApron, "cap.over_first_apron")]
    [InlineData(SecondApron - 1, CapThresholdKind.SecondApron, "cap.under_second_apron")]
    [InlineData(SecondApron, CapThresholdKind.SecondApron, "cap.at_second_apron")]
    [InlineData(SecondApron + 1, CapThresholdKind.SecondApron, "cap.over_second_apron")]
    [InlineData(HardCap - 1, CapThresholdKind.HardCap, "cap.under_hard_cap")]
    [InlineData(HardCap, CapThresholdKind.HardCap, "cap.at_hard_cap")]
    [InlineData(HardCap + 1, CapThresholdKind.HardCap, "cap.over_hard_cap")]
    public void EachThreshold_IsReportedAtAndEitherSideOfItsBoundary(
        long payroll,
        CapThresholdKind kind,
        string expectedRuleCode)
    {
        var sheet = Evaluate(ActiveCharge(payroll));

        Assert.Equal(expectedRuleCode, sheet.StandingFor(kind).RuleCode);
    }

    [Fact]
    public void ThresholdDistance_IsSignedSoRoomAndOverageCannotBeConfused()
    {
        var underTheCap = Evaluate(ActiveCharge(120_000_000)).StandingFor(CapThresholdKind.SoftCap);
        var overTheCap = Evaluate(ActiveCharge(198_000_000)).StandingFor(CapThresholdKind.SoftCap);

        Assert.Equal(21_000_000, underTheCap.SignedDistanceSmallestUnits);
        Assert.Equal(ThresholdPosition.Under, underTheCap.Position);
        Assert.False(underTheCap.IsOver);

        Assert.Equal(-57_000_000, overTheCap.SignedDistanceSmallestUnits);
        Assert.Equal(ThresholdPosition.Over, overTheCap.Position);
        Assert.True(overTheCap.IsOver);
    }

    [Fact]
    public void ATeamOverTheSecondApron_IsAlsoReportedOverEveryLineBelowIt()
    {
        var sheet = Evaluate(ActiveCharge(198_000_000));

        Assert.True(sheet.StandingFor(CapThresholdKind.SoftCap).IsOver);
        Assert.True(sheet.StandingFor(CapThresholdKind.LuxuryTax).IsOver);
        Assert.True(sheet.StandingFor(CapThresholdKind.FirstApron).IsOver);
        Assert.True(sheet.StandingFor(CapThresholdKind.SecondApron).IsOver);
        Assert.False(sheet.StandingFor(CapThresholdKind.HardCap).IsOver);
    }

    [Fact]
    public void EveryThreshold_ExplainsItselfInWordsAndNotOnlyInACode()
    {
        var sheet = Evaluate(ActiveCharge(175_000_000));

        Assert.Equal(5, sheet.Thresholds.Count);
        Assert.All(sheet.Thresholds, standing => Assert.False(string.IsNullOrWhiteSpace(standing.Explanation)));
        Assert.Contains("tax", sheet.StandingFor(CapThresholdKind.LuxuryTax).Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapSheet_RefusesAChargeThatBelongsToAnotherTeam()
    {
        var otherTeamCharge = CapCharge.ActiveContract(
            new TeamId(SortableId.NewId()),
            Season2031,
            new PlayerId(SortableId.NewId()),
            new ContractId(SortableId.NewId()),
            new Money(10_000_000));

        var result = new CapLedger().Evaluate(Team, Season2031, [otherTeamCharge], Thresholds());

        Assert.True(result.IsFailure);
        Assert.Equal("cap_ledger.charge_team_mismatch", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void CapSheet_RefusesAChargeFromAnotherSeason()
    {
        var otherSeasonCharge = CapCharge.ActiveContract(
            Team,
            new Season(2032),
            new PlayerId(SortableId.NewId()),
            new ContractId(SortableId.NewId()),
            new Money(10_000_000));

        var result = new CapLedger().Evaluate(Team, Season2031, [otherSeasonCharge], Thresholds());

        Assert.True(result.IsFailure);
        Assert.Equal("cap_ledger.charge_season_mismatch", Assert.Single(result.Errors).Code);
    }

    private static TeamCapSheet Evaluate(params CapCharge[] charges)
    {
        var result = new CapLedger().Evaluate(Team, Season2031, charges, Thresholds());

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static CapCharge ActiveCharge(long amount) =>
        CapCharge.ActiveContract(
            Team,
            Season2031,
            new PlayerId(SortableId.NewId()),
            new ContractId(SortableId.NewId()),
            new Money(amount));

    private static CapCharge DeadCharge(long amount) =>
        CapCharge.DeadMoney(
            Team,
            Season2031,
            new PlayerId(SortableId.NewId()),
            new ContractId(SortableId.NewId()),
            new Money(amount));

    private static CapThresholds Thresholds()
    {
        var result = CapThresholds.Create(
            new Money(SoftCap),
            new Money(LuxuryTax),
            new Money(FirstApron),
            new Money(SecondApron),
            new Money(HardCap));

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
