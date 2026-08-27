using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

/// <summary>
/// Offer legality and the signing routes. The refusals matter more than the approvals here: a GM who
/// is told "no" without being told which line they crossed and by how much has been given a shrug,
/// not a rules engine.
/// </summary>
public sealed class SigningValidatorTests
{
    private static readonly SigningValidator Validator = new();

    [Fact]
    public void ATeamWithRoomBelowTheSoftCapMaySignIntoIt()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, league.Offer(20_000_000));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
        Assert.Equal(SigningRouteKind.CapRoom, assessment.PermittingRoute!.Kind);
        Assert.Equal(30_000_000, assessment.CapRoomBefore!.SmallestUnits);
        Assert.Equal(90_000_000, assessment.PayrollAfter.SmallestUnits);
        Assert.Equal(4, assessment.RosterCountAfter);
    }

    /// <summary>
    /// Cap room counts the hold the signing releases. The spot the new player fills was already being
    /// reserved for somebody, so charging the team for both the hold and the player would find room
    /// that is not missing.
    /// </summary>
    [Fact]
    public void CapRoomCountsBackTheRosterSpotThisSigningFills()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000], minimumRoster: 4);
        var capRoom = Route(Assess(league, league.Offer(1_000_000)), SigningRouteKind.CapRoom);

        // Payroll is 60m of contracts plus two 1m holds; the soft cap is 100m. Room for a signing is
        // 100m - (62m - 1m released) = 39m, not the 38m a naive subtraction would report.
        Assert.Equal(39_000_000, capRoom.MaximumFirstSeasonCompensation!.SmallestUnits);
    }

    [Fact]
    public void AnOfferBeyondTheRoomIsRefusedWithTheShortfallStated()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, league.Offer(45_000_000));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, finding => finding.RuleCode == SigningValidator.NoRouteCode);

        var capRoom = Route(assessment, SigningRouteKind.CapRoom);
        Assert.False(capRoom.Permits);
        Assert.Equal(SigningRouteTable.InsufficientCapRoomCode, capRoom.RuleCode);
        Assert.Contains("15000000", capRoom.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// A minimum-salary signing is available to every team whatever its payroll — the route that
    /// stops a capped-out roster from being unable to field a legal squad.
    /// </summary>
    [Fact]
    public void ATeamWellOverTheCapMayStillSignAtTheLeagueMinimum()
    {
        var league = SigningTestLeague.Build([80_000_000, 40_000_000, 15_000_000]);

        var assessment = Assess(league, league.Offer(2_000_000));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
        Assert.Equal(SigningRouteKind.MinimumSalary, assessment.PermittingRoute!.Kind);
    }

    /// <summary>The floor rises with service, so what counts as a minimum signing does too.</summary>
    [Fact]
    public void TheMinimumAPlayerCanBeSignedForFollowsTheirServiceTier()
    {
        var rookie = SigningTestLeague.Build([80_000_000, 40_000_000, 15_000_000], freeAgentSeasonsOfService: 0);
        var veteran = SigningTestLeague.Build([80_000_000, 40_000_000, 15_000_000], freeAgentSeasonsOfService: 8);

        Assert.Equal(1_000_000, Route(Assess(rookie, rookie.Offer(1_000_000)), SigningRouteKind.MinimumSalary).MaximumFirstSeasonCompensation!.SmallestUnits);
        Assert.Equal(2_000_000, Route(Assess(veteran, veteran.Offer(1_000_000)), SigningRouteKind.MinimumSalary).MaximumFirstSeasonCompensation!.SmallestUnits);

        // A veteran cannot be signed below their own tier's floor, even though a rookie could be.
        var belowVeteranFloor = Assess(veteran, veteran.Offer(1_000_000));
        Assert.False(belowVeteranFloor.IsLegal);
        Assert.Contains(belowVeteranFloor.Violations, finding => finding.RuleCode == OfferLegality.BelowFloorCode);
    }

    [Fact]
    public void ATeamOverTheCapButUnderTheApronMaySpendItsStandardAllowance()
    {
        var league = SigningTestLeague.Build([80_000_000, 30_000_000, 15_000_000]);

        var assessment = Assess(league, league.Offer(10_000_000));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
        Assert.Equal(SigningRouteKind.StandardOverCapAllowance, assessment.PermittingRoute!.Kind);
        Assert.Equal(12_000_000, assessment.PermittingRoute.MaximumFirstSeasonCompensation!.SmallestUnits);
    }

    /// <summary>
    /// The team past the apron has nothing to offer but a pitch. That is the fixture's whole point,
    /// and the refusal says which line withdrew the allowance rather than only that it is gone.
    /// </summary>
    [Fact]
    public void ATeamAboveTheApronHasItsAllowanceWithdrawnAndIsToldWhichLineDidIt()
    {
        var league = SigningTestLeague.Build([90_000_000, 30_000_000, 15_000_000]);

        var assessment = Assess(league, league.Offer(10_000_000));

        Assert.False(assessment.IsLegal);

        var allowance = Route(assessment, SigningRouteKind.StandardOverCapAllowance);
        Assert.True(allowance.Applicable);
        Assert.False(allowance.Permits);
        Assert.Equal(SigningRouteTable.AllowanceWithdrawnCode, allowance.RuleCode);
        Assert.Contains("first apron", allowance.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// How much allowance is left is read back from the ledger rather than kept as a running total —
    /// a stored balance is a second account of the same events, and a rolled-back signing would leave
    /// it wrong with nothing to notice.
    /// </summary>
    [Fact]
    public void AllowanceAlreadySpentThisSeasonComesOffWhatIsLeft()
    {
        var league = SigningTestLeague.Build([80_000_000, 30_000_000, 15_000_000]);

        league.Ledger.RecordSigning(
            SigningTestLeague.CurrentSeason,
            league.Team.Id,
            league.FreeAgent.Id,
            new Domain.Contracts.ContractId("CONTRACT-EARLIER"),
            new Money(9_000_000),
            SigningRouteKind.StandardOverCapAllowance,
            "An earlier allowance signing this season.");

        var allowance = Route(Assess(league, league.Offer(5_000_000)), SigningRouteKind.StandardOverCapAllowance);

        Assert.Equal(3_000_000, allowance.MaximumFirstSeasonCompensation!.SmallestUnits);
        Assert.False(allowance.Permits);
    }

    [Fact]
    public void AnOfferLongerThanTheLeaguePermitsIsRefusedWithTheLimitStated()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, league.Offer(10_000_000, seasons: 7));

        Assert.False(assessment.IsLegal);
        var violation = Assert.Single(assessment.Violations, finding => finding.RuleCode == OfferLegality.TermTooLongCode);
        Assert.Contains("5", violation.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// The escalation limit is a share of the <em>first</em> season, not of the previous one.
    /// Measuring against the previous season would let a long enough contract compound its way to any
    /// figure at all, which is the arbitrage the rule exists to close.
    /// </summary>
    [Fact]
    public void ARaiseSteeperThanTheLimitIsRefusedAndOneAtTheLimitIsNot()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var atTheLimit = Assess(league, league.Offer(10_000_000, seasons: 3, stepPerSeason: 800_000));
        Assert.DoesNotContain(atTheLimit.Violations, finding => finding.RuleCode == OfferLegality.EscalationTooSteepCode);

        var overIt = Assess(league, league.Offer(10_000_000, seasons: 3, stepPerSeason: 900_000));
        Assert.Contains(overIt.Violations, finding => finding.RuleCode == OfferLegality.EscalationTooSteepCode);
    }

    [Fact]
    public void ACutSteeperThanTheLimitIsRefused()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, league.Offer(10_000_000, seasons: 3, stepPerSeason: -900_000));

        Assert.Contains(assessment.Violations, finding => finding.RuleCode == OfferLegality.DeescalationTooSteepCode);
    }

    [Fact]
    public void AnOfferAboveThePlayersCompensationCeilingIsRefused()
    {
        var league = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000], freeAgentSeasonsOfService: 4);

        var assessment = Assess(league, league.Offer(26_000_000));

        Assert.False(assessment.IsLegal);
        // The ceiling is a per-season limit, so an offer that breaches it breaches it in every season
        // it covers, and each one is named rather than the first standing in for the rest.
        var violations = assessment.Violations.Where(finding => finding.RuleCode == OfferLegality.AboveCeilingCode).ToList();
        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Contains("25000000", violation.Explanation, StringComparison.Ordinal));
    }

    /// <summary>The ceiling rises with service, so the same offer is legal for a longer-serving player.</summary>
    [Fact]
    public void TheCeilingRisesWithServiceSoTheSameOfferCanBeLegalForAVeteran()
    {
        var young = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000], freeAgentSeasonsOfService: 4);
        var veteran = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000], freeAgentSeasonsOfService: 11);

        Assert.Contains(Assess(young, young.Offer(30_000_000)).Violations, finding => finding.RuleCode == OfferLegality.AboveCeilingCode);
        Assert.DoesNotContain(Assess(veteran, veteran.Offer(30_000_000)).Violations, finding => finding.RuleCode == OfferLegality.AboveCeilingCode);
    }

    [Fact]
    public void AFullRosterIsRefusedBeforeAnyMoneyIsDiscussed()
    {
        var league = SigningTestLeague.Build([10_000_000, 10_000_000, 10_000_000, 5_000_000, 5_000_000, 5_000_000]);

        var assessment = Assess(league, league.Offer(2_000_000));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, finding => finding.RuleCode == SigningValidator.RosterFullCode);
    }

    [Fact]
    public void ASigningThatWouldCrossTheHardCapIsRefusedWhateverRoutePaysForIt()
    {
        var league = SigningTestLeague.Build([90_000_000, 40_000_000, 19_000_000]);

        var assessment = Assess(league, league.Offer(2_000_000));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, finding => finding.RuleCode == SigningValidator.AboveHardCapCode);
    }

    /// <summary>
    /// A team still short of the payroll floor is not barred from signing — it is a team with
    /// spending still to do. A warning, therefore, and not a violation.
    /// </summary>
    [Fact]
    public void StillBeingUnderThePayrollFloorIsAWarningRatherThanARefusal()
    {
        var league = SigningTestLeague.Build([20_000_000, 10_000_000, 10_000_000]);

        var assessment = Assess(league, league.Offer(5_000_000));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
        Assert.Contains(assessment.Warnings, finding => finding.RuleCode == SigningValidator.BelowFloorAfterCode);
    }

    [Fact]
    public void APlayerAlreadyUnderContractCannotBeSigned()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);
        var contracted = Domain.Contracts.Contract.Create(
            new Domain.Contracts.ContractId("CONTRACT-EXISTING"),
            league.Team.Id,
            league.FreeAgent.Id,
            [new Domain.Contracts.ContractSeasonTerm(SigningTestLeague.CurrentSeason, new Money(5_000_000), new Money(5_000_000))]).Value;

        var context = league.Context() with { Contracts = [.. league.Contracts, contracted] };
        var assessment = Validator.Validate(league.Offer(10_000_000), context).Value;

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, finding => finding.RuleCode == SigningValidator.AlreadyUnderContractCode);
    }

    /// <summary>
    /// An offer for another team is a caller bug, not a rule outcome: there is no assessment to give
    /// back, so the result fails rather than returning a verdict about the wrong league.
    /// </summary>
    [Fact]
    public void AnOfferBelongingToAnotherTeamIsAStructuredFailureRatherThanAVerdict()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);
        var foreignOffer = Offer.Create(
            new OfferId("OFFER-ELSEWHERE"),
            new Domain.Teams.TeamId("TEAM-SOMEONE-ELSE"),
            league.FreeAgent.Id,
            [new Domain.Contracts.ContractSeasonTerm(SigningTestLeague.CurrentSeason, new Money(10_000_000), new Money(10_000_000))]).Value;

        var result = Validator.Validate(foreignOffer, league.Context());

        Assert.True(result.IsFailure);
        Assert.Equal(SigningValidator.TeamMismatchCode, Assert.Single(result.Errors).Code);
    }

    private static SigningAssessment Assess(SigningTestLeague league, Offer offer)
    {
        var result = Validator.Validate(offer, league.Context());
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static SigningRouteEvaluation Route(SigningAssessment assessment, SigningRouteKind kind) =>
        assessment.Routes.Single(route => route.Kind == kind);
}
