using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Rulesets;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;
using BallGM.Rules.DraftAssets;

namespace BallGM.Integration.Tests;

/// <summary>
/// Measures how generic <see cref="LeagueRuleset"/> actually is, using a second league that the
/// schema was not written for. The fixture lives in <c>data/rulesets/conformance/</c> and describes
/// a league with no salary cap system and no draft — see the README beside it.
/// <para>
/// These tests used to pin the four gaps the fixture exposed: an absent cap system loading as a cap
/// system of zero, absence and zero being the same input, a draftless league being inexpressible,
/// and <c>StandingFor</c> throwing for a threshold the league does not have. Schema version 4 closed
/// all four, so each assertion below now states the fixed behaviour instead. The record of what was
/// wrong lives in <c>docs/negotiation-mechanisms.md</c> → "Ruleset genericity", marked closed.
/// </para>
/// </summary>
public sealed class RulesetConformanceTests
{
    private static readonly Season Season = new(2031);
    private static readonly TeamId Team = new(SortableId.NewId());

    [Fact]
    public void UncappedLeagueFixture_LoadsAtTheCurrentSchemaVersion()
    {
        var result = new LeagueRulesetSerializer().Deserialize(ReadFixture());

        Assert.True(result.IsSuccess);
        Assert.Equal("Meridian Open League — Uncapped Rules", result.Value.Name);
        Assert.Equal(LeagueRuleset.CurrentSchemaVersion, result.Value.SchemaVersion);
    }

    /// <summary>
    /// The gap that mattered most: an absent cap system used to load as a cap system of zero, which
    /// satisfied the ordering check and left every team over every line. Absence is now absence.
    /// </summary>
    [Fact]
    public void UncappedLeagueFixture_HasNoThresholdsRatherThanThresholdsOfZero()
    {
        var ruleset = new LeagueRulesetSerializer().Deserialize(ReadFixture()).Value;

        Assert.True(ruleset.CapThresholds.IsUncapped);
        Assert.Null(ruleset.CapThresholds.SoftCap);
        Assert.Null(ruleset.CapThresholds.HardCap);
        Assert.Null(ruleset.CapThresholds.PayrollFloor);
    }

    /// <summary>
    /// The cap sheet in a league with no cap: a real payroll, and no standings at all. Five zeroes
    /// every team was over is exactly what this is not.
    /// </summary>
    [Fact]
    public void AnUncappedLeagueProducesAPayrollAndNoStandings()
    {
        var sheet = new CapLedger()
            .Evaluate(Team, Season, [ActiveCharge(2_000_000)], CapThresholds.Uncapped)
            .Value;

        Assert.Equal(2_000_000, sheet.TotalPayroll.SmallestUnits);
        Assert.Empty(sheet.Thresholds);
        Assert.Null(sheet.StandingFor(CapThresholdKind.HardCap));
    }

    /// <summary>
    /// "This rule does not apply" and "someone typed a zero" used to be the same input. Absence now
    /// means the league has no salary matching; a value that is present and below 100 is still read
    /// as the data-pack typo it always was.
    /// </summary>
    [Fact]
    public void UncappedLeagueFixture_HasNoSalaryMatchingRatherThanAnInvalidOne()
    {
        var ruleset = new LeagueRulesetSerializer().Deserialize(ReadFixture()).Value;

        Assert.False(ruleset.TradeRules.HasSalaryMatching);
        Assert.Null(ruleset.TradeRules.SalaryMatchPercent);
    }

