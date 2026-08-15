using BallGM.Domain.Common;
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
        var ruleset = new LeagueRuleset(
            schemaVersion: LeagueRuleset.CurrentSchemaVersion,
            name: "Standard Fictional Ruleset",
            regularSeasonGameCount: 82,
            rosterLimits: new RosterSizeLimits(minimumPlayers: 12, maximumPlayers: 15),
            capThresholds: CapThresholds.Create(
                new Money(100_000),
                new Money(120_000),
                new Money(130_000),
                new Money(140_000),
                new Money(150_000)).Value,
            draftRules: new DraftRules(roundCount: 2, lotteryEnabled: true, tradableFutureDraftHorizon: 5, retainedRoundNumber: 1, retainedRoundInterval: 2),
            tradeRules: TestTradeRules);

        var json = serializer.Serialize(ruleset);
        var result = serializer.Deserialize(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(ruleset.Name, result.Value.Name);
        Assert.Equal(ruleset.RegularSeasonGameCount, result.Value.RegularSeasonGameCount);
        Assert.Equal(ruleset.RosterLimits.MaximumPlayers, result.Value.RosterLimits.MaximumPlayers);
        Assert.Equal(ruleset.CapThresholds.HardCap.SmallestUnits, result.Value.CapThresholds.HardCap.SmallestUnits);
        Assert.Equal(ruleset.DraftRules.RoundCount, result.Value.DraftRules.RoundCount);
        Assert.Equal(ruleset.DraftRules.TradableFutureDraftHorizon, result.Value.DraftRules.TradableFutureDraftHorizon);
        Assert.Equal(ruleset.DraftRules.RetainedRoundNumber, result.Value.DraftRules.RetainedRoundNumber);
        Assert.Equal(ruleset.DraftRules.RetainedRoundInterval, result.Value.DraftRules.RetainedRoundInterval);
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
        var envelopeJson = """
            {
              "schemaVersion": 3,
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
        Assert.Equal("ruleset.invalid_field", Assert.Single(result.Errors).Code);
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
        var envelopeJson = """
            {
              "schemaVersion": 3,
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
        var envelopeJson = """
            {
              "schemaVersion": 3,
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
