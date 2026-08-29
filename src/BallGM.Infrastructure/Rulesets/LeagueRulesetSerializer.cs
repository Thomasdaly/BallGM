using System.Text.Json;
using System.Text.Json.Serialization;
using BallGM.Domain.Cap;
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
        WriteIndented = true,

        // Absence is meaningful in this format: a league with no soft cap omits the field rather
        // than writing a zero. Writing nulls back out would round-trip "no cap system" as an
        // explicit null, which reads as a third state nobody defined.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // A field this build does not know is a rule this build cannot run. Ignoring it — the
        // default — is how a file that states a term limit gets loaded by a reader that enforces
        // none, which is the version-gate failure arriving through a side door: the schema version
        // catches a file from a build we know about, and this catches one from a build we do not.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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
            ruleset.CapThresholds.PayrollFloor?.SmallestUnits,
            ruleset.CapThresholds.SoftCap?.SmallestUnits,
            ruleset.CapThresholds.LuxuryTax?.SmallestUnits,
            ruleset.CapThresholds.FirstApron?.SmallestUnits,
            ruleset.CapThresholds.SecondApron?.SmallestUnits,
            ruleset.CapThresholds.HardCap?.SmallestUnits,
            ruleset.DraftRules.HasDraft ? ruleset.DraftRules.RoundCount : null,
            ruleset.DraftRules.LotteryEnabled,
            ruleset.DraftRules.HasDraft ? ruleset.DraftRules.TradableFutureDraftHorizon : null,
            ruleset.DraftRules.HasDraft ? ruleset.DraftRules.RetainedRoundNumber : null,
            ruleset.DraftRules.HasDraft ? ruleset.DraftRules.RetainedRoundInterval : null,
            ruleset.TradeRules.SalaryMatchPercent,
            ruleset.TradeRules.HasSalaryMatching ? ruleset.TradeRules.SalaryMatchAllowance.SmallestUnits : null,
            ruleset.TradeRules.InjuredPlayerEligibility.ToString(),
            ruleset.TradeRules.SecondApronBlocksSalaryIncrease,
            ruleset.NegotiationRules.MaximumContractSeasons,
            ruleset.NegotiationRules.MaximumIncumbentContractSeasons,
            ruleset.NegotiationRules.MaximumAnnualEscalationPercent,
            ruleset.NegotiationRules.MaximumAnnualDeescalationPercent,
            ToCeilingTiers(ruleset.NegotiationRules.CompensationCeiling),
            ToFloorBands(ruleset.NegotiationRules.CompensationFloor),
            ruleset.NegotiationRules.StandardOverCapAllowance?.SmallestUnits,
            ruleset.NegotiationRules.StandardOverCapAllowanceUnavailableAbove?.ToString(),
            ruleset.NegotiationRules.AllowanceMaySplitAcrossPlayers,
            ruleset.NegotiationRules.MarketResolution.ToString(),
            ruleset.NegotiationRules.OfferExpiryDays,
            ruleset.ScheduleRules.PreseasonDays == 0 ? null : ruleset.ScheduleRules.PreseasonDays,
            ruleset.ScheduleRules.RegularSeasonDays,
            ruleset.ScheduleRules.OffseasonDays == 0 ? null : ruleset.ScheduleRules.OffseasonDays,
            ruleset.ScheduleRules.GamesVersusDivisionOpponent,
            ruleset.ScheduleRules.GamesVersusConferenceOpponent,
            ruleset.ScheduleRules.GamesVersusOtherConferenceOpponent,
            ruleset.StandingsRules.HasTieBreaks
                ? ruleset.StandingsRules.TieBreaks.Steps.Select(step => step.ToString()).ToList()
                : null,
            ruleset.HasPostseason ? ruleset.PostseasonRules.PostseasonDays : null,
            ruleset.HasPostseason ? ruleset.PostseasonRules.QualifyingTeamsPerConference : null,
            ruleset.HasPostseason ? ruleset.PostseasonRules.SeriesLengths.ToList() : null,
            ruleset.HasPostseason ? ruleset.PostseasonRules.HomeCourtSequence.ToString() : null,
            ruleset.HasPostseason ? ruleset.PostseasonRules.PlayoffEligibilityCutoffDay : null,
            ruleset.NegotiationRules.InSeasonSigningWindowOpensDay,
            ruleset.NegotiationRules.InSeasonSigningWindowClosesDay,
            ruleset.NegotiationRules.ShortTermContractDays);

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
        //
        // The immediately previous version is the case worth explaining rather than just rejecting.
        // Each bump so far has been additive-by-absence — version 4 made the cap system and the
        // draft optional, version 5 added the negotiation section — so a valid file from the version
        // before this one is a valid file for this one with the number changed. There is no
        // migration to run. What the gate buys is the other direction: an older reader handed a
        // newer file would read absent fields as zeroes, or ignore rules it cannot enforce, and run
        // a rulebook the file never stated.
        if (envelope.SchemaVersion != LeagueRuleset.CurrentSchemaVersion)
        {
            var upgradeHint = envelope.SchemaVersion == LeagueRuleset.CurrentSchemaVersion - 1
                ? $" Version {LeagueRuleset.CurrentSchemaVersion} is the same format with an additional optional section, so a valid version {envelope.SchemaVersion} file only needs its schemaVersion changed to {LeagueRuleset.CurrentSchemaVersion}."
                : string.Empty;

            return DomainOperationResult<LeagueRuleset>.Failure(
                new DomainError(
                    UnsupportedSchemaVersionCode,
                    $"League ruleset schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {LeagueRuleset.CurrentSchemaVersion}.{upgradeHint}"));
        }

        try
        {
            var capThresholdsResult = CapThresholds.Create(
                ToMoney(envelope.PayrollFloor),
                ToMoney(envelope.SoftCap),
                ToMoney(envelope.LuxuryTax),
                ToMoney(envelope.FirstApron),
                ToMoney(envelope.SecondApron),
                ToMoney(envelope.HardCap));

            if (capThresholdsResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(capThresholdsResult.Errors.ToArray());
            }

            var eligibilityResult = TradeRules.ParseEligibility(envelope.InjuredPlayerTradeEligibility ?? string.Empty);
            if (eligibilityResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(eligibilityResult.Errors.ToArray());
            }

            var tradeRulesResult = TradeRules.Create(
                envelope.SalaryMatchPercent,
                ToMoney(envelope.SalaryMatchAllowance),
                eligibilityResult.Value,
                envelope.SecondApronBlocksSalaryIncrease);

            if (tradeRulesResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(tradeRulesResult.Errors.ToArray());
            }

            var draftRulesResult = DraftRules.Create(
                envelope.DraftRoundCount ?? 0,
                envelope.DraftLotteryEnabled,
                envelope.TradableFutureDraftHorizon ?? 0,
                envelope.RetainedRoundNumber ?? 0,
                envelope.RetainedRoundInterval ?? 0);

            if (draftRulesResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(draftRulesResult.Errors.ToArray());
            }

            var negotiationRulesResult = BuildNegotiationRules(envelope, capThresholdsResult.Value);
            if (negotiationRulesResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(negotiationRulesResult.Errors.ToArray());
            }

            var scheduleRulesResult = ScheduleRules.Create(
                envelope.PreseasonDays ?? 0,
                envelope.RegularSeasonDays ?? ScheduleRules.Minimal.RegularSeasonDays,
                envelope.OffseasonDays ?? 0,
                envelope.GamesVersusDivisionOpponent,
                envelope.GamesVersusConferenceOpponent,
                envelope.GamesVersusOtherConferenceOpponent);

            if (scheduleRulesResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(scheduleRulesResult.Errors.ToArray());
            }

            var standingsRulesResult = StandingsRules.Parse(envelope.StandingsTieBreaks);
            if (standingsRulesResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(standingsRulesResult.Errors.ToArray());
            }

            var postseasonRulesResult = BuildPostseasonRules(envelope, scheduleRulesResult.Value);
            if (postseasonRulesResult.IsFailure)
            {
                return DomainOperationResult<LeagueRuleset>.Failure(postseasonRulesResult.Errors.ToArray());
            }

            var ruleset = new LeagueRuleset(
                envelope.SchemaVersion,
                envelope.Name,
                envelope.RegularSeasonGameCount,
                new RosterSizeLimits(envelope.MinimumRosterPlayers, envelope.MaximumRosterPlayers),
                capThresholdsResult.Value,
                draftRulesResult.Value,
                tradeRulesResult.Value,
                negotiationRulesResult.Value,
                scheduleRulesResult.Value,
                standingsRulesResult.Value,
                postseasonRulesResult.Value);

            return DomainOperationResult<LeagueRuleset>.Success(ruleset);
        }
        catch (ArgumentException exception)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(new DomainError(InvalidFieldCode, exception.Message));
        }
    }

    /// <summary>
    /// Maps the negotiation section, checking it against the thresholds the same file configures.
    /// The section is optional in full: a file that leaves out every field loads as an open market
    /// rather than as a league where nobody may sign.
    /// </summary>
    private static DomainOperationResult<NegotiationRules> BuildNegotiationRules(
        LeagueRulesetEnvelope envelope,
        CapThresholds capThresholds)
    {
        var ceilingResult = CompensationCeilingScale.Create(
            envelope.CompensationCeilingTiers?.Select(tier => new ScaleBand(tier.MinimumSeasonsOfService, tier.PercentOfSoftCap)));

        if (ceilingResult.IsFailure)
        {
            return DomainOperationResult<NegotiationRules>.Failure(ceilingResult.Errors.ToArray());
        }

        var floorResult = CompensationFloorScale.Create(
            envelope.CompensationFloorScale?.Select(band => new ScaleBand(band.MinimumSeasonsOfService, band.Amount)));

        if (floorResult.IsFailure)
        {
            return DomainOperationResult<NegotiationRules>.Failure(floorResult.Errors.ToArray());
        }

        var resolutionResult = NegotiationRules.ParseMarketResolution(envelope.MarketResolution);
        if (resolutionResult.IsFailure)
        {
            return DomainOperationResult<NegotiationRules>.Failure(resolutionResult.Errors.ToArray());
        }

        // Absence is the common case and means the allowance is never withdrawn, so it is settled
        // here rather than inside the parser: a result type that carries a null success is a result
        // type that throws on the field a league most often leaves out.
        CapThresholdKind? allowanceLimit = null;
        if (!string.IsNullOrWhiteSpace(envelope.StandardOverCapAllowanceUnavailableAbove))
        {
            var allowanceLimitResult = NegotiationRules.ParseAllowanceLimit(envelope.StandardOverCapAllowanceUnavailableAbove);
            if (allowanceLimitResult.IsFailure)
            {
                return DomainOperationResult<NegotiationRules>.Failure(allowanceLimitResult.Errors.ToArray());
            }

            allowanceLimit = allowanceLimitResult.Value;
        }

        return NegotiationRules.Create(
            capThresholds,
            envelope.MaximumContractSeasons,
            envelope.MaximumIncumbentContractSeasons,
            envelope.MaximumAnnualEscalationPercent,
            envelope.MaximumAnnualDeescalationPercent,
            ceilingResult.Value,
            floorResult.Value,
            ToMoney(envelope.StandardOverCapAllowance),
            allowanceLimit,
            envelope.AllowanceMaySplitAcrossPlayers,
            resolutionResult.Value,
            envelope.OfferExpiryDays,
            envelope.InSeasonSigningWindowOpensDay,
            envelope.InSeasonSigningWindowClosesDay,
            envelope.ShortTermContractDays);
    }

    /// <summary>
    /// Maps the postseason section, which is absent in full for a league that holds no postseason.
    /// A file that names one part of it and not the rest has described a format nobody can play, so
    /// the missing pieces are reported by <see cref="PostseasonRules.Create"/> rather than filled in
    /// with a default this build invented.
    /// </summary>
    private static DomainOperationResult<PostseasonRules> BuildPostseasonRules(
        LeagueRulesetEnvelope envelope,
        ScheduleRules scheduleRules)
    {
        var statesNothing =
            envelope.PostseasonDays is null &&
            envelope.PostseasonQualifyingTeamsPerConference is null &&
            envelope.PostseasonSeriesLengths is null &&
            envelope.PostseasonHomeCourtSequence is null &&
            envelope.PlayoffEligibilityCutoffDay is null;

        if (statesNothing)
        {
            return DomainOperationResult<PostseasonRules>.Success(PostseasonRules.None);
        }

        return PostseasonRules.Create(
            envelope.PostseasonDays ?? 0,
            envelope.PostseasonQualifyingTeamsPerConference ?? 0,
            envelope.PostseasonSeriesLengths ?? [],
            envelope.PostseasonHomeCourtSequence ?? string.Empty,
            envelope.PlayoffEligibilityCutoffDay,
            scheduleRules.PreseasonDays + scheduleRules.RegularSeasonDays);
    }

    private static IReadOnlyList<CompensationCeilingTierEnvelope>? ToCeilingTiers(CompensationCeilingScale ceiling) =>
        ceiling.IsConfigured
            ? ceiling.Scale.Bands.Select(band => new CompensationCeilingTierEnvelope(band.MinimumKey, band.Value)).ToList()
            : null;

    private static IReadOnlyList<CompensationFloorBandEnvelope>? ToFloorBands(CompensationFloorScale floor) =>
        floor.IsConfigured
            ? floor.Scale.Bands.Select(band => new CompensationFloorBandEnvelope(band.MinimumKey, band.Value)).ToList()
            : null;

    /// <summary>Absent stays absent. A missing amount is a rule the league does not have, not an amount of zero.</summary>
    private static Money? ToMoney(long? smallestUnits) => smallestUnits is null ? null : new Money(smallestUnits.Value);
}
