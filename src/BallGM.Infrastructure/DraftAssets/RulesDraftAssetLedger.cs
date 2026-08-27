using BallGM.Application.DraftAssets;
using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Rules.Configuration;
using BallGM.Rules.DraftAssets;

namespace BallGM.Infrastructure.DraftAssets;

/// <summary>
/// Adapts the Rules draft-asset services onto the Application port, mirroring
/// <c>BallGM.Infrastructure.Cap.RulesCapLedger</c>: Application does not reference Rules, so this is
/// where the loaded <see cref="LeagueConfiguration"/> is turned back into the
/// <see cref="DraftRules"/> the rules layer works in.
/// </summary>
public sealed class RulesDraftAssetLedger : IDraftAssetLedger
{
    private const string InvalidDraftRulesCode = "ruleset.invalid_draft_rules";

    private readonly DraftAssetLedger _board = new();
    private readonly PickOwnershipRules _ownershipRules = new();

    public DomainOperationResult<DraftAssetBoard> BuildBoard(
        DraftAssetBook book,
        IReadOnlyList<FranchiseDraftIdentity> franchises,
        Season firstDraftSeason,
        LeagueConfiguration configuration)
    {
        var draftRulesResult = ToDraftRules(configuration);
        return draftRulesResult.IsFailure
            ? DomainOperationResult<DraftAssetBoard>.Failure(draftRulesResult.Errors.ToArray())
            : _board.BuildBoard(book, franchises, firstDraftSeason, draftRulesResult.Value);
    }

    public DomainOperationResult ValidateTransfer(
        DraftAssetBook book,
        DraftPickId pickId,
        FranchiseId fromFranchiseId,
        FranchiseId toFranchiseId,
        Season currentSeason,
        LeagueConfiguration configuration)
    {
        var draftRulesResult = ToDraftRules(configuration);
        return draftRulesResult.IsFailure
            ? DomainOperationResult.Failure(draftRulesResult.Errors.ToArray())
            : _ownershipRules.ValidateTransfer(book, pickId, fromFranchiseId, toFranchiseId, currentSeason, draftRulesResult.Value);
    }

    /// <summary>
    /// The configuration came from a file a modder can edit, so an incoherent set of draft rules is
    /// untrusted input: it fails explainably here rather than throwing out of a query.
    /// </summary>
    private static DomainOperationResult<DraftRules> ToDraftRules(LeagueConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var result = DraftRules.Create(
            configuration.DraftRoundCount,
            configuration.DraftLotteryEnabled,
            configuration.TradableFutureDraftHorizon,
            configuration.RetainedRoundNumber,
            configuration.RetainedRoundInterval);

        return result.IsFailure
            ? DomainOperationResult<DraftRules>.Failure(result.Errors
                .Select(error => new DomainError(InvalidDraftRulesCode, error.Message))
                .ToArray())
            : result;
    }
}
