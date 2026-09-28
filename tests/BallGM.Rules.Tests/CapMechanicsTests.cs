using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;
using BallGM.Rules.Trades;

namespace BallGM.Rules.Tests;

/// <summary>
/// The tax schedule, apron-tiered salary matching, and reduced over-cap allowance — the mechanics a
/// real league layers on top of its thresholds. Values here are shaped like a current NBA season
/// (brackets of $6,064,000; 200%/+$9.096M/125% matching bands) to exercise every branch, but they
/// are plain configuration: nothing in the rules knows which league it is.
/// </summary>
public sealed class CapMechanicsTests
{
    private static readonly TeamId Team = new("TEAM-A");

    private static CapMechanics NbaShaped(bool withBands = true) => CapMechanics.Create(
        taxBracketSize: new Money(6_064_000),
        taxRatesPercent: [100, 125, 350, 475],
        repeaterTaxRatesPercent: [300, 325, 550, 675],
        taxRateIncrementPercent: 50,
        salaryMatchingBands: withBands
            ?
            [
                new SalaryMatchingBand(new Money(8_846_000), 200, new Money(250_000)),
                new SalaryMatchingBand(new Money(35_384_000), 100, new Money(9_096_000)),
                new SalaryMatchingBand(null, 125, new Money(250_000)),
            ]
            : null,
        aboveFirstApronMatchPercent: 100,
        secondApronBlocksAggregation: true,
        reducedOverCapAllowance: new Money(6_064_000),
        reducedOverCapAllowanceUnavailableAbove: CapThresholdKind.SecondApron).Value;

    [Fact]
    public void TaxBill_ChargesEachBracketAtItsOwnRate()
    {
        // $20M over: 6.064M at 1.00, 6.064M at 1.25, 6.064M at 3.50, 1.808M at 4.75.
        var bill = CapStatusEvaluator.Bill(new Money(220_000_000), new Money(200_000_000), NbaShaped(), isRepeater: false);

        Assert.Equal(20_000_000, bill.AmountOverTaxLine.SmallestUnits);
        Assert.Equal([100, 125, 350, 475], bill.Brackets.Select(bracket => bracket.RatePercent));
        Assert.Equal(6_064_000 + 7_580_000 + 21_224_000 + 8_588_000, bill.TaxOwed.SmallestUnits);
    }

    [Fact]
    public void TaxBill_RaisesTheRateByTheIncrementPastTheListedBrackets()
    {
        // Seven brackets deep: the fifth, sixth and seventh are 4.75 + 0.50 per extra bracket.
        var bill = CapStatusEvaluator.Bill(new Money(200_000_000 + (7 * 6_064_000)), new Money(200_000_000), NbaShaped(), isRepeater: false);

        Assert.Equal([100, 125, 350, 475, 525, 575, 625], bill.Brackets.Select(bracket => bracket.RatePercent));
    }

    [Fact]
    public void TaxBill_UsesTheRepeaterScheduleForARepeater()
    {
        var bill = CapStatusEvaluator.Bill(new Money(206_064_000), new Money(200_000_000), NbaShaped(), isRepeater: true);

        Assert.Equal(300, Assert.Single(bill.Brackets).RatePercent);
        Assert.Equal(18_192_000, bill.TaxOwed.SmallestUnits);
        Assert.True(bill.IsRepeater);
    }

    [Fact]
    public void TaxBill_IsNothingUnderTheLine()
    {
        var bill = CapStatusEvaluator.Bill(new Money(199_000_000), new Money(200_000_000), NbaShaped(), isRepeater: false);

        Assert.Equal(0, bill.TaxOwed.SmallestUnits);
        Assert.Empty(bill.Brackets);
    }

    [Theory]
    [InlineData(5_000_000, 10_250_000)]   // up to $8.846M: 200% + $250K
    [InlineData(20_000_000, 29_096_000)]  // $8.846M-$35.384M: outgoing + $9.096M
    [InlineData(40_000_000, 50_250_000)]  // above: 125% + $250K
    public void TieredMatching_UsesTheBandForTheOutgoingAmount(long outgoing, long allowed)
    {
        Assert.Equal(allowed, NbaShaped().TieredMatchingLimit(new Money(outgoing))!.SmallestUnits);
    }

