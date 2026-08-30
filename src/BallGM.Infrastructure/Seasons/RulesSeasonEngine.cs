using BallGM.Application.Leagues;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Rulesets;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;
using BallGM.Simulation.Seasons;

namespace BallGM.Infrastructure.Seasons;

/// <summary>
/// Adapts the simulation's season engine onto the Application port, mapping the loaded
/// <see cref="LeagueConfiguration"/> back into the rules types — the same trust boundary
/// <c>RulesCapLedger</c>, <c>RulesDraftAssetLedger</c>, <c>RulesTradeEngine</c>,
/// <c>RulesSigningEngine</c> and <c>RulesFreeAgencyMarket</c> already occupy.
/// <para>
/// This is also where availability is decided: a player carrying a standing injury is not offered
/// to the depth chart builder, because <c>Player.CurrentInjury</c> has no end date and a build that
/// cannot say when someone returns should not pretend they are fit. Injuries with a stated end are
/// season state and are filtered inside the engine against the day being played.
/// </para>
/// </summary>
public sealed class RulesSeasonEngine(IMatchEngine? matchEngine = null) : ISeasonEngine
{
    // The real model by default. A caller may still hand in UnplayedMatchEngine — a standings test
    // that wants to inject its own results, or a tool that only cares about the calendar — but a
    // build that ships without a game model would be a build in which no season ever finishes.
    private readonly SeasonEngine _engine = new(matchEngine ?? new PossessionMatchEngine());
    private readonly SeasonConclusion _seasonConclusion = new();

    public DomainOperationResult<SeasonStartOutcome> Start(LeagueSnapshot snapshot, DateOnly seasonStart, int seed)
    {
        var contextResult = BuildContext(snapshot);
        if (contextResult.IsFailure)
        {
            return DomainOperationResult<SeasonStartOutcome>.Failure(contextResult.Errors.ToArray());
        }

        var startResult = _engine.Start(contextResult.Value, seasonStart, seed);

        return startResult.IsFailure
            ? DomainOperationResult<SeasonStartOutcome>.Failure(startResult.Errors.ToArray())
            : DomainOperationResult<SeasonStartOutcome>.Success(new SeasonStartOutcome(
                startResult.Value.Run,
                startResult.Value.Warnings,
                startResult.Value.Notes,
                startResult.Value.GamesPerTeam));
    }

    public DomainOperationResult<SeasonAdvanceOutcome> Assess(SeasonRun run, LeagueSnapshot snapshot, int days)
    {
        var contextResult = BuildContext(snapshot);
        if (contextResult.IsFailure)
        {
            return DomainOperationResult<SeasonAdvanceOutcome>.Failure(contextResult.Errors.ToArray());
        }

        var assessment = _engine.Assess(run, contextResult.Value, days);

        return assessment.IsFailure
            ? DomainOperationResult<SeasonAdvanceOutcome>.Failure(assessment.Errors.ToArray())
            : DomainOperationResult<SeasonAdvanceOutcome>.Success(ToOutcome(assessment.Value, []));
    }

    public DomainOperationResult<SeasonAdvanceOutcome> Advance(SeasonRun run, LeagueSnapshot snapshot, int days)
    {
        var contextResult = BuildContext(snapshot);
        if (contextResult.IsFailure)
        {
            return DomainOperationResult<SeasonAdvanceOutcome>.Failure(contextResult.Errors.ToArray());
        }

        var advanced = _engine.Advance(run, contextResult.Value, days);

        return advanced.IsFailure
            ? DomainOperationResult<SeasonAdvanceOutcome>.Failure(advanced.Errors.ToArray())
            : DomainOperationResult<SeasonAdvanceOutcome>.Success(
                ToOutcome(advanced.Value.Assessment, advanced.Value.Played));
    }

    public Standings Standings(SeasonRun run, LeagueSnapshot snapshot)
    {
        var contextResult = BuildContext(snapshot);

        // A table is read on every repaint, so it answers rather than throws. An incoherent ruleset
        // has already failed loudly at Start; there is nothing useful this call can add by crashing.
        return contextResult.IsFailure
            ? Domain.Seasons.Standings.Empty
            : _engine.Standings(run, contextResult.Value);
    }

    public DomainOperationResult<DepthChartOutcome> DepthChart(
        SeasonRun run,
        LeagueSnapshot snapshot,
        TeamId teamId,
        SeasonDay day)
    {
        var contextResult = BuildContext(snapshot);
        if (contextResult.IsFailure)
        {
            return DomainOperationResult<DepthChartOutcome>.Failure(contextResult.Errors.ToArray());
        }

        var build = _engine.DepthChartFor(run, contextResult.Value, teamId, day);

        return build.IsFailure
            ? DomainOperationResult<DepthChartOutcome>.Failure(build.Errors.ToArray())
            : DomainOperationResult<DepthChartOutcome>.Success(new DepthChartOutcome(
                build.Value.Chart,
                build.Value.Warnings,
                build.Value.Notes));
    }

    public DomainOperationResult<SeasonConclusionOutcome> ConcludeSeason(SeasonRun run, LeagueSnapshot snapshot)
    {
        var rulesetResult = snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<SeasonConclusionOutcome>.Failure(rulesetResult.Errors.ToArray());
        }

        var teamNames = snapshot.Teams.ToDictionary(team => team.Id, team => team.Name);

        var concluded = _seasonConclusion.Conclude(
            snapshot.League,
            teamNames,
            snapshot.Teams,
            snapshot.Players,
            snapshot.Contracts,
            run,
            rulesetResult.Value.StandingsRules,
            rulesetResult.Value.PostseasonRules);

        return concluded.IsFailure
            ? DomainOperationResult<SeasonConclusionOutcome>.Failure(concluded.Errors.ToArray())
            : DomainOperationResult<SeasonConclusionOutcome>.Success(new SeasonConclusionOutcome(
                concluded.Value.Entry,
                concluded.Value.PlayersReleasedToFreeAgency,
                concluded.Value.PlayersCreditedService,
                concluded.Value.Notes));
    }

    private static SeasonAdvanceOutcome ToOutcome(SeasonAdvanceAssessment assessment, IReadOnlyList<GameResult> played) =>
        new(
            assessment.FromDay,
            assessment.ToDay,
            assessment.FromPhase,
            assessment.ToPhase,
            assessment.Fixtures,
            assessment.Violations,
            assessment.Warnings,
            assessment.Notes,
            played);

    /// <summary>
    /// Rebuilds the rules-layer view of a loaded league, including the squads. The configuration came
    /// from a file a modder can edit, so an incoherent set of rules fails explainably here rather
    /// than throwing out of a command.
    /// </summary>
    private static DomainOperationResult<SeasonContext> BuildContext(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rulesetResult = snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<SeasonContext>.Failure(rulesetResult.Errors.ToArray());
        }

        var playersById = snapshot.Players.ToDictionary(player => player.Id, player => player);

        var teams = snapshot.Teams
            .Select(team => new SeasonTeam(
                team.Id,
                team.Name,
                team.RosterCount,
                team.PlayerIds
                    .Select(playerId => playersById.GetValueOrDefault(playerId))
                    .Where(player => player is not null && !player.IsInjured)
                    .Select(player => new AvailablePlayer(player!.Id, player.Position, player.Rating.Overall))
                    .ToList()))
            .ToList();

        return DomainOperationResult<SeasonContext>.Success(new SeasonContext(
            snapshot.CurrentSeason,
            snapshot.League,
            rulesetResult.Value,
            teams));
    }

}
