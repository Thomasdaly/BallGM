using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Infrastructure.Negotiations;

/// <summary>
/// Adapts the Rules signing validator and executor onto the Application port, mapping the loaded
/// <see cref="LeagueConfiguration"/> back into the rules types — the same trust boundary
/// <c>RulesTradeEngine</c> and <c>RulesCapLedger</c> already occupy.
/// </summary>
public sealed class RulesSigningEngine : ISigningEngine
{
    private const string InvalidNegotiationRulesCode = "ruleset.invalid_negotiation_rules";
    private const string UnknownTeamCode = "signing.unknown_team";
    private const string UnknownPlayerCode = "signing.unknown_player";

    private readonly SigningValidator _validator = new();
    private readonly SigningExecutor _executor = new();

    public DomainOperationResult<SigningAssessment> Assess(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null)
    {
        var contextResult = BuildContext(snapshot, teamId, playerId, day);
        return contextResult.IsFailure
            ? DomainOperationResult<SigningAssessment>.Failure(contextResult.Errors.ToArray())
            : _validator.Validate(offer, contextResult.Value);
    }

    public DomainOperationResult<SigningResult> Execute(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null)
    {
        var contextResult = BuildContext(snapshot, teamId, playerId, day);
        if (contextResult.IsFailure)
        {
            return DomainOperationResult<SigningResult>.Failure(contextResult.Errors.ToArray());
        }

        var executionResult = _executor.Execute(offer, contextResult.Value);
        return executionResult.IsFailure
            ? DomainOperationResult<SigningResult>.Failure(executionResult.Errors.ToArray())
            : DomainOperationResult<SigningResult>.Success(new SigningResult(
                executionResult.Value.Assessment,
                executionResult.Value.Contract,
                executionResult.Value.Route,
                executionResult.Value.LedgerEntryCount));
    }

    public CompensationLimits LimitsFor(LeagueSnapshot snapshot, int seasonsOfService)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var negotiation = snapshot.Configuration.Negotiation;
        var floor = CompensationFloorScale.From(negotiation.CompensationFloorScale);
        var ceiling = CompensationCeilingScale.From(negotiation.CompensationCeilingTiers);

        return new CompensationLimits(
            floor.FloorFor(seasonsOfService),
            ceiling.CeilingFor(seasonsOfService, snapshot.Configuration.SoftCap));
    }

    /// <summary>
    /// Rebuilds the rules-layer view of a loaded league for one prospective signing. The
    /// configuration came from a file a modder can edit, so an incoherent set of rules fails
    /// explainably here rather than throwing out of a command.
    /// </summary>
    private static DomainOperationResult<SigningContext> BuildContext(
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(playerId);

        var configuration = snapshot.Configuration;

        var team = snapshot.Teams.FirstOrDefault(candidate => candidate.Id == teamId);
        if (team is null)
        {
            return DomainOperationResult<SigningContext>.Failure(new DomainError(
                UnknownTeamCode,
                $"Team '{teamId.Value}' is not a team in this league."));
        }

        var player = snapshot.Players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is null)
        {
            return DomainOperationResult<SigningContext>.Failure(new DomainError(
                UnknownPlayerCode,
                $"Player '{playerId.Value}' is not in this league."));
        }

        var thresholdsResult = CapThresholds.Create(
            configuration.PayrollFloor,
            configuration.SoftCap,
            configuration.LuxuryTax,
            configuration.FirstApron,
            configuration.SecondApron,
            configuration.HardCap);

        if (thresholdsResult.IsFailure)
        {
            return DomainOperationResult<SigningContext>.Failure(thresholdsResult.Errors.ToArray());
        }

        var negotiation = configuration.Negotiation;
        var negotiationRulesResult = NegotiationRules.Create(
            thresholdsResult.Value,
            negotiation.MaximumContractSeasons,
            negotiation.MaximumIncumbentContractSeasons,
            negotiation.MaximumAnnualEscalationPercent,
            negotiation.MaximumAnnualDeescalationPercent,
            CompensationCeilingScale.From(negotiation.CompensationCeilingTiers),
            CompensationFloorScale.From(negotiation.CompensationFloorScale),
            negotiation.StandardOverCapAllowance,
            negotiation.StandardOverCapAllowanceUnavailableAbove,
            negotiation.AllowanceMaySplitAcrossPlayers,
            negotiation.MarketResolution,
            negotiation.OfferExpiryDays);

        if (negotiationRulesResult.IsFailure)
        {
            return DomainOperationResult<SigningContext>.Failure(negotiationRulesResult.Errors
                .Select(error => new DomainError(InvalidNegotiationRulesCode, error.Message))
                .ToArray());
        }

        // The postseason rules travel with the signing because the eligibility cutoff is a rule
        // about the day a signing happens, not about the money. A league whose postseason section is
        // absent hands over None, which the validator reports as "no postseason" rather than as an
        // eligible signing.
        var postseasonResult = BuildPostseasonRules(snapshot);
        if (postseasonResult.IsFailure)
        {
            return DomainOperationResult<SigningContext>.Failure(postseasonResult.Errors.ToArray());
        }

        return DomainOperationResult<SigningContext>.Success(new SigningContext(
            snapshot.CurrentSeason,
            team,
            player,
            snapshot.Contracts,
            snapshot.Ledger,
            configuration.RosterLimits,
            thresholdsResult.Value,
            negotiationRulesResult.Value,
            postseasonResult.Value,
            day));
    }

    /// <summary>
    /// This league's postseason format, or <see cref="PostseasonRules.None"/> where it holds none.
    /// Built here rather than reached for, the same way the negotiation rules above are.
    /// </summary>
    internal static DomainOperationResult<PostseasonRules> BuildPostseasonRules(LeagueSnapshot snapshot)
    {
        if (snapshot.Configuration.Postseason is not { } postseason)
        {
            return DomainOperationResult<PostseasonRules>.Success(PostseasonRules.None);
        }

        var schedule = snapshot.Configuration.ResolvedSchedule;

        return PostseasonRules.Create(
            postseason.PostseasonDays,
            postseason.QualifyingTeamsPerConference,
            postseason.SeriesLengths,
            postseason.HomeCourtSequence,
            postseason.PlayoffEligibilityCutoffDay,
            schedule.PreseasonDays + schedule.RegularSeasonDays,
            includesFinal: !snapshot.League.Alignment.IsFlat);
    }
}
