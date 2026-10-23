using BallGM.Application.Leagues;
using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
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

    /// <param name="excludedTeams">
    /// In a summer market, teams already part of a trade this summer (each makes at most one); when
    /// given, every candidate must also leave both teams better by their own direction, and the best
    /// for both comes first. Null outside a summer market.
    /// </param>
    /// <param name="excludedPlayers">In a summer market, players already moved this summer.</param>
    DomainOperationResult<IReadOnlyList<TradeTargetCandidate>> FindTradeTargets(
        TeamId teamId,
        LeagueSnapshot snapshot,
        IReadOnlySet<TeamId>? excludedTeams = null,
        IReadOnlySet<PlayerId>? excludedPlayers = null);

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

    /// <summary>
    /// Which expiring players every team keeps (<c>docs/franchise-realism.md</c> → F3), decided in the
    /// final season of their contracts, before it is concluded: each a re-signing at the player's asking
    /// price, which he accepts from his own team. Read-only; the caller signs them.
    /// </summary>
    /// <summary>
    /// Every option on next season's term, decided now by whichever side holds it (team options by
    /// the team's needs, player options by what the market would pay him). Read-only; the caller applies them.
    /// </summary>
    DomainOperationResult<IReadOnlyList<OptionPlan>> PlanOptionDecisions(LeagueSnapshot snapshot);

    /// <param name="records">Each team's record in the season ending, which decides its direction.</param>
    DomainOperationResult<IReadOnlyList<ReSigningPlan>> PlanReSignings(LeagueSnapshot snapshot, IReadOnlyDictionary<TeamId, TeamRecord> records);

    /// <summary>
    /// One round of the free-agency market: every team's best offers to <paramref name="freeAgents"/>
    /// at round <paramref name="round"/>'s prices, and which of them each player takes (at most one
    /// signing per team a round). Read-only; the caller executes the accepted offers, in order.
    /// </summary>
    /// <summary>
    /// The buyouts teams agree this summer (at most one a team, only by teams not contending, only of
    /// overpaid veterans outside the rotation): read-only; the caller executes them.
    /// </summary>
    DomainOperationResult<IReadOnlyList<BuyoutPlan>> PlanBuyouts(LeagueSnapshot snapshot);

    /// <param name="restricted">This summer's restricted free agents and the team that may match for each.</param>
    DomainOperationResult<IReadOnlyList<MarketSigningPlan>> PlanMarketRound(
        LeagueSnapshot snapshot,
        IReadOnlyCollection<Player> freeAgents,
        int round,
        IReadOnlyDictionary<PlayerId, TeamId>? restricted = null);

    /// <summary>
    /// The qualifying offers every team tenders to its expiring restricted players it did not re-sign
    /// (decided after the re-signing window, before the season is concluded). Read-only.
    /// </summary>
    DomainOperationResult<IReadOnlyList<QualifyingOfferPlan>> PlanQualifyingOffers(LeagueSnapshot snapshot);

    /// <summary>
    /// The salary dumps of a summer: teams over the tax paying rebuilding teams with room, in picks, to
    /// take overpaid contracts. Read-only; each plan is a trade the engine re-validates on execution.
    /// </summary>
    /// <param name="excludedTeams">Teams already part of a dump this summer, on either side.</param>
    DomainOperationResult<IReadOnlyList<SalaryDumpPlan>> PlanSalaryDumps(LeagueSnapshot snapshot, IReadOnlySet<TeamId>? excludedTeams = null);
}

/// <summary>A salary dump: the trade, the two teams, the player moved and why.</summary>
public sealed record SalaryDumpPlan(BallGM.Domain.Trades.TradeProposal Proposal, TeamId FromTeamId, TeamId ToTeamId, PlayerId PlayerId, int PicksAttached, RuleFinding Rationale);

/// <summary>A qualifying offer tendered: the player, his team, the one-season salary, and why.</summary>
public sealed record QualifyingOfferPlan(PlayerId PlayerId, TeamId TeamId, long Salary, RuleFinding Rationale);

/// <summary>One option decided: the contract, the season, whether it is taken up, and why.</summary>
public sealed record OptionPlan(Domain.Contracts.ContractId ContractId, Season Season, bool Exercise, RuleFinding Rationale);

/// <summary>A team's agreed buyout of one player: the contract, the share of his money kept, and why.</summary>
public sealed record BuyoutPlan(TeamId TeamId, PlayerId PlayerId, Domain.Contracts.ContractId ContractId, int RetainedPercent, RuleFinding Rationale);

/// <summary>A team's decision to re-sign one of its own: the terms, and the reason.</summary>
public sealed record ReSigningPlan(TeamId TeamId, PlayerId PlayerId, int Seasons, long FirstSeasonSalary, RuleFinding Rationale);

/// <summary>An offer a free agent accepted in a market round: the offer, why the team made it, and why he took it.</summary>
public sealed record MarketSigningPlan(Offer Offer, RuleFinding Bid, RuleFinding Choice);

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