    [Fact]
    public void APresentSalaryMatchPercentageBelowOneHundredIsStillReadAsATypo()
    {
        var json = ReadFixture().Replace(
            "\"injuredPlayerTradeEligibility\"",
            "\"salaryMatchPercent\": 0,\n  \"injuredPlayerTradeEligibility\"",
            StringComparison.Ordinal);

        var result = new LeagueRulesetSerializer().Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.invalid_salary_match_percent", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// A league with no draft is now configurable, and a franchise in it cannot be handed a pick:
    /// an asset no draft will ever select with is not an asset.
    /// </summary>
    [Fact]
    public void ADraftlessLeagueLoadsAndRefusesToRegisterAPick()
    {
        var ruleset = new LeagueRulesetSerializer().Deserialize(ReadFixture()).Value;

        Assert.False(ruleset.DraftRules.HasDraft);
        Assert.Equal(0, ruleset.DraftRules.RoundCount);

        var leagueId = new LeagueId(SortableId.NewId());
        var pick = DraftPick.Create(
            new DraftPickId(SortableId.NewId()),
            leagueId,
            new Season(Season.Year + 1),
            round: 1,
            new FranchiseId(SortableId.NewId())).Value;

        var result = new PickOwnershipRules().ValidateRegistration(pick, ruleset.DraftRules);

        Assert.True(result.IsFailure);
        Assert.Equal("pick_registration.league_has_no_draft", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// The roster and schedule half of the schema was already generic. Still worth pinning: it is
    /// the evidence that the four gaps were specific to the cap and draft systems rather than a
    /// general problem with the ruleset design.
    /// </summary>
    [Fact]
    public void UncappedLeagueFixture_RosterAndScheduleValuesAreExpressible()
    {
        var ruleset = new LeagueRulesetSerializer().Deserialize(ReadFixture()).Value;

        Assert.Equal(10, ruleset.RosterLimits.MinimumPlayers);
        Assert.Equal(14, ruleset.RosterLimits.MaximumPlayers);
        Assert.Equal(34, ruleset.RegularSeasonGameCount);
    }

    /// <summary>
    /// The whole way through, not just at the ruleset boundary: the fixture league built on this
    /// ruleset loads, produces cap sheets with a payroll and no standings, and offers no picks.
    /// This is the path the client takes, so it is the path that has to be true.
    /// </summary>
    [Fact]
    public void TheUncappedLeagueLoadsAllTheWayToTheReadModel()
    {
        var rulesetPath = Path.Combine(
            AppContext.BaseDirectory, "data", "rulesets", "conformance", "uncapped-open-league.json");

        var result = new GetLeagueOverviewQuery(
            new FixtureLeagueDataSource(rulesetPath),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var overview = result.Value;
        Assert.True(overview.CapThresholds.IsUncapped);
        Assert.Null(overview.CapThresholds.SoftCap);
        Assert.Equal(0, overview.PickBoard.DraftCount);
        Assert.Empty(overview.PickBoard.Franchises.SelectMany(row => row.Drafts));

        Assert.All(overview.Teams, team =>
        {
            Assert.Empty(team.CapSheet.Thresholds);
            Assert.True(team.CapSheet.TotalPayroll > 0);
        });
    }

    /// <summary>
    /// The negotiation section is absent in full, and that is a league rather than a gap: no term
    /// limit, no minimum, no maximum, no over-cap allowance. What it is <em>not</em> is a league
    /// where nobody may sign — signing is a capability and the routes are what gate it.
    /// </summary>
    [Fact]
    public void UncappedLeagueFixture_HasAnOpenMarketRatherThanNoSignings()
    {
        var ruleset = new LeagueRulesetSerializer().Deserialize(ReadFixture()).Value;
        var rules = ruleset.NegotiationRules;

        Assert.Null(rules.MaximumContractSeasons);
        Assert.False(rules.HasTermLimit);
        Assert.False(rules.HasEscalationLimit);
        Assert.False(rules.HasStandardOverCapAllowance);
        Assert.False(rules.CompensationFloor.IsConfigured);
        Assert.False(rules.CompensationCeiling.IsConfigured);
    }

    /// <summary>
    /// Free agency in a league that measures payroll against nothing. Any team may pay anyone
    /// anything — and the screen says so as a route that permits, not as the absence of a refusal,
    /// because "you may pay him whatever you like" is what a GM here needs told.
    /// </summary>
    [Fact]
    public void InTheUncappedLeagueAnyTeamMayPayAnyoneAnything()
    {
        var session = UncappedSession(out var overview);
        var team = overview.Teams[0];
        var player = overview.FreeAgents.Players[0];

        // No line to be measured against, so no range is quoted either — nullable on both ends.
        Assert.Null(player.MinimumSalary);
        Assert.Null(player.MaximumSalary);
        Assert.False(overview.FreeAgents.LeagueHasCompensationFloor);
        Assert.False(overview.FreeAgents.LeagueHasCompensationCeiling);
        Assert.Null(overview.FreeAgents.MaximumContractSeasons);

        var extravagant = new OfferRequest(
            team.TeamId,
            player.PlayerId,
            Enumerable.Range(0, 9)
                .Select(index => new OfferSeasonRequest(2031 + index, 400_000_000, 400_000_000))
                .ToList());

        var assessment = session.AssessOffer(extravagant);
        Assert.True(assessment.IsSuccess, string.Join("; ", assessment.Errors.Select(error => error.Message)));

        var verdict = assessment.Value;
        Assert.True(verdict.IsLegal, string.Join("; ", verdict.Violations.Select(v => v.Explanation)));
        Assert.Equal("Unrestricted signing", verdict.PermittingRouteName);
        Assert.Null(verdict.CapRoomBefore);
    }

    /// <summary>
    /// Every route that needs a line this league does not have reports as inapplicable rather than as
    /// a refusal, and the note says which line is missing. A check that never ran has to stay
    /// distinguishable from a check that ran and approved.
    /// </summary>
    [Fact]
    public void InTheUncappedLeagueTheOtherRoutesSayTheLeagueHasNoSuchLineRatherThanRefusing()
    {
        var session = UncappedSession(out var overview);
        var team = overview.Teams[0];
        var player = overview.FreeAgents.Players[0];

        var verdict = session.AssessOffer(new OfferRequest(
            team.TeamId,
            player.PlayerId,
            [new OfferSeasonRequest(2031, 25_000_000, 25_000_000)])).Value;

        var byName = verdict.Routes.ToDictionary(route => route.RouteName);

        Assert.True(byName["Unrestricted signing"].Applicable);
        Assert.False(byName["Cap room"].Applicable);
        Assert.False(byName["Minimum salary"].Applicable);
        Assert.False(byName["Standard over-cap allowance"].Applicable);

        Assert.Equal("signing.route_needs_soft_cap", byName["Cap room"].RuleCode);
        Assert.Equal("signing.route_needs_compensation_floor", byName["Minimum salary"].RuleCode);
        Assert.Equal("signing.route_needs_allowance", byName["Standard over-cap allowance"].RuleCode);

        // Notes, not warnings: nothing is wrong with a league that sets no ceiling, and styling it as
        // a caution would say otherwise.
        Assert.Empty(verdict.Violations);
        Assert.Contains(verdict.Notes, note => note.RuleCode == "offer.no_term_limit_configured");
        Assert.Contains(verdict.Notes, note => note.RuleCode == "offer.no_compensation_ceiling_configured");
        Assert.Contains(verdict.Notes, note => note.RuleCode == "offer.no_compensation_floor_configured");
        Assert.Contains(verdict.Notes, note => note.RuleCode == "offer.no_escalation_limit_configured");
        Assert.Contains(verdict.Notes, note => note.RuleCode == "signing.no_hard_cap_configured");
    }

    /// <summary>
    /// A league with no minimum salary has no honest figure to reserve for an empty roster spot, so
    /// it produces no holds — rather than holds of nought, which would be a rule saying "nothing".
    /// </summary>
    [Fact]
    public void TheUncappedLeagueCarriesNoRosterSlotHoldsBecauseItHasNoMinimumToReserve()
    {
        UncappedSession(out var overview);

        Assert.Contains(overview.Teams, team => team.RosterCount < overview.MinimumRosterPlayers);
        Assert.All(overview.Teams, team => Assert.Equal(0, team.CapSheet.RosterHolds));
    }

    private static LeagueSession UncappedSession(out LeagueOverview overview)
    {
        var rulesetPath = Path.Combine(
            AppContext.BaseDirectory, "data", "rulesets", "conformance", "uncapped-open-league.json");

        var session = new LeagueSession(
            new FixtureLeagueDataSource(rulesetPath),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine());

        var result = session.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        overview = result.Value;
        return session;
    }

    private static CapCharge ActiveCharge(long amount) =>
        CapCharge.ActiveContract(
            Team,
            Season,
            new PlayerId(SortableId.NewId()),
            new ContractId(SortableId.NewId()),
            new Money(amount));

    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "rulesets",
            "conformance",
            "uncapped-open-league.json"));
}
