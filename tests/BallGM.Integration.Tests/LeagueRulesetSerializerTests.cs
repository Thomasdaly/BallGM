using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Infrastructure.Rulesets;
using BallGM.Rules.Configuration;

namespace BallGM.Integration.Tests;

public sealed class LeagueRulesetSerializerTests
{
    [Fact]
    public void RoundTripPreservesAConfiguredRuleset()
    {
        var serializer = new LeagueRulesetSerializer();
        var thresholds = CapThresholds.Create(
            payrollFloor: new Money(90_000),
            softCap: new Money(100_000),
            luxuryTax: new Money(120_000),
            firstApron: new Money(130_000),
            secondApron: new Money(140_000),
            hardCap: new Money(150_000)).Value;

        var ruleset = new LeagueRuleset(
            schemaVersion: LeagueRuleset.CurrentSchemaVersion,
            name: "Standard Fictional Ruleset",
            regularSeasonGameCount: 82,
            rosterLimits: new RosterSizeLimits(minimumPlayers: 12, maximumPlayers: 15),
            capThresholds: thresholds,
            draftRules: DraftRules.Create(roundCount: 2, lotteryEnabled: true, tradableFutureDraftHorizon: 5, retainedRoundNumber: 1, retainedRoundInterval: 2).Value,
            tradeRules: TestTradeRules,
            negotiationRules: NegotiationRules.Create(
                thresholds,
                maximumContractSeasons: 5,
                maximumIncumbentContractSeasons: 6,
                maximumAnnualEscalationPercent: 8,
                maximumAnnualDeescalationPercent: 8,
                CompensationCeilingScale.Create([new ScaleBand(0, 25), new ScaleBand(7, 30)]).Value,
                CompensationFloorScale.Create([new ScaleBand(0, 1_150), new ScaleBand(3, 2_100)]).Value,
                standardOverCapAllowance: new Money(12_800),
                standardOverCapAllowanceUnavailableAbove: CapThresholdKind.FirstApron,
                allowanceMaySplitAcrossPlayers: true,
                MarketResolutionMode.ResolutionPoint,
                offerExpiryDays: 3).Value,
            draftLotteryRules: DraftLotteryRules.Create([140, 125]).Value);

        var json = serializer.Serialize(ruleset);
        var result = serializer.Deserialize(json);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(ruleset.Name, result.Value.Name);
        Assert.Equal(ruleset.RegularSeasonGameCount, result.Value.RegularSeasonGameCount);
        Assert.Equal(ruleset.RosterLimits.MaximumPlayers, result.Value.RosterLimits.MaximumPlayers);
        Assert.Equal(ruleset.CapThresholds.HardCap?.SmallestUnits, result.Value.CapThresholds.HardCap?.SmallestUnits);
        Assert.Equal(90_000, result.Value.CapThresholds.PayrollFloor?.SmallestUnits);
        Assert.NotNull(result.Value.CapThresholds.HardCap);
        Assert.Equal(ruleset.DraftRules.RoundCount, result.Value.DraftRules.RoundCount);
        Assert.Equal(ruleset.DraftRules.TradableFutureDraftHorizon, result.Value.DraftRules.TradableFutureDraftHorizon);
        Assert.Equal(ruleset.DraftRules.RetainedRoundNumber, result.Value.DraftRules.RetainedRoundNumber);
        Assert.Equal(ruleset.DraftRules.RetainedRoundInterval, result.Value.DraftRules.RetainedRoundInterval);

        // The tier tables are the first ruleset content that is a table rather than a scalar, so the
        // round trip has to carry the bands themselves, not just their presence.
        Assert.Equal(5, result.Value.NegotiationRules.MaximumContractSeasons);
        Assert.Equal(6, result.Value.NegotiationRules.MaximumIncumbentContractSeasons);
        Assert.Equal(1_150, result.Value.NegotiationRules.CompensationFloor.FloorFor(0)?.SmallestUnits);
        Assert.Equal(2_100, result.Value.NegotiationRules.CompensationFloor.FloorFor(5)?.SmallestUnits);
        Assert.Equal(30, result.Value.NegotiationRules.CompensationCeiling.PercentFor(9));
        Assert.Equal(12_800, result.Value.NegotiationRules.StandardOverCapAllowance?.SmallestUnits);
        Assert.Equal(CapThresholdKind.FirstApron, result.Value.NegotiationRules.StandardOverCapAllowanceUnavailableAbove);
        Assert.Equal(MarketResolutionMode.ResolutionPoint, result.Value.NegotiationRules.MarketResolution);
        Assert.Equal(3, result.Value.NegotiationRules.OfferExpiryDays);
    }

