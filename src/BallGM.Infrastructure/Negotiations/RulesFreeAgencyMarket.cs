using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;

namespace BallGM.Infrastructure.Negotiations;

/// <summary>
/// Adapts the Rules free-agency resolver and executor onto the Application port, mapping the loaded
/// <see cref="LeagueConfiguration"/> back into the rules types — the same trust boundary
/// <see cref="RulesSigningEngine"/>, <c>RulesTradeEngine</c> and <c>RulesCapLedger</c> already occupy.
/// </summary>
public sealed class RulesFreeAgencyMarket : IFreeAgencyMarket
{
    private const string InvalidNegotiationRulesCode = "ruleset.invalid_negotiation_rules";
    private const string UnknownPlayerCode = "market.unknown_player";

    private readonly FreeAgencyMarketResolver _resolver = new();
    private readonly FreeAgencyMarketExecutor _executor = new();
    private readonly PreferenceModel _preferences = new();

    public DomainOperationResult<MarketAssessment> Assess(
        Negotiation negotiation,
        LeagueSnapshot snapshot,
        SeasonDay day,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(negotiation);

        var contextResult = BuildContext(snapshot, negotiation.PlayerId, day, random);
        return contextResult.IsFailure
            ? DomainOperationResult<MarketAssessment>.Failure(contextResult.Errors.ToArray())
            : _resolver.Assess(negotiation, contextResult.Value);
    }

    public DomainOperationResult<MarketExecution> Resolve(
        Negotiation negotiation,
        LeagueSnapshot snapshot,
        SeasonDay day,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(negotiation);

        var contextResult = BuildContext(snapshot, negotiation.PlayerId, day, random);
        return contextResult.IsFailure
            ? DomainOperationResult<MarketExecution>.Failure(contextResult.Errors.ToArray())
            : _executor.Resolve(negotiation, contextResult.Value);
    }

    public Money? AskingPrice(LeagueSnapshot snapshot, PlayerId playerId)
    {
        var contextResult = BuildContext(snapshot, playerId, SeasonDay.Opening, new SeededRandomSource(0));

        // An asking price is a figure on a board, not a verdict: a league whose negotiation rules do
        // not load has bigger problems than this cell, and every operation that matters reports them.
        return contextResult.IsFailure ? null : _preferences.AskingPrice(contextResult.Value);
    }

    /// <summary>
    /// Rebuilds the rules-layer view of a loaded league for one player's market. The configuration
    /// came from a file a modder can edit, so an incoherent set of rules fails explainably here
    /// rather than throwing out of a command.
    /// </summary>
    private static DomainOperationResult<MarketContext> BuildContext(
        LeagueSnapshot snapshot,
        PlayerId playerId,
        SeasonDay day,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(random);

        var configuration = snapshot.Configuration;

        var player = snapshot.Players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is null)
        {
            return DomainOperationResult<MarketContext>.Failure(new DomainError(
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
            return DomainOperationResult<MarketContext>.Failure(thresholdsResult.Errors.ToArray());
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
            return DomainOperationResult<MarketContext>.Failure(negotiationRulesResult.Errors
                .Select(error => new DomainError(InvalidNegotiationRulesCode, error.Message))
                .ToArray());
        }

        // The market judges its offers with the same validator the offer screen uses, so it has to
        // hand that validator the same rules — the playoff eligibility cutoff included, or a signing
        // made through the market would be checked against one fewer rule than a signing made by hand.
        var postseasonResult = RulesSigningEngine.BuildPostseasonRules(snapshot);
        if (postseasonResult.IsFailure)
        {
            return DomainOperationResult<MarketContext>.Failure(postseasonResult.Errors.ToArray());
        }

        return DomainOperationResult<MarketContext>.Success(new MarketContext(
            snapshot.CurrentSeason,
            day,
            player,
            snapshot.Teams,
            snapshot.Players,
            snapshot.Contracts,
            snapshot.Ledger,
            configuration.RosterLimits,
            thresholdsResult.Value,
            negotiationRulesResult.Value,
            random,
            postseasonResult.Value));
    }
}
