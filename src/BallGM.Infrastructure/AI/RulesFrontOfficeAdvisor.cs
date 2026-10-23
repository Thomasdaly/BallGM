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
using BallGM.Infrastructure.Negotiations;
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
            teamId, chart, team.RosterCount, playersById, ruleset.RosterLimits, capSheetResult.Value);

        return DomainOperationResult<FrontOfficeAssessment>.Success(new FrontOfficeAssessment(direction, needs));
    }

    public DomainOperationResult<IReadOnlyList<TradeTargetCandidate>> FindTradeTargets(
        TeamId teamId,
        LeagueSnapshot snapshot,
        IReadOnlySet<TeamId>? excludedTeams = null,
        IReadOnlySet<PlayerId>? excludedPlayers = null)
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
            ruleset.DraftRules,
            ruleset.CapMechanics);

        TradeTargetingOptions? options = null;
        if (excludedTeams is not null || excludedPlayers is not null)
        {
            var asOf = new DateOnly(snapshot.CurrentSeason.Year, 7, 1);
            var records = ThisSeasonRecords(snapshot, snapshot.CurrentSeason.Year - 1);
            var playersById = snapshot.Players.ToDictionary(player => player.Id);
            var directions = snapshot.Teams.ToDictionary(
                team => team.Id,
                team => DirectionOf(snapshot, team, playersById, snapshot.CurrentSeason.Year, asOf, ruleset));
            options = new TradeTargetingOptions(excludedTeams ?? new HashSet<TeamId>(), excludedPlayers ?? new HashSet<PlayerId>(), directions, asOf);
        }

        var candidates = TradeTargetingModel.FindCandidates(teamId, context, ruleset.DevelopmentRules, ruleset.NegotiationRules, options);
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
            ruleset.PostseasonRules,
            ruleset.CapMechanics,
            ruleset.BuyoutRules,
            ruleset.IncumbentRightsRules,
            LeagueSalaryScale.For(snapshot));

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

    public DomainOperationResult<IReadOnlyList<ReSigningPlan>> PlanReSignings(LeagueSnapshot snapshot, IReadOnlyDictionary<TeamId, TeamRecord> records)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(records);
        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<ReSigningPlan>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var asOf = new DateOnly(snapshot.CurrentSeason.Year + 1, 7, 1);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var plans = new List<ReSigningPlan>();
        foreach (var team in snapshot.Teams.OrderBy(team => team.Id.Value, StringComparer.Ordinal))
        {
            var roster = team.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
            var direction = DirectionOf(snapshot, team, playersById, snapshot.CurrentSeason.Year + 1, asOf, ruleset, records);
            plans.AddRange(OffseasonModel
                .ReSignings(team, roster, snapshot.Contracts, snapshot.CurrentSeason, ruleset.NegotiationRules, ruleset.CapThresholds, direction, asOf, LeagueSalaryScale.For(snapshot))
                .Select(decision => new ReSigningPlan(decision.TeamId, decision.PlayerId, decision.Seasons, decision.FirstSeasonSalary.SmallestUnits, decision.Rationale)));
        }

        return DomainOperationResult<IReadOnlyList<ReSigningPlan>>.Success(plans);
    }

    public DomainOperationResult<IReadOnlyList<SalaryDumpPlan>> PlanSalaryDumps(LeagueSnapshot snapshot, IReadOnlySet<TeamId>? excludedTeams = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<SalaryDumpPlan>>.Failure(rulesetResult.Errors.ToArray());
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
            ruleset.DraftRules,
            ruleset.CapMechanics);
        var asOf = new DateOnly(snapshot.CurrentSeason.Year, 7, 1);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var directions = snapshot.Teams.ToDictionary(
            team => team.Id,
            team => DirectionOf(snapshot, team, playersById, snapshot.CurrentSeason.Year, asOf, ruleset));

        return DomainOperationResult<IReadOnlyList<SalaryDumpPlan>>.Success(SalaryDumpModel
            .Find(context, directions, ruleset.NegotiationRules, LeagueSalaryScale.For(snapshot), excludedTeams)
            .Select(dump => new SalaryDumpPlan(dump.Proposal, dump.FromTeamId, dump.ToTeamId, dump.PlayerId, dump.Picks.Count, dump.Rationale))
            .ToList());
    }

    public DomainOperationResult<IReadOnlyList<QualifyingOfferPlan>> PlanQualifyingOffers(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<QualifyingOfferPlan>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var asOf = new DateOnly(snapshot.CurrentSeason.Year + 1, 7, 1);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var plans = new List<QualifyingOfferPlan>();
        foreach (var team in snapshot.Teams.OrderBy(team => team.Id.Value, StringComparer.Ordinal))
        {
            var roster = team.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
            plans.AddRange(OffseasonModel
                .QualifyingOffers(team, roster, snapshot.Contracts, snapshot.CurrentSeason, ruleset.IncumbentRightsRules, asOf)
                .Select(tender => new QualifyingOfferPlan(tender.PlayerId, team.Id, tender.Salary.SmallestUnits, tender.Rationale)));
        }

        return DomainOperationResult<IReadOnlyList<QualifyingOfferPlan>>.Success(plans);
    }

    public DomainOperationResult<IReadOnlyList<MarketSigningPlan>> PlanMarketRound(
        LeagueSnapshot snapshot,
        IReadOnlyCollection<Player> freeAgents,
        int round,
        IReadOnlyDictionary<PlayerId, TeamId>? restricted = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(freeAgents);
        if (freeAgents.Count == 0)
        {
            return DomainOperationResult<IReadOnlyList<MarketSigningPlan>>.Success([]);
        }

        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<MarketSigningPlan>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var asOf = new DateOnly(snapshot.CurrentSeason.Year, 7, 1);
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
            ruleset.PostseasonRules,
            ruleset.CapMechanics,
            ruleset.BuyoutRules,
            ruleset.IncumbentRightsRules,
            LeagueSalaryScale.For(snapshot));

        // Last season's table: the offseason follows the season it concluded.
        var records = ThisSeasonRecords(snapshot, snapshot.CurrentSeason.Year - 1);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var offers = new List<MarketOffer>();
        foreach (var team in snapshot.Teams.OrderBy(team => team.Id.Value, StringComparer.Ordinal))
        {
            var roster = context.RosterOf(team);
            var direction = DirectionOf(snapshot, team, playersById, snapshot.CurrentSeason.Year, asOf, ruleset);
            offers.AddRange(OffseasonModel.MarketOffers(team, freeAgents, context, direction, asOf, round, MarketOffersPerTeam));
        }

        var plans = new List<MarketSigningPlan>();
        foreach (var (bid, choice) in OffseasonModel.ResolveRound(offers, playersById, records, asOf))
        {
            // A restricted free agent's own team may match the offer he took elsewhere.
            var player = playersById[bid.Offer.PlayerId];
            if (restricted is not null && restricted.TryGetValue(player.Id, out var incumbentId) && incumbentId != bid.Offer.TeamId &&
                snapshot.Teams.FirstOrDefault(team => team.Id == incumbentId) is { } incumbent &&
                OffseasonModel.MatchesOfferSheet(incumbent, context.RosterOf(incumbent), player, bid.Offer, snapshot.Contracts, snapshot.CurrentSeason, ruleset.CapThresholds, asOf) &&
                Offer.Create(new OfferId($"MATCH-{round}-{incumbentId.Value}-{player.Id.Value}"), incumbentId, player.Id, bid.Offer.Terms) is { IsSuccess: true } matched)
            {
                plans.Add(new MarketSigningPlan(matched.Value, bid.Rationale, new RuleFinding("ai_offseason.offer_sheet_matched",
                    $"{incumbent.Name} matches the offer {player.FullName} accepted from another team and keeps its restricted free agent.", incumbentId)));
                continue;
            }

            plans.Add(new MarketSigningPlan(bid.Offer, bid.Rationale, choice));
        }

        return DomainOperationResult<IReadOnlyList<MarketSigningPlan>>.Success(plans);
    }

    public DomainOperationResult<IReadOnlyList<OptionPlan>> PlanOptionDecisions(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<OptionPlan>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var asOf = new DateOnly(snapshot.CurrentSeason.Year + 1, 7, 1);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var plans = new List<OptionPlan>();
        foreach (var team in snapshot.Teams.OrderBy(team => team.Id.Value, StringComparer.Ordinal))
        {
            var roster = team.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
            plans.AddRange(OffseasonModel
                .OptionDecisions(team, roster, snapshot.Contracts, snapshot.CurrentSeason, ruleset.NegotiationRules, ruleset.CapThresholds, asOf, LeagueSalaryScale.For(snapshot))
                .Select(decision => new OptionPlan(decision.ContractId, decision.Season, decision.Exercise, decision.Rationale)));
        }

        return DomainOperationResult<IReadOnlyList<OptionPlan>>.Success(plans);
    }

    public DomainOperationResult<IReadOnlyList<BuyoutPlan>> PlanBuyouts(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rulesetResult = BuildRuleset(snapshot);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<IReadOnlyList<BuyoutPlan>>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var asOf = new DateOnly(snapshot.CurrentSeason.Year, 7, 1);
        var records = ThisSeasonRecords(snapshot, snapshot.CurrentSeason.Year - 1);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);
        var plans = new List<BuyoutPlan>();
        foreach (var team in snapshot.Teams.OrderBy(team => team.Id.Value, StringComparer.Ordinal))
        {
            var roster = team.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
            var direction = DirectionOf(snapshot, team, playersById, snapshot.CurrentSeason.Year, asOf, ruleset);
            var decision = BuyoutModel.Decide(team, roster, snapshot.Contracts, snapshot.CurrentSeason, ruleset.NegotiationRules, ruleset.CapThresholds, ruleset.BuyoutRules, direction, asOf, LeagueSalaryScale.For(snapshot));
            if (decision is not null)
            {
                plans.Add(new BuyoutPlan(decision.TeamId, decision.PlayerId, decision.ContractId, decision.RetainedPercent, decision.Rationale));
            }
        }

        return DomainOperationResult<IReadOnlyList<BuyoutPlan>>.Success(plans);
    }

    /// <summary>How many offers one team makes in one market round, best first.</summary>
    private const int MarketOffersPerTeam = 3;

    private static int PeakAgeStart(LeagueRuleset ruleset) => ruleset.DevelopmentRules.IsConfigured ? ruleset.DevelopmentRules.PeakAgeStart : 26;

    /// <summary>A team's direction from the two seasons before <paramref name="nextSeasonYear"/>.</summary>
    private static CompetitiveDirection DirectionOf(LeagueSnapshot snapshot, Team team, IReadOnlyDictionary<PlayerId, Player> playersById, int nextSeasonYear, DateOnly asOf, LeagueRuleset ruleset, IReadOnlyDictionary<TeamId, TeamRecord>? lastSeasonOverride = null)
    {
        var last = lastSeasonOverride ?? ThisSeasonRecords(snapshot, nextSeasonYear - 1);
        var before = ThisSeasonRecords(snapshot, nextSeasonYear - 2);
        var roster = team.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
        return OffseasonModel.DirectionFor(
            last.TryGetValue(team.Id, out var lastRecord) ? lastRecord : null,
            before.TryGetValue(team.Id, out var beforeRecord) ? beforeRecord : null,
            roster,
            asOf,
            PeakAgeStart(ruleset));
    }

    /// <summary>Each team's record in a concluded season, from the league's history (empty before any is archived).</summary>
    private static Dictionary<TeamId, TeamRecord> ThisSeasonRecords(LeagueSnapshot snapshot, int year) =>
        snapshot.League.History.FirstOrDefault(entry => entry.Season.Year == year)?.FinalStandings
            .ToDictionary(row => row.TeamId, row => row.Record)
        ?? [];

    private static DomainOperationResult<LeagueRuleset> BuildRuleset(LeagueSnapshot snapshot) =>
        snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
}
