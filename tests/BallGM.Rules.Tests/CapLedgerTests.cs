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
        Assert.Equal(SoftCap, Standing(sheet, CapThresholdKind.SoftCap).SignedDistanceSmallestUnits);
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

        Assert.Equal(expectedRuleCode, Standing(sheet, kind).RuleCode);
    }

    [Fact]
    public void ThresholdDistance_IsSignedSoRoomAndOverageCannotBeConfused()
    {
        var underTheCap = Standing(Evaluate(ActiveCharge(120_000_000)), CapThresholdKind.SoftCap);
        var overTheCap = Standing(Evaluate(ActiveCharge(198_000_000)), CapThresholdKind.SoftCap);

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

        Assert.True(Standing(sheet, CapThresholdKind.SoftCap).IsOver);
        Assert.True(Standing(sheet, CapThresholdKind.LuxuryTax).IsOver);
        Assert.True(Standing(sheet, CapThresholdKind.FirstApron).IsOver);
        Assert.True(Standing(sheet, CapThresholdKind.SecondApron).IsOver);
        Assert.False(Standing(sheet, CapThresholdKind.HardCap).IsOver);
    }

    [Fact]
    public void EveryThreshold_ExplainsItselfInWordsAndNotOnlyInACode()
    {
        var sheet = Evaluate(ActiveCharge(175_000_000));

        Assert.Equal(5, sheet.Thresholds.Count);
        Assert.All(sheet.Thresholds, standing => Assert.False(string.IsNullOrWhiteSpace(standing.Explanation)));
        Assert.Contains("tax", Standing(sheet, CapThresholdKind.LuxuryTax).Explanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A league that configures two lines gets two standings. Not five, and certainly not five with
    /// three of them set to zero, which would report every team as over three lines it does not
    /// have.
    /// </summary>
    [Fact]
    public void APartiallyConfiguredLeague_ReportsOnlyTheThresholdsItHas()
    {
        var partial = CapThresholds.Create(
            softCap: new Money(SoftCap),
            luxuryTax: new Money(LuxuryTax)).Value;

        var sheet = new CapLedger()
            .Evaluate(Team, Season2031, [ActiveCharge(150_000_000)], partial)
            .Value;

        Assert.Equal(
            [CapThresholdKind.SoftCap, CapThresholdKind.LuxuryTax],
            sheet.Thresholds.Select(standing => standing.Kind));

        Assert.Null(sheet.StandingFor(CapThresholdKind.FirstApron));
        Assert.Equal("cap.over_soft_cap", Standing(sheet, CapThresholdKind.SoftCap).RuleCode);
        Assert.Equal("cap.under_luxury_tax", Standing(sheet, CapThresholdKind.LuxuryTax).RuleCode);
    }

    /// <summary>
    /// An uncapped league produces a real payroll and nothing to measure it against — the truth,
    /// rather than five zeroes every team is over.
    /// </summary>
    [Fact]
    public void AnUncappedLeague_ProducesAPayrollAndNoStandings()
    {
        var sheet = new CapLedger()
            .Evaluate(Team, Season2031, [ActiveCharge(88_000_000)], CapThresholds.Uncapped)
            .Value;

        Assert.Equal(88_000_000, sheet.TotalPayroll.SmallestUnits);
        Assert.Empty(sheet.Thresholds);
    }

    [Fact]
    public void StandingFor_ReturnsNullForAThresholdTheLeagueDoesNotConfigure()
    {
        var sheet = new CapLedger()
            .Evaluate(Team, Season2031, [ActiveCharge(88_000_000)], CapThresholds.Uncapped)
            .Value;

        Assert.Null(sheet.StandingFor(CapThresholdKind.SoftCap));
    }

    /// <summary>
    /// The payroll floor is the one threshold a team is on the wrong side of by being under it, so
    /// the standing has to say that in words as well as in the sign of a number.
    /// </summary>
    [Fact]
    public void ATeamBelowThePayrollFloor_GetsAStatedStanding()
    {
        var withFloor = CapThresholds.Create(
            payrollFloor: new Money(127_000_000),
            softCap: new Money(SoftCap)).Value;

        var sheet = new CapLedger()
            .Evaluate(Team, Season2031, [ActiveCharge(118_400_000)], withFloor)
            .Value;

        var floor = Standing(sheet, CapThresholdKind.PayrollFloor);

        Assert.Equal("cap.under_payroll_floor", floor.RuleCode);
        Assert.Equal(8_600_000, floor.SignedDistanceSmallestUnits);
        Assert.True(floor.IsBreached);
        Assert.True(floor.IsFloor);
        Assert.Contains("below the payroll floor", floor.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Over the floor is compliance, not a breach — the distinction the rest of the cap sheet keys
    /// its highlighting off.
    /// </summary>
    [Fact]
    public void ATeamAboveThePayrollFloor_IsNotReportedAsBreachingIt()
    {
        var withFloor = CapThresholds.Create(
            payrollFloor: new Money(127_000_000),
            softCap: new Money(SoftCap)).Value;

        var sheet = new CapLedger()
            .Evaluate(Team, Season2031, [ActiveCharge(135_000_000)], withFloor)
            .Value;

        var floor = Standing(sheet, CapThresholdKind.PayrollFloor);

        Assert.True(floor.IsOver);
        Assert.False(floor.IsBreached);
    }

    /// <summary>
    /// A roster-slot hold is money a team has not spent yet but cannot spend twice. It counts
    /// towards the payroll like any other charge, which is what makes the room it leaves real room.
    /// </summary>
    [Fact]
    public void ARosterSlotHold_ReducesTheRoomUnderTheSoftCap()
    {
        var withoutHold = Evaluate(ActiveCharge(120_000_000));
        var withHold = Evaluate(ActiveCharge(120_000_000), CapCharge.RosterSlotHold(Team, Season2031, new Money(1_150_000)));

        Assert.Equal(121_150_000, withHold.TotalPayroll.SmallestUnits);
        Assert.Equal(
            Standing(withoutHold, CapThresholdKind.SoftCap).SignedDistanceSmallestUnits - 1_150_000,
            Standing(withHold, CapThresholdKind.SoftCap).SignedDistanceSmallestUnits);

        // Its own bucket. A hold is not dead money — nobody has been released — and it is not
        // committed salary either, because no player is owed it; folding it into either would make
        // one of those two figures answer a question it is not being asked.
        Assert.Equal(0, withHold.DeadMoney.SmallestUnits);
        Assert.Equal(120_000_000, withHold.CommittedSalary.SmallestUnits);
        Assert.Equal(1_150_000, withHold.RosterHolds.SmallestUnits);
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

    /// <summary>
    /// The standing against a threshold the league is known to configure. Nullable at the source —
    /// a league can legitimately have no such line — so these tests assert it exists before reading
    /// it rather than suppressing the warning.
    /// </summary>
    private static ThresholdStanding Standing(TeamCapSheet sheet, CapThresholdKind kind)
    {
        var standing = sheet.StandingFor(kind);
        Assert.NotNull(standing);
        return standing;
    }

    private static CapThresholds Thresholds()
    {
        var result = CapThresholds.Create(
            softCap: new Money(SoftCap),
            luxuryTax: new Money(LuxuryTax),
            firstApron: new Money(FirstApron),
            secondApron: new Money(SecondApron),
            hardCap: new Money(HardCap));

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
