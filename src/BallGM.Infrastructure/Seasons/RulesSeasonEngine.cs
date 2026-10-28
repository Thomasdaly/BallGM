using BallGM.Application.Leagues;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Rulesets;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;
using BallGM.Rules.Development;
using BallGM.Rules.Draft;
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
    private readonly DraftDay _draftDay = new();

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

    public IReadOnlyList<PlayerSeasonStatLine> PlayerSeasonStats(SeasonRun run, SeasonPhase phase)
    {
        ArgumentNullException.ThrowIfNull(run);
        return PlayerSeasonStatsCalculator.Calculate(run.Results, phase);
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
            rulesetResult.Value.PostseasonRules,
            rulesetResult.Value.AwardRules,
            PriorSeasons(snapshot, run.Season.Year - 1));

        return concluded.IsFailure
            ? DomainOperationResult<SeasonConclusionOutcome>.Failure(concluded.Errors.ToArray())
            : DomainOperationResult<SeasonConclusionOutcome>.Success(new SeasonConclusionOutcome(
                concluded.Value.Entry,
                concluded.Value.PlayersReleasedToFreeAgency,
                concluded.Value.PlayersCreditedService,
                concluded.Value.Notes));
    }

    public DomainOperationResult<PlayerDevelopmentOutcome> DevelopPlayers(LeagueSnapshot snapshot, DateOnly asOf, int seed, IReadOnlyDictionary<PlayerId, (int Days, string Injury)>? longInjuries = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rulesetResult = snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<PlayerDevelopmentOutcome>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var active = snapshot.Players.Where(player => !player.IsRetired).OrderBy(player => player.Id.Value, StringComparer.Ordinal).ToList();
        var rostered = snapshot.Teams.SelectMany(team => team.PlayerIds).Select(id => id.Value).ToHashSet(StringComparer.Ordinal);
        var underContract = snapshot.Contracts
            .Where(contract => !contract.IsTerminated && contract.TermFor(snapshot.CurrentSeason) is not null)
            .Select(contract => contract.PlayerId.Value)
            .ToHashSet(StringComparer.Ordinal);

        // Quality against the league, read before anyone moves: the percentile each player's Current
        // Ability stands at, which the retirement decision weighs.
        var byAbility = active.OrderBy(player => player.Rating.Overall).ThenBy(player => player.Id.Value, StringComparer.Ordinal).ToList();
        var percentile = byAbility
            .Select((player, rank) => (player.Id.Value, Percent: byAbility.Count <= 1 ? 50 : rank * 100 / (byAbility.Count - 1)))
            .ToDictionary(entry => entry.Value, entry => entry.Percent, StringComparer.Ordinal);

        // Who played in the season just concluded: a free agent who did not, and was not signed, leaves the league.
        var concludedLabel = LeagueSession.SeasonLabel(snapshot.CurrentSeason.Year - 1);
        // A player under contract at any point of it counts too: a deep reserve who never got into a game
        // was still in the league.
        var concludedSeason = new Season(snapshot.CurrentSeason.Year - 1);
        var playedThisSeason = snapshot.CareerHistory
            .Where(entry => entry.Value.Any(line => line.Season == concludedLabel && line.GamesPlayed > 0))
            .Select(entry => entry.Key)
            .Concat(snapshot.Contracts.Where(contract => contract.TermFor(concludedSeason) is not null).Select(contract => contract.PlayerId.Value))
            .ToHashSet(StringComparer.Ordinal);

        var developed = 0;
        var retired = new List<PlayerId>();
        var notes = new List<RuleFinding>();
        foreach (var player in active)
        {
            // One stream per player, mixed from the boundary's seed and his identifier, so his season
            // does not depend on how many players were listed before him.
            var random = new SeededRandomSource(SeedMixer.Mix(seed, player.Id.Value));
            var age = player.AgeOn(asOf);

            if (ruleset.DevelopmentRules.IsConfigured)
            {
                player.Develop(AttributeDevelopmentModel.Develop(player.Rating, age, ruleset.DevelopmentRules, random));
                developed++;
            }

            // A long injury this season leaves its mark after the year's development.
            if (longInjuries is not null && longInjuries.TryGetValue(player.Id, out var injury))
            {
                var (rating, finding) = InjuryAftermathModel.Apply(player.Rating, age, injury.Days, player.FullName, injury.Injury, random);
                player.Develop(rating);
                if (finding is not null)
                {
                    notes.Add(finding);
                }
            }

            var signed = rostered.Contains(player.Id.Value) || underContract.Contains(player.Id.Value);
            if (!ruleset.RetirementRules.IsConfigured || signed)
            {
                continue;
            }

            var leaves = RetirementModel.LeavesTheLeague(playedThisSeason.Contains(player.Id.Value), player.IsInjured, ruleset.RetirementRules);
            var verdict = leaves.Retires
                ? leaves
                : RetirementModel.Assess(age, percentile.GetValueOrDefault(player.Id.Value, 50), underContract: false, ruleset.RetirementRules, random);
            if (verdict.Retires && player.Retire().IsSuccess)
            {
                retired.Add(player.Id);
                notes.Add(new RuleFinding(verdict.Finding.RuleCode, $"{player.FullName} retires. {verdict.Finding.Explanation}"));
            }
        }

        return DomainOperationResult<PlayerDevelopmentOutcome>.Success(new PlayerDevelopmentOutcome(developed, retired, notes));
    }

    /// <summary>
    /// Each player's regular season the year before, from the league's career history: what the
    /// most-improved award measures this season against.
    /// </summary>
    private static Dictionary<PlayerId, PriorSeasonProduction> PriorSeasons(LeagueSnapshot snapshot, int year)
    {
        var label = LeagueSession.SeasonLabel(year);
        var prior = new Dictionary<PlayerId, PriorSeasonProduction>();
        foreach (var (playerId, seasons) in snapshot.CareerHistory)
        {
            var line = seasons.LastOrDefault(season => !season.IsPostseason && season.Season == label);
            if (line is not null && line.GamesPlayed > 0)
            {
                prior[new PlayerId(playerId)] = new PriorSeasonProduction(line.GamesPlayed, line.Points, line.Rebounds, line.Assists);
            }
        }

        return prior;
    }

    public DomainOperationResult<SeasonDraftOutcome> RunDraft(
        LeagueSnapshot snapshot,
        IReadOnlyList<SeasonHistoryTeamRecord> finalStandings,
        Season draftSeason,
        int seed)
    {
        var rulesetResult = snapshot.Configuration.ToRuleset(snapshot.League.Alignment.IsFlat);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<SeasonDraftOutcome>.Failure(rulesetResult.Errors.ToArray());
        }

        var ruleset = rulesetResult.Value;
        var random = new SeededRandomSource(seed);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);

        var dayResult = _draftDay.Run(
            draftSeason,
            finalStandings,
            snapshot.Teams,
            playersById,
            snapshot.DraftAssets,
            ruleset.DraftRules,
            ruleset.DraftClassRules,
            ruleset.DraftLotteryRules,
            ruleset.ScoutingRules,
            random);

        if (dayResult.IsFailure)
        {
            return DomainOperationResult<SeasonDraftOutcome>.Failure(dayResult.Errors.ToArray());
        }

        var notes = new List<RuleFinding>(dayResult.Value.Notes);

        // The draft just made used one season's picks; the tradable window moves on a season with it.
        if (ruleset.DraftRules.RoundCount > 0)
        {
            var horizon = Fixtures.FixtureDraftAssets.ExtendHorizon(snapshot.DraftAssets, snapshot.League.Id, draftSeason, snapshot.Franchises, ruleset.DraftRules);
            notes.AddRange(horizon.Errors.Select(error => new RuleFinding(error.Code, error.Message)));
        }

        var drafted = new List<DraftedPlayer>();
        var planned = snapshot.Teams.ToDictionary(team => team.Id, team => team.RosterCount);

        foreach (var selection in dayResult.Value.Selections)
        {
            var playerResult = Player.Create(
                new PlayerId(SortableId.NewId()),
                selection.Prospect.FullName,
                selection.Prospect.Position,
                selection.Prospect.TrueRating,
                selection.Prospect.BirthDate,
                seasonsOfService: 0);

            if (playerResult.IsFailure)
            {
                notes.AddRange(playerResult.Errors.Select(error => new RuleFinding(error.Code, error.Message)));
                continue;
            }

            var drafting = snapshot.Teams.First(team => team.Id == selection.TeamId);
            var roster = drafting.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
            var sign = OffseasonModel.SignsDraftPick(selection.Round, playerResult.Value.Rating, roster, planned[selection.TeamId], drafting.RosterLimits.MaximumPlayers);
            if (sign)
            {
                planned[selection.TeamId]++;
            }
            else
            {
                notes.Add(new RuleFinding("draft.pick_not_signed",
                    $"{drafting.Name} does not sign round {selection.Round} pick {selection.SelectionNumber} {playerResult.Value.FullName}: he does not earn a roster spot, and becomes a free agent.",
                    drafting.Id));
            }

            drafted.Add(new DraftedPlayer(selection.TeamId, playerResult.Value, selection.Round, selection.SelectionNumber) { ShouldSign = sign });
        }

        return DomainOperationResult<SeasonDraftOutcome>.Success(new SeasonDraftOutcome(draftSeason.Year, drafted, notes));
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

        // A team that tanked last season plays its young players this one (DevelopmentMinutesPolicy).
        var lastSeason = snapshot.League.History.FirstOrDefault(entry => entry.Season.Year == snapshot.CurrentSeason.Year - 1)?.FinalStandings
            .ToDictionary(row => row.TeamId, row => row.Record);
        var openingDay = new DateOnly(snapshot.CurrentSeason.Year, 10, 1);

        var teams = snapshot.Teams
            .Select(team => new SeasonTeam(
                team.Id,
                team.Name,
                team.RosterCount,
                team.PlayerIds
                    .Select(playerId => playersById.GetValueOrDefault(playerId))
                    .Where(player => player is not null && !player.IsInjured)
                    .Select(player => new AvailablePlayer(
                        player!.Id,
                        player.Position,
                        player.Rating,
                        DevelopmentMinutesPolicy.PriorityFor(lastSeason?.GetValueOrDefault(team.Id), player.AgeOn(openingDay)),
                        VeteranMinutesPolicy.LimitFor(player.AgeOn(openingDay))))
                    .ToList()))
            .ToList();

        return DomainOperationResult<SeasonContext>.Success(new SeasonContext(
            snapshot.CurrentSeason,
            snapshot.League,
            rulesetResult.Value,
            teams));
    }

}
