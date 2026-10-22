using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BallGM.Application.Packs;
using BallGM.Mods.Manifests;

namespace BallGM.Mods.Settings;

/// <summary>A preset's ruleset with a setup's overrides applied, and what was said about each override.</summary>
public sealed record ResolvedSettings(string? RulesetJson, IReadOnlyList<PackFinding> Findings);

/// <summary>
/// Applies a setup's setting overrides to a preset's ruleset: one dotted key at a time, each checked
/// against <see cref="LeagueSettingCatalogue"/> before it is written, so a typo is "did you mean
/// softCap?" rather than the ruleset reader's "unknown field". <c>null</c> switches an optional setting
/// off. The result is still only a candidate — the ruleset reader decides whether it is a league.
/// </summary>
public static class LeagueSettingsResolver
{
    public static ResolvedSettings Resolve(string baseRulesetJson, IReadOnlyDictionary<string, JsonElement> overrides, string? rulesetPackId = null)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var findings = new List<PackFinding>();

        JsonObject? ruleset;
        try
        {
            ruleset = JsonNode.Parse(baseRulesetJson, documentOptions: PackJson.DocumentOptions) as JsonObject;
        }
        catch (JsonException exception)
        {
            findings.Add(PackFinding.Error("settings.preset_malformed", PackCheckStage.Settings, $"The preset's ruleset file is not valid JSON: {exception.Message}", rulesetPackId));
            return new ResolvedSettings(null, findings);
        }

        if (ruleset is null)
        {
            findings.Add(PackFinding.Error("settings.preset_malformed", PackCheckStage.Settings, "The preset's ruleset file does not hold a JSON object.", rulesetPackId));
            return new ResolvedSettings(null, findings);
        }

        foreach (var (key, value) in overrides.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var setting = LeagueSettingCatalogue.Find(key);
            if (setting is null)
            {
                var suggestion = LeagueSettingCatalogue.Suggest(key);
                findings.Add(PackFinding.Error(
                    "settings.unknown_setting",
                    PackCheckStage.Settings,
                    $"'{key}' is not a league setting.",
                    fix: suggestion is null ? "Run 'ballgm-packs settings' for the full list." : $"Did you mean '{suggestion}'?"));
                continue;
            }

            var before = Read(ruleset, setting.Path);
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (!setting.Optional)
                {
                    findings.Add(PackFinding.Error("settings.not_optional", PackCheckStage.Settings, $"'{key}' ({setting.Label}) cannot be switched off: every league has one."));
                    continue;
                }

                Remove(ruleset, setting.Path);
                findings.Add(PackFinding.Info("settings.override_applied", PackCheckStage.Settings, $"{setting.Label} ({key}): {Describe(before)} → off."));
                continue;
            }

            var problem = Check(setting, value);
            if (problem is not null)
            {
                findings.Add(PackFinding.Error("settings.invalid_value", PackCheckStage.Settings, $"'{key}' ({setting.Label}): {problem}"));
                continue;
            }

            Write(ruleset, setting.Path, JsonNode.Parse(value.GetRawText()));
            findings.Add(PackFinding.Info("settings.override_applied", PackCheckStage.Settings, $"{setting.Label} ({key}): {Describe(before)} → {Describe(value)}."));
        }

        return new ResolvedSettings(ruleset.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), findings);
    }

    /// <summary>What is wrong with <paramref name="value"/> for <paramref name="setting"/>, or null when it is the right shape.</summary>
    public static string? Check(LeagueSetting setting, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        switch (setting.Kind)
        {
            case SettingValueKind.Integer or SettingValueKind.Money or SettingValueKind.Percent or SettingValueKind.BasisPoints:
                return CheckNumber(setting, value);

            case SettingValueKind.Boolean:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "expected true or false.";

            case SettingValueKind.Text:
                return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? null : "expected some text.";

            case SettingValueKind.Choice:
                return value.ValueKind == JsonValueKind.String && setting.Choices.Contains(value.GetString()!, StringComparer.Ordinal)
                    ? null
                    : $"expected one of {string.Join(", ", setting.Choices)}.";

            case SettingValueKind.IntegerList:
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                {
                    return "expected a list of whole numbers.";
                }

                return value.EnumerateArray().Select(item => CheckNumber(setting, item)).FirstOrDefault(problem => problem is not null);

            case SettingValueKind.ChoiceList:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    return $"expected a list drawn from {string.Join(", ", setting.Choices)}.";
                }

                var names = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToList();
                if (names.Any(name => name is null || !setting.Choices.Contains(name, StringComparer.Ordinal)))
                {
                    return $"every entry must be one of {string.Join(", ", setting.Choices)}.";
                }

                return names.Distinct(StringComparer.Ordinal).Count() == names.Count ? null : "an entry appears more than once.";

            case SettingValueKind.Table:
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                {
                    return $"expected a list of rows, each with {string.Join(", ", setting.Columns)}.";
                }

                foreach (var row in value.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object)
                    {
                        return $"every row must be an object with {string.Join(", ", setting.Columns)}.";
                    }

                    var unknown = row.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => !setting.Columns.Contains(name, StringComparer.Ordinal));
                    if (unknown is not null)
                    {
                        return $"'{unknown}' is not a column; rows carry {string.Join(", ", setting.Columns)}.";
                    }
                }

                return null;

            case SettingValueKind.Section:
                return "a whole section can only be switched off (null); set its settings one by one instead.";

            default:
                return "this setting cannot be overridden.";
        }
    }

    private static string? CheckNumber(LeagueSetting setting, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            return setting.Kind == SettingValueKind.Money ? "expected a whole amount in the currency's smallest units." : "expected a whole number.";
        }

        if (setting.Minimum is long minimum && number < minimum)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{number:N0} is below the least this setting can be ({minimum:N0}).");
        }

        return setting.Maximum is long maximum && number > maximum
            ? string.Create(CultureInfo.InvariantCulture, $"{number:N0} is above the most this setting can be ({maximum:N0}).")
            : null;
    }

    private static JsonNode? Read(JsonObject root, IReadOnlyList<string> path)
    {
        JsonNode? node = root;
        foreach (var segment in path)
        {
            node = (node as JsonObject)?[segment];
        }

        return node;
    }

    private static void Write(JsonObject root, IReadOnlyList<string> path, JsonNode? value)
    {
        var parent = root;
        foreach (var segment in path.Take(path.Count - 1))
        {
            if (parent[segment] is not JsonObject child)
            {
                child = [];
                parent[segment] = child;
            }

            parent = child;
        }

        parent[path[^1]] = value;
    }

    private static void Remove(JsonObject root, IReadOnlyList<string> path)
    {
        var parent = path.Count == 1 ? root : Read(root, path.Take(path.Count - 1).ToList()) as JsonObject;
        parent?.Remove(path[^1]);
    }

    private static string Describe(JsonNode? node) => node is null ? "off" : Compact(node.ToJsonString());

    private static string Describe(JsonElement element) => Compact(element.GetRawText());

    private static string Compact(string json)
    {
        var text = string.Join(' ', json.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
        return text.Length <= 80 ? text : text[..77] + "...";
    }
}
