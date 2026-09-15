using BallGM.Application.AI;
using BallGM.Application.Cap;
using BallGM.Application.Leagues;
using BallGM.Domain.AI;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Rulesets;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;
using BallGM.Rules.Negotiations;
using BallGM.Rules.Trades;

namespace BallGM.Infrastructure.AI;

/// <summary>
/// Adapts the four Milestone 9 AI models (<c>BallGM.Rules.AI</c>) onto the Application port, mapping
/// the loaded <see cref="LeagueConfiguration"/> back into the rules types — the same trust boundary
/// <c>RulesTradeEngine</c>, <c>RulesSigningEngine</c> and <c>RulesFreeAgencyMarket</c> already occupy.
/// <para>
/// Uses <see cref="LeagueConfigurationMapper.ToRuleset"/> rather than rebuilding each rule type by
/// hand the way those three adapters do — that manual approach predates the mapper
/// <c>RulesSeasonEngine</c> already uses for <c>ConcludeSeason</c>/<c>RunDraft</c>, and every rule
/// type all four AI models need (development, negotiation, trade, draft-class, scouting) comes back
/// in the one call.
/// </para>
/// </summary>
public sealed class RulesFrontOfficeAdvisor(ICapLedger capLedger) : IFrontOfficeAdvisor
{
    private const string UnknownTeamCode = "ai_advisor.unknown_team";
    private const string NoDepthChartCode = "ai_advisor.no_depth_chart";

    /// <summary>
    /// The random seed a free-agent asking price is read with. Deterministic and unused for anything
    /// beyond satisfying <c>MarketContext</c>'s shape — the same reasoning and the same fixed seed
    /// <c>BallGM.Infrastructure.Negotiations.RulesFreeAgencyMarket.AskingPrice</c> already uses for its
    /// own read-only price lookup: pricing here draws no cards of its own.
    /// </summary>
    private const int AskingPriceSeed = 0;

    public DomainOperationResult<FrontOfficeAssessment> AssessFrontOffice(
        TeamId teamId,
        LeagueSnapshot snapshot,
        StandingsRow? standing)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<FrontOfficeAssessment>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;

        var team = snapshot.Teams.FirstOrDefault(candidate => candidate.Id == teamId);
        if (team is null)
        {
            return DomainOperationResult<FrontOfficeAssessment>.Failure(new DomainError(
                UnknownTeamCode, $"Team '{teamId.Value}' is not a team in this league."));
        }

        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var roster = team.PlayerIds
            .Select(playerId => playersById.GetValueOrDefault(playerId))
            .Where(player => player is not null)
            .Select(player => player!)
            .ToList();

        var direction = OrganisationalDirectionClassifier.Classify(
            teamId, snapshot.CurrentSeason, roster, standing, ruleset.DevelopmentRules);

        var chart = DepthChartSupport.BuildChart(team, playersById, ruleset.RosterLimits);
        if (chart is null)
        {
            return DomainOperationResult<FrontOfficeAssessment>.Failure(new DomainError(
                NoDepthChartCode, $"Team '{teamId.Value}' has no depth chart that could be built for it."));
        }

        // Unlike the trade- and free-agent-targeting models, which only read needs to filter
        // positions and never surface the payroll-floor note, this is the foundation slice's own
        // reading — the one meant to be shown — so it earns the real cap sheet rather than
        // DepthChartSupport's neutral stand-in.
        var charges = CapChargeProjection.ForTeamSeason(snapshot.Contracts, teamId, snapshot.CurrentSeason);
        var capSheetResult = capLedger.Evaluate(
            teamId, snapshot.CurrentSeason, charges, team.RosterCount, snapshot.Configuration);
        if (capSheetResult.IsFailure)
        {
            return DomainOperationResult<FrontOfficeAssessment>.Failure(capSheetResult.Errors.ToArray());
        }

        var needs = RosterNeedsCalculator.Assess(
            teamId, chart, playersById, ruleset.RosterLimits, capSheetResult.Value);

