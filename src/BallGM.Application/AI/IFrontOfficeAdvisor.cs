using BallGM.Application.Leagues;
using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.AI;

/// <summary>
/// The port an Application query reaches the AI front-office models through — the same arrangement
/// as <see cref="Trades.ITradeEngine"/>, <see cref="Negotiations.ISigningEngine"/> and
/// <see cref="Negotiations.IFreeAgencyMarket"/>, because Application still does not reference Rules.
/// <para>
/// Every method here is read-only, the "Assess" half of every other port's own split, and there is no
/// "Execute" half at all: nothing behind this port ever proposes a trade, submits an offer, or drafts
/// a prospect for real. That is deliberate — this is the diagnostics seam
/// <c>docs/architecture.md</c>'s four AI sections named as arriving "with whichever slice first calls
/// this from outside Rules"; whether and how an AI turn executes anything unattended is a separate,
/// later decision this slice does not make.
/// </para>
/// </summary>
public interface IFrontOfficeAdvisor
{
    /// <param name="standing">
    /// This team's row in the current table, or <c>null</c> before any games are played or with no
    /// season under way — the caller resolves this from whatever season state it holds, because the
    /// port itself knows nothing about a season being in progress, the same separation
    /// <see cref="Negotiations.ISigningEngine"/> keeps for its own <c>SeasonDay?</c> parameter.
    /// </param>
    DomainOperationResult<FrontOfficeAssessment> AssessFrontOffice(
        TeamId teamId,
        LeagueSnapshot snapshot,
        StandingsRow? standing);

    DomainOperationResult<IReadOnlyList<TradeTargetCandidate>> FindTradeTargets(
        TeamId teamId,
        LeagueSnapshot snapshot);

    /// <param name="freeAgents">
    /// The pool to offer against — the caller's own notion of who is unsigned, so this port carries no
    /// second opinion about who counts as a free agent.
    /// </param>
    DomainOperationResult<IReadOnlyList<FreeAgentTargetCandidate>> FindFreeAgentTargets(
        TeamId teamId,
        IReadOnlyCollection<Player> freeAgents,
        LeagueSnapshot snapshot);

    /// <summary>
    /// What this team would take with a selection, read against a freshly generated preview class —
    /// never persisted, and never the class an actual future draft will use, because nothing in this
    /// codebase keeps a draft class alive outside the one pass <c>DraftDay</c> runs and consumes it
    /// in. <paramref name="previewSeed"/> makes the preview reproducible call to call; it is not, and
    /// must not be read as, a forecast of who this team will actually be able to draft.
    /// </summary>
    /// <returns>
    /// <c>null</c> where this league holds no draft, generates no classes of its own, or the preview
    /// class came up empty — never a failure for any of the three, because each is a real league
    /// shape rather than a misconfiguration.
    /// </returns>
    DomainOperationResult<DraftPreview?> PreviewDraftRecommendation(
        TeamId teamId,
        LeagueSnapshot snapshot,
        int previewSeed);
}

/// <summary>
/// A draft recommendation alongside the prospect it names. <see cref="DraftPickRecommendation"/> only
/// carries a <see cref="ProspectId"/> — correct for the real draft, where the class survives long
/// enough for a caller to look one up — but a preview class is generated and discarded inside the one
/// call that produced it, so the recommendation would otherwise name a prospect nobody outside this
/// call could ever resolve. Carrying the <see cref="Prospect"/> itself is the fix.
/// </summary>
public sealed record DraftPreview(DraftPickRecommendation Recommendation, Prospect Prospect);

/// <summary>
/// The foundation reading for one team: its classified competitive direction and its positional
/// needs. Bundled into one call because both come from
/// <c>BallGM.Rules.AI.OrganisationalDirectionClassifier</c>/<c>BallGM.Rules.AI.RosterNeedsCalculator</c>
/// at once and nothing yet needs them apart — the same "small container beside the interface" shape
/// <see cref="Trades.TradeResult"/> and <see cref="Negotiations.CompensationLimits"/> already use.
/// </summary>
public sealed record FrontOfficeAssessment(
    OrganisationalDirectionAssessment Direction,
    RosterNeedsAssessment Needs);
