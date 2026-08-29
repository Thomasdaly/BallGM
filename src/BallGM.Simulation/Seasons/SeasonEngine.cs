using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;

namespace BallGM.Simulation.Seasons;

/// <summary>A started season, with everything the rules had to say about starting it.</summary>
public sealed record SeasonStarted(
    SeasonRun Run,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    IReadOnlyDictionary<string, int> GamesPerTeam);

/// <summary>
/// What advancing the calendar would do, worked out without doing it. The counterpart to
/// <c>TradeAssessment</c> and <c>SigningAssessment</c>, and safe to call as often as a screen likes.
/// </summary>
public sealed record SeasonAdvanceAssessment(
    int FromDay,
    int ToDay,
    string FromPhase,
    string ToPhase,
    IReadOnlyList<Fixture> Fixtures,
    IReadOnlyList<RuleFinding> Violations,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes)
{
    public bool IsPermitted => Violations.Count == 0;
}

/// <summary>An advance that actually happened, and the games it played on the way.</summary>
public sealed record SeasonAdvanced(SeasonAdvanceAssessment Assessment, IReadOnlyList<GameResult> Played);

/// <summary>
/// Owns a season being played: builds its calendar and fixtures, moves it forward a day at a time,
/// plays whatever is scheduled, and reports the table.
/// <para>
/// It lives in <c>BallGM.Simulation</c> rather than <c>BallGM.Rules</c> because it drives the match
/// engine, and <c>Rules</c> is below <c>Simulation</c> in the dependency order. Every rule it
/// applies still belongs to <c>Rules</c> — the calendar builder, the schedule generator, the depth
/// chart, the standings calculator — so this type owns sequencing and nothing else. That is the
/// same division of labour the trade executor keeps with the trade validator.
/// </para>
/// <para>
/// <b>Assessment never mutates and execution rolls back.</b> <see cref="Advance"/> captures the
/// season before it touches anything, and puts it back in full if any day fails — a season left
/// half-advanced would have games played on days the league had not reached, which is the state
/// <c>SeasonRun.RecordResult</c> exists to refuse.
/// </para>
/// <para>
/// <b>No wall clock, anywhere.</b> Nothing here reads the current time. The calendar's dates are
/// derived from the season's stated start, every game's randomness is derived from the season seed
/// and the game's identifier, and days advance because a caller asked for them to. Two runs of the
/// same league advanced by the same number of days from the same seed are therefore identical.
/// </para>
/// </summary>
public sealed class SeasonEngine(IMatchEngine matchEngine)
{
    private const string NotAdvancingCode = "season.advance_of_no_days";
    private const string PastEndCode = "season.advance_past_end_of_season";
    private const string ShortOfFloorCode = "season.team_cannot_field_five";
    private const string NoPostseasonCode = "season.postseason_not_configured";
    private const string NoSigningWindowCode = "season.in_season_signing_window_not_configured";
    private const string NoEligibilityCutoffCode = "season.playoff_eligibility_cutoff_not_configured";
    private const string NoShortTermContractsCode = "season.short_term_contracts_not_configured";

    private readonly IMatchEngine _matchEngine = matchEngine ?? throw new ArgumentNullException(nameof(matchEngine));
    private readonly SeasonCalendarBuilder _calendarBuilder = new();
    private readonly ScheduleGenerator _scheduleGenerator = new();
    private readonly DepthChartBuilder _depthChartBuilder = new();
    private readonly StandingsCalculator _standingsCalculator = new();

    /// <summary>
    /// Builds the calendar and the fixture list, and opens the season on day 0 with nothing played.
    /// </summary>
    public DomainOperationResult<SeasonStarted> Start(SeasonContext context, DateOnly seasonStart, int seed)
    {
        ArgumentNullException.ThrowIfNull(context);

        var calendarResult = _calendarBuilder.Build(
            context.Season,
            seasonStart,
            context.Ruleset.ScheduleRules,
            context.Ruleset.PostseasonRules);

        if (calendarResult.IsFailure)
        {
            return DomainOperationResult<SeasonStarted>.Failure(calendarResult.Errors.ToArray());
        }

        var seasonSeed = new SeasonSeed(seed);

        var scheduleResult = _scheduleGenerator.Generate(
            context.Season,
            context.League,
            calendarResult.Value,
            context.Ruleset.ScheduleRules,
            context.Ruleset.RegularSeasonGameCount,
            seasonSeed);

        if (scheduleResult.IsFailure)
        {
            return DomainOperationResult<SeasonStarted>.Failure(scheduleResult.Errors.ToArray());
        }

        var runResult = SeasonRun.Start(
            context.Season,
            seasonSeed,
            calendarResult.Value,
            scheduleResult.Value.Schedule);

        if (runResult.IsFailure)
        {
            return DomainOperationResult<SeasonStarted>.Failure(runResult.Errors.ToArray());
        }

        var notes = new List<RuleFinding>(scheduleResult.Value.Notes);
        ReportUnconfiguredCalendarRules(context, notes);

        return DomainOperationResult<SeasonStarted>.Success(new SeasonStarted(
            runResult.Value,
            scheduleResult.Value.Warnings,
            notes,
            scheduleResult.Value.GamesPerTeam));
    }