    [Fact]
    public void DeserializeRejectsARulesetFileFromBeforeTheDraftRestrictionsExisted()
    {
        var serializer = new LeagueRulesetSerializer();
        var version1Json = """
            {
              "schemaVersion": 1,
              "name": "Pre-draft-asset Ruleset",
              "regularSeasonGameCount": 82,
              "minimumRosterPlayers": 12,
              "maximumRosterPlayers": 15,
              "softCap": 100000,
              "luxuryTax": 120000,
              "firstApron": 130000,
              "secondApron": 140000,
              "hardCap": 150000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true
            }
            """;

        var result = serializer.Deserialize(version1Json);

        // Defaulting the missing restrictions would run a league under rules its ruleset never
        // stated, so the file is refused instead.
        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DeserializeReturnsStructuredFailureForARetainedRoundTheDraftDoesNotHave()
    {
        var serializer = new LeagueRulesetSerializer();
        var envelopeJson = $$"""
            {
              "schemaVersion": {{LeagueRuleset.CurrentSchemaVersion}},
              "name": "Broken Ruleset",
              "regularSeasonGameCount": 82,
              "minimumRosterPlayers": 12,
              "maximumRosterPlayers": 15,
              "softCap": 100000,
              "luxuryTax": 120000,
              "firstApron": 130000,
              "secondApron": 140000,
              "hardCap": 150000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true,
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 4,
              "retainedRoundInterval": 2,
              "salaryMatchPercent": 125,
              "salaryMatchAllowance": 250000,
              "injuredPlayerTradeEligibility": "AllowedWithWarning",
              "secondApronBlocksSalaryIncrease": true
            }
            """;

        var result = serializer.Deserialize(envelopeJson);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.invalid_retained_round", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// The immediately previous version is byte-identical to the current one apart from the number:
    /// everything each bump has added is expressed by leaving a field out. There is no migration to
    /// run — but it is still refused, because the older reader would run a different rulebook than
    /// the file states. The error says what to change rather than only that it failed.
    /// </summary>
    [Fact]
    public void DeserializeRejectsTheImmediatelyPreviousVersionAndSaysHowToUpgradeIt()
    {
        var serializer = new LeagueRulesetSerializer();
        var previousVersionJson = $$"""
            {
              "schemaVersion": {{LeagueRuleset.CurrentSchemaVersion - 1}},
              "name": "Previous-version Ruleset",
              "regularSeasonGameCount": 82,
              "minimumRosterPlayers": 12,
              "maximumRosterPlayers": 15,
              "softCap": 100000,
              "luxuryTax": 120000,
              "firstApron": 130000,
              "secondApron": 140000,
              "hardCap": 150000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true,
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 1,
              "retainedRoundInterval": 2,
              "salaryMatchPercent": 125,
              "salaryMatchAllowance": 250000,
              "injuredPlayerTradeEligibility": "AllowedWithWarning",
              "secondApronBlocksSalaryIncrease": true
            }
            """;

        var result = serializer.Deserialize(previousVersionJson);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Errors);
        Assert.Equal("ruleset.unsupported_schema_version", error.Code);
        Assert.Contains($"only needs its schemaVersion changed to {LeagueRuleset.CurrentSchemaVersion}", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A league with no cap system, no draft, and no salary matching survives a round trip as a
    /// league that has none of those things — the fields stay absent rather than coming back as
    /// zeroes or as explicit nulls.
    /// </summary>
    [Fact]
    public void RoundTripPreservesALeagueThatHasNoCapNoDraftAndNoSalaryMatching()
    {
        var serializer = new LeagueRulesetSerializer();
        var ruleset = new LeagueRuleset(
            schemaVersion: LeagueRuleset.CurrentSchemaVersion,
            name: "Open League",
            regularSeasonGameCount: 34,
            rosterLimits: new RosterSizeLimits(10, 14),
            capThresholds: CapThresholds.Uncapped,
            draftRules: DraftRules.NoDraft,
            tradeRules: TradeRules.Create(
                salaryMatchPercent: null,
                salaryMatchAllowance: null,
                InjuredPlayerTradeEligibility.AllowedWithWarning,
                secondApronBlocksSalaryIncrease: false).Value,
            negotiationRules: NegotiationRules.OpenMarket);

        var json = serializer.Serialize(ruleset);

        Assert.DoesNotContain("softCap", json, StringComparison.Ordinal);
        Assert.DoesNotContain("draftRoundCount", json, StringComparison.Ordinal);
        Assert.DoesNotContain("salaryMatchPercent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("compensationFloorScale", json, StringComparison.Ordinal);
        Assert.DoesNotContain("maximumContractSeasons", json, StringComparison.Ordinal);

        var result = serializer.Deserialize(json);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CapThresholds.IsUncapped);
        Assert.False(result.Value.DraftRules.HasDraft);
        Assert.False(result.Value.TradeRules.HasSalaryMatching);

        // An open market: no term limit, no minimum, no maximum, and no allowance — and emphatically
        // not "no signings", which is the reading this whole section exists to refuse.
        Assert.Null(result.Value.NegotiationRules.MaximumContractSeasons);
        Assert.False(result.Value.NegotiationRules.CompensationFloor.IsConfigured);
        Assert.False(result.Value.NegotiationRules.CompensationCeiling.IsConfigured);
        Assert.False(result.Value.NegotiationRules.HasStandardOverCapAllowance);
    }

    [Fact]
    public void DeserializeReturnsStructuredFailureForMalformedJsonInsteadOfThrowing()
    {
        var serializer = new LeagueRulesetSerializer();

        var result = serializer.Deserialize("{ not valid json");

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.malformed_file", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DeserializeReturnsStructuredFailureForOutOfOrderCapThresholdsInsteadOfThrowing()
    {
        var serializer = new LeagueRulesetSerializer();
        var envelopeJson = $$"""
            {
              "schemaVersion": {{LeagueRuleset.CurrentSchemaVersion}},
              "name": "Broken Ruleset",
              "regularSeasonGameCount": 82,
              "minimumRosterPlayers": 12,
              "maximumRosterPlayers": 15,
              "softCap": 150000,
              "luxuryTax": 120000,
              "firstApron": 130000,
              "secondApron": 140000,
              "hardCap": 150000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true,
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 1,
              "retainedRoundInterval": 2,
              "salaryMatchPercent": 125,
              "salaryMatchAllowance": 250000,
              "injuredPlayerTradeEligibility": "AllowedWithWarning",
              "secondApronBlocksSalaryIncrease": true
            }
            """;

        var result = serializer.Deserialize(envelopeJson);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.cap_thresholds_out_of_order", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DeserializeReturnsStructuredFailureForInvalidRosterLimitsInsteadOfThrowing()
    {
        var serializer = new LeagueRulesetSerializer();
        var envelopeJson = $$"""
            {
              "schemaVersion": {{LeagueRuleset.CurrentSchemaVersion}},
              "name": "Broken Ruleset",
              "regularSeasonGameCount": 82,
              "minimumRosterPlayers": 20,
              "maximumRosterPlayers": 15,
              "softCap": 100000,
              "luxuryTax": 120000,
              "firstApron": 130000,
              "secondApron": 140000,
              "hardCap": 150000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true,
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 1,
              "retainedRoundInterval": 2,
              "salaryMatchPercent": 125,
              "salaryMatchAllowance": 250000,
              "injuredPlayerTradeEligibility": "AllowedWithWarning",
              "secondApronBlocksSalaryIncrease": true
            }
            """;

        var result = serializer.Deserialize(envelopeJson);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.invalid_field", Assert.Single(result.Errors).Code);
    }

    private static TradeRules TestTradeRules => TradeRules.Create(
        salaryMatchPercent: 125,
        new Money(250_000),
        InjuredPlayerTradeEligibility.AllowedWithWarning,
        secondApronBlocksSalaryIncrease: true).Value;
}