        return DomainOperationResult<FrontOfficeAssessment>.Success(new FrontOfficeAssessment(direction, needs));
    }

    public DomainOperationResult<IReadOnlyList<TradeTargetCandidate>> FindTradeTargets(
        TeamId teamId,
        LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<TradeTargetCandidate>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var context = new TradeContext(
            snapshot.CurrentSeason,
            snapshot.Teams,
            snapshot.Players,
            snapshot.Contracts,
            snapshot.DraftAssets,
            snapshot.Ledger,
            ruleset.RosterLimits,
            ruleset.CapThresholds,
            ruleset.TradeRules,
            ruleset.DraftRules);

        var candidates = TradeTargetingModel.FindCandidates(teamId, context, ruleset.DevelopmentRules, ruleset.NegotiationRules);
        return DomainOperationResult<IReadOnlyList<TradeTargetCandidate>>.Success(candidates);
    }

    public DomainOperationResult<IReadOnlyList<FreeAgentTargetCandidate>> FindFreeAgentTargets(
        TeamId teamId,
        IReadOnlyCollection<Player> freeAgents,
        LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(freeAgents);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (freeAgents.Count == 0)
        {
            return DomainOperationResult<IReadOnlyList<FreeAgentTargetCandidate>>.Success([]);
        }

        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<FreeAgentTargetCandidate>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;

        // MarketContext.Player is mandatory but FreeAgentTargetingModel overrides it per candidate —
        // see its own class remarks — so any free agent in the pool is a fine placeholder here.
        var context = new MarketContext(
            snapshot.CurrentSeason,
            SeasonDay.Opening,
            freeAgents.First(),
            snapshot.Teams,
            snapshot.Players,
            snapshot.Contracts,
            snapshot.Ledger,
            ruleset.RosterLimits,
            ruleset.CapThresholds,
            ruleset.NegotiationRules,
            new SeededRandomSource(AskingPriceSeed),
            ruleset.PostseasonRules);

        var candidates = FreeAgentTargetingModel.FindCandidates(teamId, freeAgents, context);
        return DomainOperationResult<IReadOnlyList<FreeAgentTargetCandidate>>.Success(candidates);
    }

    public DomainOperationResult<DraftPreview?> PreviewDraftRecommendation(
        TeamId teamId,
        LeagueSnapshot snapshot,
        int previewSeed)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<DraftPreview?>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        if (!ruleset.DraftRules.HasDraft || !ruleset.DraftClassRules.IsConfigured)
        {
            return DomainOperationResult<DraftPreview?>.Success(null);
        }

        var team = snapshot.Teams.FirstOrDefault(candidate => candidate.Id == teamId);
        if (team is null)
        {
            return DomainOperationResult<DraftPreview?>.Failure(new DomainError(
                UnknownTeamCode, $"Team '{teamId.Value}' is not a team in this league."));
        }

        var classResult = ProspectGenerator.Generate(
            new DraftClassId(SortableId.NewId()),
            snapshot.CurrentSeason,
            ruleset.DraftClassRules,
            new SeededRandomSource(previewSeed));
        if (classResult.IsFailure)
        {
            return DomainOperationResult<DraftPreview?>.Failure(classResult.Errors.ToArray());
        }

        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var recommendation = DraftDecisionModel.Recommend(
            teamId,
            team,
            playersById,
            classResult.Value.Prospects,
            ruleset.RosterLimits,
            ruleset.ScoutingRules,
            snapshot.CurrentSeason);

        if (recommendation is null)
        {
            return DomainOperationResult<DraftPreview?>.Success(null);
        }

        var prospect = classResult.Value.Prospects.First(candidate => candidate.Id == recommendation.ProspectId);
        return DomainOperationResult<DraftPreview?>.Success(new DraftPreview(recommendation, prospect));
    }

    private static DomainOperationResult<LeagueRuleset> BuildRuleset(LeagueSnapshot snapshot) =>
        snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
}