    /// <summary>Works out what advancing <paramref name="days"/> days would do. Touches nothing.</summary>
    public DomainOperationResult<SeasonAdvanceAssessment> Assess(SeasonRun run, SeasonContext context, int days)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);

        var violations = new List<RuleFinding>();
        var warnings = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        if (days <= 0)
        {
            violations.Add(new RuleFinding(
                NotAdvancingCode,
                $"An advance of {days} day(s) moves the league nowhere. Time in a season runs forwards only."));
        }

        var target = run.CurrentDay.Plus(Math.Max(0, days));

        if (target > run.Calendar.EndDayExclusive)
        {
            violations.Add(new RuleFinding(
                PastEndCode,
                $"Season {run.Season.Year} runs {run.Calendar.LengthInDays} days and the league has reached {run.CurrentDay}, so it cannot advance {days} more."));

            target = run.Calendar.EndDayExclusive;
        }

        var fixtures = run.Schedule
            .Between(run.CurrentDay, target)
            .Where(fixture => !run.HasResult(fixture.Id))
            .ToArray();

        if (!_matchEngine.CanPlay && fixtures.Length > 0)
        {
            notes.Add(new RuleFinding(
                UnplayedMatchEngine.NoMatchEngineCode,
                $"{fixtures.Length} fixture(s) fall inside this advance, and this build has no model for deciding a game. The days pass and the fixtures stay unplayed."));
        }

        foreach (var team in context.Teams)
        {
            var availableCount = AvailableFor(team, run, run.CurrentDay).Count;

            if (availableCount < MinutesAllocationBounds.PlayersOnFloor)
            {
                warnings.Add(new RuleFinding(
                    ShortOfFloorCode,
                    $"{team.TeamName} has {availableCount} available player(s), fewer than the {MinutesAllocationBounds.PlayersOnFloor} needed to put a side on the floor.",
                    team.TeamId));
            }
        }

        ReportUnconfiguredCalendarRules(context, notes);

        return DomainOperationResult<SeasonAdvanceAssessment>.Success(new SeasonAdvanceAssessment(
            run.CurrentDay.Index,
            target.Index,
            DescribePhase(run.Calendar, run.CurrentDay),
            DescribePhase(run.Calendar, target),
            fixtures,
            violations,
            warnings,
            notes));
    }

    /// <summary>
    /// Moves the season forward, playing whatever falls inside the advance. Re-assesses against the
    /// season as it stands rather than trusting an assessment handed in, and restores the season in
    /// full if any day fails.
    /// </summary>
    public DomainOperationResult<SeasonAdvanced> Advance(SeasonRun run, SeasonContext context, int days)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);

        var assessmentResult = Assess(run, context, days);
        if (assessmentResult.IsFailure)
        {
            return DomainOperationResult<SeasonAdvanced>.Failure(assessmentResult.Errors.ToArray());
        }

        var assessment = assessmentResult.Value;
        if (!assessment.IsPermitted)
        {
            return DomainOperationResult<SeasonAdvanced>.Failure(
                assessment.Violations.Select(violation => new DomainError(violation.RuleCode, violation.Explanation)).ToArray());
        }

        var restorePoint = run.Capture();
        var played = new List<GameResult>();

        for (var dayIndex = assessment.FromDay; dayIndex < assessment.ToDay; dayIndex++)
        {
            var day = new SeasonDay(dayIndex);

            var advanceResult = run.AdvanceTo(day);
            if (advanceResult.IsFailure)
            {
                run.RestoreTo(restorePoint);
                return DomainOperationResult<SeasonAdvanced>.Failure(advanceResult.Errors.ToArray());
            }

            if (!_matchEngine.CanPlay)
            {
                continue;
            }

            var dayResult = PlayDay(run, context, day, played);
            if (dayResult.IsFailure)
            {
                run.RestoreTo(restorePoint);
                return DomainOperationResult<SeasonAdvanced>.Failure(dayResult.Errors.ToArray());
            }
        }

        var finalAdvance = run.AdvanceTo(new SeasonDay(assessment.ToDay));
        if (finalAdvance.IsFailure)
        {
            run.RestoreTo(restorePoint);
            return DomainOperationResult<SeasonAdvanced>.Failure(finalAdvance.Errors.ToArray());
        }

        return DomainOperationResult<SeasonAdvanced>.Success(new SeasonAdvanced(assessment, played));
    }

    /// <summary>The table as the games recorded so far leave it, ordered by the league's stated sequence.</summary>
    public Standings Standings(SeasonRun run, SeasonContext context)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);

        return _standingsCalculator.Calculate(
            context.League,
            context.TeamNames,
            run.Results,
            context.Ruleset.StandingsRules);
    }

    /// <summary>The rotation a team would field on a given day, given who is fit.</summary>
    public DomainOperationResult<DepthChartBuild> DepthChartFor(
        SeasonRun run,
        SeasonContext context,
        TeamId teamId,
        SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(day);

        var team = context.Team(teamId);
        if (team is null)
        {
            return DomainOperationResult<DepthChartBuild>.Failure(new DomainError(
                "season.unknown_team",
                $"Team '{teamId.Value}' is not a team in this league."));
        }

        return _depthChartBuilder.Build(
            teamId,
            AvailableFor(team, run, day),
            context.Ruleset.RosterLimits,
            team.RosterCount);
    }

    private DomainOperationResult PlayDay(
        SeasonRun run,
        SeasonContext context,
        SeasonDay day,
        List<GameResult> played)
    {
        foreach (var fixture in run.Schedule.On(day).Where(fixture => !run.HasResult(fixture.Id)))
        {
            var homeChart = DepthChartFor(run, context, fixture.HomeTeamId, day);
            if (homeChart.IsFailure)
            {
                return DomainOperationResult.Failure(homeChart.Errors.ToArray());
            }

            var awayChart = DepthChartFor(run, context, fixture.AwayTeamId, day);
            if (awayChart.IsFailure)
            {
                return DomainOperationResult.Failure(awayChart.Errors.ToArray());
            }

            var setup = new MatchSetup(
                fixture,
                homeChart.Value.Chart,
                awayChart.Value.Chart,
                run.Seed.ForGame(fixture.Id));

            var playResult = _matchEngine.Play(setup);
            if (playResult.IsFailure)
            {
                return DomainOperationResult.Failure(playResult.Errors.ToArray());
            }

            var recordResult = run.RecordResult(playResult.Value);
            if (recordResult.IsFailure)
            {
                return DomainOperationResult.Failure(recordResult.Errors.ToArray());
            }

            played.Add(playResult.Value);
        }

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Who a team can put on the floor: everyone on the roster the adapter judged fit, less anyone
    /// under an injury spell covering this day.
    /// </summary>
    private static IReadOnlyList<AvailablePlayer> AvailableFor(SeasonTeam team, SeasonRun run, SeasonDay day)
    {
        var unavailable = run.UnavailableOn(day).Select(playerId => playerId.Value).ToHashSet(StringComparer.Ordinal);

        return team.Players
            .Where(player => !unavailable.Contains(player.PlayerId.Value))
            .ToArray();
    }

    /// <summary>
    /// The calendar rules this league does not configure, each named. A check that never ran because
    /// a value was absent is indistinguishable from a check that ran and approved — the same reason
    /// the trade and signing assessments carry their own note lists.
    /// </summary>
    private static void ReportUnconfiguredCalendarRules(SeasonContext context, List<RuleFinding> notes)
    {
        if (!context.Ruleset.HasPostseason)
        {
            notes.Add(new RuleFinding(
                NoPostseasonCode,
                "This league configures no postseason, so its season ends when the regular season does and no bracket is drawn."));
        }

        if (!context.Ruleset.NegotiationRules.HasInSeasonSigningWindow)
        {
            notes.Add(new RuleFinding(
                NoSigningWindowCode,
                "This league states no in-season signing window, so no day of the season bars a signing."));
        }

        if (!context.Ruleset.PostseasonRules.HasEligibilityCutoff)
        {
            notes.Add(new RuleFinding(
                NoEligibilityCutoffCode,
                "This league states no playoff eligibility cutoff, so a player signed on the last day of the regular season is as eligible as one signed on the first."));
        }

        if (!context.Ruleset.NegotiationRules.HasShortTermContracts)
        {
            notes.Add(new RuleFinding(
                NoShortTermContractsCode,
                "This league states no short-term contract length, so every contract runs by seasons alone."));
        }
    }

    private static string DescribePhase(LeagueCalendar calendar, SeasonDay day)
    {
        var phase = calendar.PhaseOn(day);
        return phase.IsSuccess ? phase.Value.ToString() : "SeasonComplete";
    }
}
