using BallGM.Application.Leagues;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Rules.Configuration;

namespace BallGM.Infrastructure.Rulesets;

/// <summary>
/// The two-way mapping between the Application layer's <see cref="LeagueConfiguration"/> — the
/// ruleset shape a project that does not reference <c>BallGM.Rules</c> can carry — and the Rules
/// layer's own <see cref="LeagueRuleset"/>, where the values are actually validated.
/// <para>
/// Extracted rather than left inline once a second caller needed it: <c>RulesSeasonEngine</c> maps
/// <see cref="LeagueConfiguration"/> onto <see cref="LeagueRuleset"/> to drive the calendar and the
/// match engine, <c>FixtureLeagueDataSource</c> maps the other way to build the configuration a
/// freshly loaded ruleset file produces, and <c>BallGM.Infrastructure.Saves.SaveGameSerializer</c>
/// needs both directions to embed the ruleset actually in effect at save time and read it back.
/// </para>
/// </summary>
internal static class LeagueConfigurationMapper
{
    private const string InvalidScheduleRulesCode = "ruleset.invalid_schedule_rules";
    private const string InvalidStandingsRulesCode = "ruleset.invalid_standings_rules";
    private const string InvalidPostseasonRulesCode = "ruleset.invalid_postseason_rules";
    private const string InvalidNegotiationRulesCode = "ruleset.invalid_negotiation_rules";

    /// <summary>
    /// Rebuilds the rules-layer ruleset from the Application-facing configuration. The configuration
    /// came from a file a modder can edit (or, once loaded, a save), so an incoherent set of rules
    /// fails explainably here rather than throwing out of a command.
    /// <para>
    /// <paramref name="leagueIsFlat"/> travels in separately because whether a postseason plays a
    /// final between two conference winners depends on the league's alignment, not on anything the
    /// ruleset itself states — the same file loaded by a flat league and by a two-conference one has
    /// a different round count.
    /// </para>
    /// </summary>
    public static DomainOperationResult<LeagueRuleset> ToRuleset(this LeagueConfiguration configuration, bool leagueIsFlat)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var schedule = configuration.ResolvedSchedule;

        var thresholdsResult = CapThresholds.Create(
            configuration.PayrollFloor,
            configuration.SoftCap,
            configuration.LuxuryTax,
            configuration.FirstApron,
            configuration.SecondApron,
            configuration.HardCap);