    [Fact]
    public void Mechanics_RefuseBandsThatAreOutOfOrderOrOpenEndedTooEarly()
    {
        var result = CapMechanics.Create(null, null, null, null,
            [new SalaryMatchingBand(new Money(10), 150, Money.Zero), new SalaryMatchingBand(new Money(5), 150, Money.Zero), new SalaryMatchingBand(new Money(20), 125, Money.Zero)],
            null, false, null, null);

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error => Assert.Equal("ruleset.invalid_salary_matching_bands", error.Code));
    }

    [Fact]
    public void Mechanics_RefuseATaxBracketWithoutRates()
    {
        var result = CapMechanics.Create(new Money(6_000_000), null, null, null, null, null, false, null, null);

        Assert.Equal("ruleset.invalid_tax_schedule", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Trade_UsesTheTieredBandBelowTheFirstApron()
    {
        // A is over the cap (110M against 100M) and sends $5M: the band allows $10.25M back.
        var league = TradeTestLeague.Build(salaryMatchPercent: 125, salaryMatchAllowance: 1_000_000)
            .WithTeam("A", 105_000_000, 5_000_000)
            .WithTeam("B", 10_200_000, 11_000_000, 30_000_000)
            .WithCapMechanics(NbaShaped());

        var legal = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));
        var tooMuch = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 1, "A"));

        Assert.True(legal.IsLegal, string.Join("; ", legal.Violations.Select(v => v.Explanation)));
        var violation = Assert.Single(tooMuch.Violations, finding => finding.RuleCode == "trade.salary_not_matched");
        Assert.Contains("band", violation.Explanation);
    }

    [Fact]
    public void Trade_AboveTheFirstApronMayTakeBackOnlyWhatItSends()
    {
        // A finishes at 133.5M, above the first apron (130M): $5.5M back for $5M is refused, even
        // though the flat 125% rule and the band would both allow it.
        var league = TradeTestLeague.Build(salaryMatchPercent: 125, salaryMatchAllowance: 1_000_000)
            .WithTeam("A", 128_000_000, 5_000_000)
            .WithTeam("B", 5_500_000, 30_000_000)
            .WithCapMechanics(NbaShaped());

        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.Contains(assessment.Violations, finding => finding.RuleCode == "trade.first_apron_salary_matching");
    }

    [Fact]
    public void Trade_AboveTheSecondApronCannotCombineSalaries()
    {
        // A sends two $6M contracts for one $11M contract and finishes at 141M, above the second apron.
        var league = TradeTestLeague.Build(salaryMatchPercent: 125, salaryMatchAllowance: 1_000_000)
            .WithTeam("A", 130_000_000, 6_000_000, 6_000_000)
            .WithTeam("B", 11_000_000, 30_000_000)
            .WithCapMechanics(NbaShaped());

        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("A", 2, "B"), league.SendPlayer("B", 0, "A"));

        Assert.Contains(assessment.Violations, finding => finding.RuleCode == "trade.second_apron_salary_aggregation");
    }

    [Fact]
    public void ReducedAllowance_OpensOnlyBetweenTheTwoCutoffs()
    {
        var rules = SigningTestNegotiationRules();
        var thresholds = Thresholds;
        var offer = OfferFor(new Money(5_000_000));

        SigningRouteEvaluation Reduced(long payroll) => SigningRouteTable.Evaluate(
            offer, rules, thresholds, 5, new Money(payroll), Money.Zero, Money.Zero, 0, NbaShaped(), Money.Zero)
            .Single(route => route.Kind == SigningRouteKind.ReducedOverCapAllowance);

        Assert.False(Reduced(125_000_000).Applicable);   // still has the standard allowance
        Assert.True(Reduced(135_000_000).Permits);       // past the first apron, under the second
        Assert.False(Reduced(145_000_000).Permits);      // past the second apron too
        Assert.Equal("signing.reduced_allowance_unavailable_above_threshold", Reduced(145_000_000).RuleCode);
    }

    [Fact]
    public void Status_ListsOnlyTheRestrictionsThePayrollTriggers()
    {
        var ruleset = RulesetWith(NbaShaped());

        var underCap = CapStatusEvaluator.Evaluate(Team, new Money(90_000_000), false, ruleset);
        var pastSecond = CapStatusEvaluator.Evaluate(Team, new Money(145_000_000), false, ruleset);

        Assert.Empty(underCap.Restrictions);
        var codes = pastSecond.Restrictions.Select(finding => finding.RuleCode).ToList();
        Assert.Contains(CapStatusEvaluator.OverTaxLineCode, codes);
        Assert.Contains(CapStatusEvaluator.NoAllowanceCode, codes);
        Assert.Contains(CapStatusEvaluator.ApronMatchingCode, codes);
        Assert.Contains(CapStatusEvaluator.NoAggregationCode, codes);
        Assert.True(pastSecond.TaxBill!.TaxOwed.SmallestUnits > 0);
    }

    private static readonly CapThresholds Thresholds = CapThresholds.Create(
        softCap: new Money(100_000_000),
        luxuryTax: new Money(120_000_000),
        firstApron: new Money(130_000_000),
        secondApron: new Money(140_000_000)).Value;

    private static TradeAssessment Assess(TradeTestLeague league, params TradeAssetMovement[] movements)
    {
        var result = new TradeValidator().Validate(league.Proposal(movements), league.Context());
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static NegotiationRules SigningTestNegotiationRules() => NegotiationRules.Create(
        Thresholds,
        maximumContractSeasons: 5,
        maximumIncumbentContractSeasons: 5,
        maximumAnnualEscalationPercent: 8,
        maximumAnnualDeescalationPercent: 8,
        compensationCeiling: null,
        compensationFloor: null,
        standardOverCapAllowance: new Money(15_044_000),
        standardOverCapAllowanceUnavailableAbove: CapThresholdKind.FirstApron,
        allowanceMaySplitAcrossPlayers: true,
        marketResolution: MarketResolutionMode.ResolutionPoint,
        offerExpiryDays: 3).Value;

    private static Offer OfferFor(Money firstSeason) => Offer.Create(
        new OfferId("OFFER-1"),
        Team,
        new BallGM.Domain.Players.PlayerId("PLAYER-FA"),
        [new BallGM.Domain.Contracts.ContractSeasonTerm(new BallGM.Domain.Leagues.Season(2031), firstSeason, firstSeason)]).Value;

    private static LeagueRuleset RulesetWith(CapMechanics mechanics) => new(
        schemaVersion: LeagueRuleset.CurrentSchemaVersion,
        name: "Cap mechanics test",
        regularSeasonGameCount: 82,
        rosterLimits: new RosterSizeLimits(12, 15),
        capThresholds: Thresholds,
        draftRules: DraftRules.NoDraft,
        tradeRules: TradeRules.Create(125, new Money(250_000), InjuredPlayerTradeEligibility.AllowedWithWarning, secondApronBlocksSalaryIncrease: true).Value,
        negotiationRules: SigningTestNegotiationRules(),
        capMechanics: mechanics);
}
