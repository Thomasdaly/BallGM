using System.Reflection;
using System.Text.Json;
using BallGM.Domain.Cap;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Trades;
using BallGM.Infrastructure.Rulesets;
using BallGM.Mods.Settings;
using BallGM.Rules.Configuration;

namespace BallGM.Integration.Tests.Packs;

/// <summary>
/// The settings catalogue against the ruleset format, both ways round: every field the ruleset file
/// can state is a setting with prose, and every setting names a real field. A new rule cannot reach the
/// format without someone writing down what it means, and a stale entry cannot linger after a rule goes.
/// </summary>
public sealed class LeagueSettingCatalogueTests
{
    private static readonly Type[] Sections = [typeof(CapMechanicsEnvelope), typeof(GameRulesEnvelope), typeof(StatLeadersEnvelope)];

    [Fact]
    public void Catalogue_HasOneEntryForEveryRulesetField()
    {
        var fields = FieldKeys(typeof(LeagueRulesetEnvelope), prefix: string.Empty).Except(LeagueSettingCatalogue.NotSettings).Order(StringComparer.Ordinal).ToList();
        var settings = LeagueSettingCatalogue.All.Select(setting => setting.Key).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(fields, settings);
    }

    [Fact]
    public void Catalogue_EverySettingHasALabelAGroupAndASentence()
    {
        Assert.All(LeagueSettingCatalogue.All, setting =>
        {
            Assert.False(string.IsNullOrWhiteSpace(setting.Label), setting.Key);
            Assert.False(string.IsNullOrWhiteSpace(setting.Group), setting.Key);
            Assert.EndsWith(".", setting.Description.TrimEnd(')'), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Catalogue_ChoicesAreExactlyTheNamesTheRulesetReaderAccepts()
    {
        Assert.Equal(Enum.GetNames<InjuredPlayerTradeEligibility>(), LeagueSettingCatalogue.InjuredTradeNames);
        Assert.Equal(Enum.GetNames<MarketResolutionMode>(), LeagueSettingCatalogue.MarketResolutionNames);
        Assert.Equal(Enum.GetNames<StandingsTieBreak>(), LeagueSettingCatalogue.TieBreakNames);
        Assert.Equal(Enum.GetNames<AwardStatBasis>(), LeagueSettingCatalogue.AwardStatBasisNames);
        Assert.Equal(Enum.GetNames<CapThresholdKind>().Order(StringComparer.Ordinal), LeagueSettingCatalogue.ThresholdNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Catalogue_OnlySettingsTheReaderTreatsAsOptionalCanBeSwitchedOff()
    {
        var required = typeof(LeagueRulesetEnvelope).GetConstructors().Single().GetParameters()
            .Where(parameter => !parameter.HasDefaultValue && Nullable.GetUnderlyingType(parameter.ParameterType) is null && !IsReference(parameter))
            .Select(parameter => parameter.Name!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var setting in LeagueSettingCatalogue.All.Where(setting => required.Contains(setting.Key)))
        {
            Assert.False(setting.Optional, $"{setting.Key} is required by the ruleset reader but the catalogue lets it be switched off.");
        }
    }

    [Fact]
    public void Resolver_AppliesOverridesThatTheRulesetReaderThenAccepts()
    {
        var overrides = Overrides("""
            {
              "softCap": 170000000,
              "hardCap": null,
              "capMechanics.taxBracketSize": 5000000,
              "capMechanics.taxRatesPercent": [150, 175, 250, 325],
              "gameRules.personalFoulLimit": 5,
              "standingsTieBreaks": ["PointDifferential"]
            }
            """);

        var resolved = LeagueSettingsResolver.Resolve(File.ReadAllText(DefaultRuleset), overrides);

        Assert.DoesNotContain(resolved.Findings, finding => finding.Severity == Application.Packs.PackFindingSeverity.Error);
        var ruleset = new LeagueRulesetSerializer().Deserialize(resolved.RulesetJson!);
        Assert.True(ruleset.IsSuccess, string.Join("; ", ruleset.Errors.Select(error => error.Message)));
        Assert.Equal(170_000_000, ruleset.Value.CapThresholds.SoftCap!.SmallestUnits);
        Assert.Null(ruleset.Value.CapThresholds.HardCap);
        Assert.Equal(5, ruleset.Value.GameRules.PersonalFoulLimit);
        Assert.Equal(6, resolved.Findings.Count(finding => finding.Code == "settings.override_applied"));
    }

    [Theory]
    [InlineData("""{ "regularSeasonGameCount": null }""", "settings.not_optional")]
    [InlineData("""{ "softCap": "lots" }""", "settings.invalid_value")]
    [InlineData("""{ "maximumRosterPlayers": 400 }""", "settings.invalid_value")]
    [InlineData("""{ "marketResolution": "Auction" }""", "settings.invalid_value")]
    [InlineData("""{ "standingsTieBreaks": ["PointDifferential", "PointDifferential"] }""", "settings.invalid_value")]
    [InlineData("""{ "compensationFloorScale": [{ "minimumSeasons": 0, "amount": 1 }] }""", "settings.invalid_value")]
    [InlineData("""{ "capMechanics": { "taxBracketSize": 1 } }""", "settings.invalid_value")]
    [InlineData("""{ "luxuryTaxLine": 1 }""", "settings.unknown_setting")]
    public void Resolver_RefusesAnOverrideOfTheWrongShapeWithAReason(string json, string code)
    {
        var resolved = LeagueSettingsResolver.Resolve(File.ReadAllText(DefaultRuleset), Overrides(json));

        Assert.Equal(code, Assert.Single(resolved.Findings).Code);
    }

    [Fact]
    public void Resolver_SwitchingASectionOffRemovesIt()
    {
        var withSection = Overrides("""{ "gameRules.personalFoulLimit": 5 }""");
        var resolved = LeagueSettingsResolver.Resolve(File.ReadAllText(DefaultRuleset), withSection);
        var removed = LeagueSettingsResolver.Resolve(resolved.RulesetJson!, Overrides("""{ "gameRules": null }"""));

        Assert.DoesNotContain("gameRules", removed.RulesetJson!, StringComparison.Ordinal);
    }

    [Fact]
    public void Suggest_FindsTheSettingAPlayerMostLikelyMeant()
    {
        Assert.Equal("softCap", LeagueSettingCatalogue.Suggest("softcap"));
        Assert.Equal("maximumRosterPlayers", LeagueSettingCatalogue.Suggest("maximumRosterPlayer"));
        Assert.Null(LeagueSettingCatalogue.Suggest("teamColourOfTheMonth"));
    }

    private static string DefaultRuleset => Path.Combine(AppContext.BaseDirectory, "data", "rulesets", "default-league.json");

    private static Dictionary<string, JsonElement> Overrides(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static IEnumerable<string> FieldKeys(Type envelope, string prefix)
    {
        foreach (var property in envelope.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var key = prefix + JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (Sections.Contains(property.PropertyType))
            {
                yield return key;
                foreach (var nested in FieldKeys(property.PropertyType, key + "."))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return key;
            }
        }
    }

    private static bool IsReference(ParameterInfo parameter) =>
        !parameter.ParameterType.IsValueType && new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;
}
