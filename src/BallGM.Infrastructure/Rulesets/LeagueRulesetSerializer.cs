using System.Text.Json;
using BallGM.Domain.Common;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Infrastructure.Rulesets;

/// <summary>
/// Loads and saves the league ruleset file. This is the concrete answer to "rule changes
/// shouldn't require waiting for the next release": swapping this file changes cap thresholds,
/// draft structure, and schedule length without a code change. Because the file is untrusted
/// input, <see cref="Deserialize"/> never throws on malformed content — a bad file produces a
/// structured, explainable failure instead of crashing the load.
/// </summary>
public sealed class LeagueRulesetSerializer
{
    private const string MalformedFileCode = "ruleset.malformed_file";
    private const string InvalidFieldCode = "ruleset.invalid_field";
    private const string UnsupportedSchemaVersionCode = "ruleset.unsupported_schema_version";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Serialize(LeagueRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        var envelope = new LeagueRulesetEnvelope(
            ruleset.SchemaVersion,
            ruleset.Name,
            ruleset.RegularSeasonGameCount,
            ruleset.RosterLimits.MinimumPlayers,
            ruleset.RosterLimits.MaximumPlayers,
            ruleset.CapThresholds.SoftCap.SmallestUnits,
            ruleset.CapThresholds.LuxuryTax.SmallestUnits,
            ruleset.CapThresholds.FirstApron.SmallestUnits,
            ruleset.CapThresholds.SecondApron.SmallestUnits,
            ruleset.CapThresholds.HardCap.SmallestUnits,
            ruleset.DraftRules.RoundCount,
            ruleset.DraftRules.LotteryEnabled,
            ruleset.DraftRules.TradableFutureDraftHorizon,
            ruleset.DraftRules.RetainedRoundNumber,
            ruleset.DraftRules.RetainedRoundInterval);

        return JsonSerializer.Serialize(envelope, Options);
    }

    public DomainOperationResult<LeagueRuleset> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        LeagueRulesetEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LeagueRulesetEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(
                new DomainError(MalformedFileCode, $"The league ruleset file is not valid JSON: {exception.Message}"));
        }

        if (envelope is null)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(
                new DomainError(MalformedFileCode, "The league ruleset file did not contain a ruleset."));
        }

        // A ruleset file this build cannot read fails structurally rather than loading with fields
        // this build invented. Draft-asset restrictions arrived in version 2, and a league silently
        // running restrictions its ruleset never stated is the failure mode worth refusing.
        if (envelope.SchemaVersion != LeagueRuleset.CurrentSchemaVersion)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(
                new DomainError(
                    UnsupportedSchemaVersionCode,
                    $"League ruleset schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {LeagueRuleset.CurrentSchemaVersion}."));
        }

        try
        {
            var capThresholdsResult = CapThresholds.Create(
                new Money(envelope.SoftCap),
                new Money(envelope.LuxuryTax),
                new Money(envelope.FirstApron),
                new Money(envelope.SecondApron),
                new Money(envelope.HardCap));

            if (capThresholdsResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(capThresholdsResult.Errors.ToArray());
            }

            var ruleset = new LeagueRuleset(
                envelope.SchemaVersion,
                envelope.Name,
                envelope.RegularSeasonGameCount,
                new RosterSizeLimits(envelope.MinimumRosterPlayers, envelope.MaximumRosterPlayers),
                capThresholdsResult.Value,
                new DraftRules(
                    envelope.DraftRoundCount,
                    envelope.DraftLotteryEnabled,
                    envelope.TradableFutureDraftHorizon,
                    envelope.RetainedRoundNumber,
                    envelope.RetainedRoundInterval));

            return DomainOperationResult<LeagueRuleset>.Success(ruleset);
        }
        catch (ArgumentException exception)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(new DomainError(InvalidFieldCode, exception.Message));
        }
    }
}
