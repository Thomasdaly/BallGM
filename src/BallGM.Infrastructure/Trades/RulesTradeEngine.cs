using BallGM.Application.Leagues;
using BallGM.Application.Trades;
using BallGM.Domain.Common;
using BallGM.Domain.Trades;
using BallGM.Rules.Configuration;
using BallGM.Rules.Trades;

namespace BallGM.Infrastructure.Trades;

/// <summary>
/// Adapts the Rules trade validator and executor onto the Application port, mapping the loaded
/// <see cref="LeagueConfiguration"/> back into the rules types — the same trust boundary
/// <c>RulesCapLedger</c> and <c>RulesDraftAssetLedger</c> already occupy.
/// </summary>
public sealed class RulesTradeEngine : ITradeEngine
{
    private const string InvalidTradeRulesCode = "ruleset.invalid_trade_rules";
    private const string InvalidDraftRulesCode = "ruleset.invalid_draft_rules";

    private readonly TradeValidator _validator = new();
    private readonly TradeExecutor _executor = new();

    public DomainOperationResult<TradeAssessment> Assess(TradeProposal proposal, LeagueSnapshot snapshot)
    {
        var contextResult = BuildContext(snapshot);
        return contextResult.IsFailure
            ? DomainOperationResult<TradeAssessment>.Failure(contextResult.Errors.ToArray())
            : _validator.Validate(proposal, contextResult.Value);
    }

    public DomainOperationResult<TradeResult> Execute(TradeProposal proposal, LeagueSnapshot snapshot)
    {
        var contextResult = BuildContext(snapshot);
        if (contextResult.IsFailure)
        {
            return DomainOperationResult<TradeResult>.Failure(contextResult.Errors.ToArray());
        }

        var executionResult = _executor.Execute(proposal, contextResult.Value);
        return executionResult.IsFailure
            ? DomainOperationResult<TradeResult>.Failure(executionResult.Errors.ToArray())
            : DomainOperationResult<TradeResult>.Success(new TradeResult(
                executionResult.Value.Assessment,
                executionResult.Value.LedgerEntries.Count));
    }

    /// <summary>
    /// Rebuilds the rules-layer view of a loaded league. The configuration came from a file a modder
    /// can edit, so an incoherent set of rules fails explainably here rather than throwing out of a
    /// command.
    /// </summary>
    private static DomainOperationResult<TradeContext> BuildContext(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var configuration = snapshot.Configuration;

        var thresholdsResult = CapThresholds.Create(
            configuration.PayrollFloor,
            configuration.SoftCap,
            configuration.LuxuryTax,
            configuration.FirstApron,
            configuration.SecondApron,
            configuration.HardCap);

        if (thresholdsResult.IsFailure)
        {
            return DomainOperationResult<TradeContext>.Failure(thresholdsResult.Errors.ToArray());
        }

        var tradeRulesResult = TradeRules.Create(
            configuration.SalaryMatchPercent,
            configuration.SalaryMatchAllowance,
            configuration.InjuredPlayerTradeEligibility,
            configuration.SecondApronBlocksSalaryIncrease);

        if (tradeRulesResult.IsFailure)
        {
            return DomainOperationResult<TradeContext>.Failure(tradeRulesResult.Errors
                .Select(error => new DomainError(InvalidTradeRulesCode, error.Message))
                .ToArray());
        }

        var draftRulesResult = DraftRules.Create(
            configuration.DraftRoundCount,
            configuration.DraftLotteryEnabled,
            configuration.TradableFutureDraftHorizon,
            configuration.RetainedRoundNumber,
            configuration.RetainedRoundInterval);

        if (draftRulesResult.IsFailure)
        {
            return DomainOperationResult<TradeContext>.Failure(draftRulesResult.Errors
                .Select(error => new DomainError(InvalidDraftRulesCode, error.Message))
                .ToArray());
        }

        return DomainOperationResult<TradeContext>.Success(new TradeContext(
            snapshot.CurrentSeason,
            snapshot.Teams,
            snapshot.Players,
            snapshot.Contracts,
            snapshot.DraftAssets,
            snapshot.Ledger,
            configuration.RosterLimits,
            thresholdsResult.Value,
            tradeRulesResult.Value,
            draftRulesResult.Value));
    }
}