        if (thresholdsResult.IsFailure)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(thresholdsResult.Errors.ToArray());
        }

        var scheduleRulesResult = ScheduleRules.Create(
            schedule.PreseasonDays,
            schedule.RegularSeasonDays,
            schedule.OffseasonDays,
            schedule.GamesVersusDivisionOpponent,
            schedule.GamesVersusConferenceOpponent,
            schedule.GamesVersusOtherConferenceOpponent);

        if (scheduleRulesResult.IsFailure)
        {
            return Relabel<LeagueRuleset>(scheduleRulesResult.Errors, InvalidScheduleRulesCode);
        }

        var standingsRulesResult = StandingsRules.Create(configuration.ResolvedTieBreaks.Steps);
        if (standingsRulesResult.IsFailure)
        {
            return Relabel<LeagueRuleset>(standingsRulesResult.Errors, InvalidStandingsRulesCode);
        }

        var postseasonRules = PostseasonRules.None;
        if (configuration.Postseason is { } postseason)
        {
            var postseasonResult = PostseasonRules.Create(
                postseason.PostseasonDays,
                postseason.QualifyingTeamsPerConference,
                postseason.SeriesLengths,
                postseason.HomeCourtSequence,
                postseason.PlayoffEligibilityCutoffDay,
                schedule.PreseasonDays + schedule.RegularSeasonDays,
                includesFinal: !leagueIsFlat);

            if (postseasonResult.IsFailure)
            {
                return Relabel<LeagueRuleset>(postseasonResult.Errors, InvalidPostseasonRulesCode);
            }

            postseasonRules = postseasonResult.Value;
        }

        var negotiation = configuration.Negotiation;

        var ceilingResult = CompensationCeilingScale.Create(negotiation.CompensationCeilingTiers.Bands);
        if (ceilingResult.IsFailure)
        {
            return Relabel<LeagueRuleset>(ceilingResult.Errors, InvalidNegotiationRulesCode);
        }

        var floorResult = CompensationFloorScale.Create(negotiation.CompensationFloorScale.Bands);
        if (floorResult.IsFailure)
        {
            return Relabel<LeagueRuleset>(floorResult.Errors, InvalidNegotiationRulesCode);
        }

        var negotiationRulesResult = NegotiationRules.Create(
            thresholdsResult.Value,
            negotiation.MaximumContractSeasons,
            negotiation.MaximumIncumbentContractSeasons,
            negotiation.MaximumAnnualEscalationPercent,
            negotiation.MaximumAnnualDeescalationPercent,
            ceilingResult.Value,
            floorResult.Value,
            negotiation.StandardOverCapAllowance,
            negotiation.StandardOverCapAllowanceUnavailableAbove,
            negotiation.AllowanceMaySplitAcrossPlayers,
            negotiation.MarketResolution,
            negotiation.OfferExpiryDays,
            negotiation.InSeasonSigningWindowOpensDay,
            negotiation.InSeasonSigningWindowClosesDay,
            negotiation.ShortTermContractDays);

        if (negotiationRulesResult.IsFailure)
        {
            return Relabel<LeagueRuleset>(negotiationRulesResult.Errors, InvalidNegotiationRulesCode);
        }

        var draftRulesResult = DraftRules.Create(
            configuration.DraftRoundCount,
            configuration.DraftLotteryEnabled,
            configuration.TradableFutureDraftHorizon,
            configuration.RetainedRoundNumber,
            configuration.RetainedRoundInterval);

        if (draftRulesResult.IsFailure)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(draftRulesResult.Errors.ToArray());
        }

        var tradeRulesResult = TradeRules.Create(
            configuration.SalaryMatchPercent,
            configuration.SalaryMatchAllowance,
            configuration.InjuredPlayerTradeEligibility,
            configuration.SecondApronBlocksSalaryIncrease);

        if (tradeRulesResult.IsFailure)
        {
            return DomainOperationResult<LeagueRuleset>.Failure(tradeRulesResult.Errors.ToArray());
        }

        return DomainOperationResult<LeagueRuleset>.Success(new LeagueRuleset(
            LeagueRuleset.CurrentSchemaVersion,
            configuration.RulesetName,
            configuration.RegularSeasonGameCount,
            configuration.RosterLimits,
            thresholdsResult.Value,
            draftRulesResult.Value,
            tradeRulesResult.Value,
            negotiationRulesResult.Value,
            scheduleRulesResult.Value,
            standingsRulesResult.Value,
            postseasonRules));
    }

    /// <summary>
    /// The Application-facing shape of an already-validated ruleset. Pure and total: every
    /// <see cref="LeagueRuleset"/> that exists is one <see cref="ToRuleset"/> could have produced, so
    /// nothing here can fail.
    /// </summary>
    public static LeagueConfiguration ToConfiguration(this LeagueRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        return new LeagueConfiguration(
            ruleset.Name,
            ruleset.RegularSeasonGameCount,
            ruleset.RosterLimits,
            ruleset.CapThresholds.PayrollFloor,
            ruleset.CapThresholds.SoftCap,
            ruleset.CapThresholds.LuxuryTax,
            ruleset.CapThresholds.FirstApron,
            ruleset.CapThresholds.SecondApron,
            ruleset.CapThresholds.HardCap,
            ruleset.DraftRules.RoundCount,
            ruleset.DraftRules.LotteryEnabled,
            ruleset.DraftRules.TradableFutureDraftHorizon,
            ruleset.DraftRules.RetainedRoundNumber,
            ruleset.DraftRules.RetainedRoundInterval,
            ruleset.TradeRules.SalaryMatchPercent,
            ruleset.TradeRules.SalaryMatchAllowance,
            ruleset.TradeRules.InjuredPlayerEligibility,
            ruleset.TradeRules.SecondApronBlocksSalaryIncrease,
            new NegotiationConfiguration(
                ruleset.NegotiationRules.MaximumContractSeasons,
                ruleset.NegotiationRules.MaximumIncumbentContractSeasons,
                ruleset.NegotiationRules.MaximumAnnualEscalationPercent,
                ruleset.NegotiationRules.MaximumAnnualDeescalationPercent,
                ruleset.NegotiationRules.CompensationCeiling.Scale,
                ruleset.NegotiationRules.CompensationFloor.Scale,
                ruleset.NegotiationRules.StandardOverCapAllowance,
                ruleset.NegotiationRules.StandardOverCapAllowanceUnavailableAbove,
                ruleset.NegotiationRules.AllowanceMaySplitAcrossPlayers,
                ruleset.NegotiationRules.MarketResolution,
                ruleset.NegotiationRules.OfferExpiryDays,
                ruleset.NegotiationRules.InSeasonSigningWindowOpensDay,
                ruleset.NegotiationRules.InSeasonSigningWindowClosesDay,
                ruleset.NegotiationRules.ShortTermContractDays),
            new SeasonScheduleConfiguration(
                ruleset.ScheduleRules.PreseasonDays,
                ruleset.ScheduleRules.RegularSeasonDays,
                ruleset.ScheduleRules.OffseasonDays,
                ruleset.ScheduleRules.GamesVersusDivisionOpponent,
                ruleset.ScheduleRules.GamesVersusConferenceOpponent,
                ruleset.ScheduleRules.GamesVersusOtherConferenceOpponent),
            ruleset.StandingsRules.TieBreaks,
            ruleset.HasPostseason
                ? new PostseasonConfiguration(
                    ruleset.PostseasonRules.PostseasonDays,
                    ruleset.PostseasonRules.QualifyingTeamsPerConference,
                    ruleset.PostseasonRules.SeriesLengths,
                    ruleset.PostseasonRules.HomeCourtSequence.ToString(),
                    ruleset.PostseasonRules.PlayoffEligibilityCutoffDay)
                : null);
    }

    private static DomainOperationResult<T> Relabel<T>(IReadOnlyList<DomainError> errors, string code) =>
        DomainOperationResult<T>.Failure(errors.Select(error => new DomainError(code, error.Message)).ToArray());
}
