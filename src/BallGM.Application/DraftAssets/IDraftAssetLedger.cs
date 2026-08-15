using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Application.DraftAssets;

/// <summary>
/// The port an Application query reaches the draft-asset rules through, mirroring
/// <see cref="Cap.ICapLedger"/> exactly: the implementation lives in <c>BallGM.Rules.DraftAssets</c>
/// and is adapted in <c>BallGM.Infrastructure</c>, because Application does not reference Rules.
/// <para>
/// Configuration travels per call from the already-loaded <see cref="LeagueConfiguration"/>, so
/// there is one ruleset load path and no second copy of the draft rules to drift.
/// </para>
/// </summary>
public interface IDraftAssetLedger
{
    DomainOperationResult<DraftAssetBoard> BuildBoard(
        DraftAssetBook book,
        IReadOnlyList<FranchiseDraftIdentity> franchises,
        Season firstDraftSeason,
        LeagueConfiguration configuration);

    /// <summary>
    /// Whether one franchise may hand one pick to another. Nothing in the client calls this yet —
    /// the trade engine (Milestone 5) does, the same way it will call <see cref="Cap.ICapLedger"/>.
    /// It is on the port now so that engine has a validated surface to build against rather than a
    /// rules layer it has to reach around.
    /// </summary>
    DomainOperationResult ValidateTransfer(
        DraftAssetBook book,
        DraftPickId pickId,
        FranchiseId fromFranchiseId,
        FranchiseId toFranchiseId,
        Season currentSeason,
        LeagueConfiguration configuration);
}
