using System.Globalization;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.Leagues;

/// <summary>
/// The calendar half of the session: starting a season, moving it forward, and reading the table,
/// the fixtures, and a box score out of it.
/// <para>
/// The season in progress is held here, beside the negotiations, and deliberately not on
/// <see cref="LeagueSnapshot"/> — for exactly the reason Milestone 6b kept a negotiation off it. A
/// schedule in the snapshot would give every read model in the game an opinion about what day it
/// is, and the cap sheet has no business knowing.
/// </para>
/// <para>
/// The session is what owns date advancement, as <c>docs/architecture.md</c> anticipated when it
/// introduced <see cref="LeagueSession"/> for trades. Nothing here reads a clock: a season opens on
/// a date derived from its year, and every day that passes does so because a caller asked.
/// </para>
/// </summary>
public sealed partial class LeagueSession
{
    private const string NoSeasonCode = "league_session.no_season_in_progress";
    private const string SeasonAlreadyStartedCode = "league_session.season_already_started";
    private const string UnknownGameCode = "league_session.unknown_game";
    private const string SeasonNotCompleteCode = "league_session.season_not_complete";

    private SeasonRun? _seasonRun;

    /// <summary>Whether a season is being played in this session.</summary>
    public bool HasSeason => _seasonRun is not null;

    /// <summary>
    /// Builds this league's calendar and fixture list and opens the season on day 0.
    /// <para>
    /// The opening date is derived from the season year rather than read from a clock, so a league
    /// started today and the same league started next week run the same season on the same dates.
    /// </para>
    /// </summary>
    public DomainOperationResult<SeasonSummary> StartSeason(int? seed = null)
    {
        if (_snapshot is null)
        {
            return NotLoaded<SeasonSummary>();
        }

        if (_seasonRun is not null)
        {
            return DomainOperationResult<SeasonSummary>.Failure(new DomainError(
                SeasonAlreadyStartedCode,
                $"Season {_seasonRun.Season.Year} is already under way in this session and has reached {_seasonRun.CurrentDay}. Starting it again would discard the games it has played."));
        }

        var seasonStart = new DateOnly(_snapshot.CurrentSeason.Year, SeasonOpeningMonth, SeasonOpeningDayOfMonth);

        var startResult = _seasonEngine.Start(_snapshot, seasonStart, seed ?? DefaultSeasonSeed);
        if (startResult.IsFailure)
        {
            return DomainOperationResult<SeasonSummary>.Failure(startResult.Errors.ToArray());
        }

        _seasonRun = startResult.Value.Run;

        return DomainOperationResult<SeasonSummary>.Success(BuildSummary(
            _snapshot,
            _seasonRun,
            startResult.Value.Warnings,
            startResult.Value.Notes));
    }

    /// <summary>
    /// Adopts a season loaded from a save. The load half of persistence, matching
    /// <c>AdoptNegotiation</c>: the file is replayed into an aggregate elsewhere, and this is where
    /// the session takes it over.
    /// </summary>
    public DomainOperationResult AdoptSeason(SeasonRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (_snapshot is null)
        {
            return DomainOperationResult.Failure(new DomainError(
                NotLoadedCode,
                "No league is loaded in this session, so there is nothing for a loaded season to belong to."));
        }

        _seasonRun = run;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Concludes a finished season: archives the champion and the final table, credits service time,
    /// releases expired contracts back into the free-agent pool, and advances the league to the next
    /// season year so <see cref="StartSeason"/> can be called again.
    /// <para>
    /// Refuses a season that has not reached its last day — the same "not reached yet" refusal
    /// <see cref="AdvanceDays"/>'s underlying engine already applies to a single game — and refuses a
    /// season already concluded through <c>League.RecordSeason</c>'s own check, surfaced here without
    /// this session having mutated anything first.
    /// </para>
    /// </summary>
    public DomainOperationResult<SeasonConclusionSummary> ConcludeSeason()
    {
        if (_snapshot is null)
        {
            return NotLoaded<SeasonConclusionSummary>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<SeasonConclusionSummary>();
        }

        if (!_seasonRun.IsComplete)
        {
            return DomainOperationResult<SeasonConclusionSummary>.Failure(new DomainError(
                SeasonNotCompleteCode,
                $"Season {_seasonRun.Season.Year} has reached {_seasonRun.CurrentDay} of {_seasonRun.Calendar.LengthInDays} days and cannot be concluded until it is played out."));
        }

        var concludedResult = _seasonEngine.ConcludeSeason(_seasonRun, _snapshot);
        if (concludedResult.IsFailure)
        {
            return DomainOperationResult<SeasonConclusionSummary>.Failure(concludedResult.Errors.ToArray());
        }

        var outcome = concludedResult.Value;
        var teamNames = TeamNames(_snapshot);
        var concludedYear = _seasonRun.Season.Year;

        _snapshot = _snapshot with { CurrentSeason = new Season(concludedYear + 1) };
        _seasonRun = null;

        return DomainOperationResult<SeasonConclusionSummary>.Success(new SeasonConclusionSummary(
            concludedYear,
            outcome.Entry.ChampionTeamId?.Value,
            outcome.Entry.ChampionTeamId is null
                ? null
                : teamNames.GetValueOrDefault(outcome.Entry.ChampionTeamId, outcome.Entry.ChampionTeamId.Value),
            outcome.Entry.FinalStandings.Select(row => ToLine(row, teamNames)).ToList(),
            outcome.PlayersReleasedToFreeAgency.Count,
            outcome.PlayersCreditedService,
            concludedYear + 1,
            outcome.Notes.Select(finding => ToSeasonLine(finding, teamNames)).ToList()));
    }

    private static SeasonHistoryLine ToLine(SeasonHistoryTeamRecord row, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(
            row.Position,
            row.TeamId.Value,
            teamNames.GetValueOrDefault(row.TeamId, row.TeamId.Value),
            row.Record.Wins,
            row.Record.Losses,
            row.PointsFor,
            row.PointsAgainst);

    /// <summary>The season as it stands: where the calendar is, what the table says, and what is on next.</summary>
    public DomainOperationResult<SeasonSummary> Season()
    {
        if (_snapshot is null)
        {
            return NotLoaded<SeasonSummary>();
        }

        return _seasonRun is null
            ? NoSeason<SeasonSummary>()
            : DomainOperationResult<SeasonSummary>.Success(BuildSummary(_snapshot, _seasonRun, [], []));
    }

    /// <summary>
    /// What advancing <paramref name="days"/> days would do. Changes nothing, so an advance-date
    /// control can preview on every keystroke — the same contract <c>AssessTrade</c> keeps.
    /// </summary>
    public DomainOperationResult<SeasonAdvanceSummary> AssessAdvance(int days)
    {
        if (_snapshot is null)
        {
            return NotLoaded<SeasonAdvanceSummary>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<SeasonAdvanceSummary>();
        }

        var assessment = _seasonEngine.Assess(_seasonRun, _snapshot, days);

        return assessment.IsFailure
            ? DomainOperationResult<SeasonAdvanceSummary>.Failure(assessment.Errors.ToArray())
            : DomainOperationResult<SeasonAdvanceSummary>.Success(ToSummary(_snapshot, _seasonRun, assessment.Value));
    }

    /// <summary>Advances the league by a number of days, playing whatever falls inside the advance.</summary>
    public DomainOperationResult<SeasonAdvanceSummary> AdvanceDays(int days)
    {
        if (_snapshot is null)
        {
            return NotLoaded<SeasonAdvanceSummary>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<SeasonAdvanceSummary>();
        }

        var advanced = _seasonEngine.Advance(_seasonRun, _snapshot, days);

        return advanced.IsFailure
            ? DomainOperationResult<SeasonAdvanceSummary>.Failure(advanced.Errors.ToArray())
            : DomainOperationResult<SeasonAdvanceSummary>.Success(ToSummary(_snapshot, _seasonRun, advanced.Value));
    }

    /// <summary>Advances to a stated day. Expressed in days so the engine has one notion of an advance.</summary>
    public DomainOperationResult<SeasonAdvanceSummary> AdvanceToDay(int day) =>
        _seasonRun is null
            ? _snapshot is null ? NotLoaded<SeasonAdvanceSummary>() : NoSeason<SeasonAdvanceSummary>()
            : AdvanceDays(day - _seasonRun.CurrentDay.Index);

    /// <summary>Advances to the end of the season, playing everything on the way.</summary>
    public DomainOperationResult<SeasonAdvanceSummary> AdvanceToEndOfSeason() =>
        _seasonRun is null
            ? _snapshot is null ? NotLoaded<SeasonAdvanceSummary>() : NoSeason<SeasonAdvanceSummary>()
            : AdvanceDays(_seasonRun.Calendar.EndDayExclusive.Index - _seasonRun.CurrentDay.Index);

    /// <summary>The table as the games played so far leave it.</summary>
    public DomainOperationResult<StandingsSummary> Standings()
    {
        if (_snapshot is null)
        {
            return NotLoaded<StandingsSummary>();
        }

        return _seasonRun is null
            ? NoSeason<StandingsSummary>()
            : DomainOperationResult<StandingsSummary>.Success(
                ToSummary(_snapshot, _seasonEngine.Standings(_seasonRun, _snapshot)));
    }

    /// <summary>Every fixture on a range of days, played or not.</summary>
    public DomainOperationResult<IReadOnlyList<ScheduleDayLine>> Schedule(int fromDay, int dayCount)
    {
        if (_snapshot is null)
        {
            return NotLoaded<IReadOnlyList<ScheduleDayLine>>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<IReadOnlyList<ScheduleDayLine>>();
        }

        var teamNames = TeamNames(_snapshot);
        var days = new List<ScheduleDayLine>();
        var last = Math.Min(fromDay + Math.Max(0, dayCount), _seasonRun.Calendar.EndDayExclusive.Index);

        for (var dayIndex = Math.Max(0, fromDay); dayIndex < last; dayIndex++)
        {
            var day = new SeasonDay(dayIndex);
            var fixtures = _seasonRun.Schedule.On(day);

            if (fixtures.Count == 0)
            {
                continue;
            }

            days.Add(new ScheduleDayLine(
                dayIndex,
                Describe(_seasonRun.Calendar.DateOn(day)),
                DescribePhase(_seasonRun, day),
                fixtures.Select(fixture => ToLine(_seasonRun, fixture, teamNames)).ToList()));
        }

        return DomainOperationResult<IReadOnlyList<ScheduleDayLine>>.Success(days);
    }

    /// <summary>One game's box score, or an explanation of why there is none.</summary>
    public DomainOperationResult<BoxScoreSummary> BoxScore(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        if (_snapshot is null)
        {
            return NotLoaded<BoxScoreSummary>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<BoxScoreSummary>();
        }

        var identifier = new GameId(gameId);
        var fixture = _seasonRun.Schedule.Game(identifier);

        if (fixture is null)
        {
            return DomainOperationResult<BoxScoreSummary>.Failure(new DomainError(
                UnknownGameCode,
                $"Game '{gameId}' is not in season {_seasonRun.Season.Year}'s schedule."));
        }

        var result = _seasonRun.ResultOf(identifier);
        if (result is null)
        {
            return DomainOperationResult<BoxScoreSummary>.Failure(new DomainError(
                UnknownGameCode,
                $"Game '{gameId}' is scheduled for day {fixture.Day.Index} and has not been played."));
        }

        return DomainOperationResult<BoxScoreSummary>.Success(ToSummary(_snapshot, _seasonRun, result));
    }

    /// <summary>Every box score from one day, in play order.</summary>
    public DomainOperationResult<IReadOnlyList<BoxScoreSummary>> BoxScoresOn(int day)
    {
        if (_snapshot is null)
        {
            return NotLoaded<IReadOnlyList<BoxScoreSummary>>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<IReadOnlyList<BoxScoreSummary>>();
        }

        var summaries = _seasonRun.Schedule
            .On(new SeasonDay(Math.Max(0, day)))
            .Select(fixture => _seasonRun.ResultOf(fixture.Id))
            .Where(result => result is not null)
            .Select(result => ToSummary(_snapshot, _seasonRun, result!))
            .ToList();

        return DomainOperationResult<IReadOnlyList<BoxScoreSummary>>.Success(summaries);
    }

    /// <summary>The rotation a team would field today, columned by position.</summary>
    public DomainOperationResult<DepthChartSummary> DepthChart(string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        if (_snapshot is null)
        {
            return NotLoaded<DepthChartSummary>();
        }

        if (_seasonRun is null)
        {
            return NoSeason<DepthChartSummary>();
        }

        var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == teamId);
        if (team is null)
        {
            return DomainOperationResult<DepthChartSummary>.Failure(new DomainError(
                UnknownTeamCode,
                $"Team '{teamId}' is not a team in this league."));
        }

        var chart = _seasonEngine.DepthChart(_seasonRun, _snapshot, team.Id, _seasonRun.CurrentDay);

        return chart.IsFailure
            ? DomainOperationResult<DepthChartSummary>.Failure(chart.Errors.ToArray())
            : DomainOperationResult<DepthChartSummary>.Success(
                ToSummary(_snapshot, _seasonRun, team, chart.Value));
    }

    private SeasonSummary BuildSummary(
        LeagueSnapshot snapshot,
        SeasonRun run,
        IReadOnlyList<RuleFinding> warnings,
        IReadOnlyList<RuleFinding> notes)
    {
        var teamNames = TeamNames(snapshot);
        var upcoming = new List<ScheduleDayLine>();

        for (var dayIndex = run.CurrentDay.Index; dayIndex < run.Calendar.EndDayExclusive.Index && upcoming.Count < UpcomingDaysShown; dayIndex++)
        {
            var day = new SeasonDay(dayIndex);
            var fixtures = run.Schedule.On(day);

            if (fixtures.Count == 0)
            {
                continue;
            }

            upcoming.Add(new ScheduleDayLine(
                dayIndex,
                Describe(run.Calendar.DateOn(day)),
                DescribePhase(run, day),
                fixtures.Select(fixture => ToLine(run, fixture, teamNames)).ToList()));
        }

        return new SeasonSummary(
            ToSummary(run),
            ToSummary(snapshot, _seasonEngine.Standings(run, snapshot)),
            upcoming,
            warnings.Select(finding => ToSeasonLine(finding, teamNames)).ToList(),
            notes.Select(finding => ToSeasonLine(finding, teamNames)).ToList());
    }

    /// <summary>How many days of fixtures a season screen shows ahead of the current day.</summary>
    private const int UpcomingDaysShown = 7;

    private static SeasonCalendarSummary ToSummary(SeasonRun run)
    {
        var phases = run.Calendar.Phases
            .Select(phase => new CalendarPhaseLine(
                phase.Phase.ToString(),
                phase.StartDay.Index,
                phase.EndDayExclusive.Index,
                Describe(run.Calendar.DateOn(phase.StartDay)),
                Describe(run.Calendar.DateOn(phase.LastDay)),
                phase.Contains(run.CurrentDay)))
            .ToList();

        return new SeasonCalendarSummary(
            run.Season.Year,
            Describe(run.Calendar.SeasonStart),
            run.CurrentDay.Index,
            Describe(run.Calendar.DateOn(run.IsComplete ? run.Calendar.LastDay : run.CurrentDay)),
            DescribePhase(run, run.CurrentDay),
            run.Calendar.LengthInDays,
            run.IsComplete,
            run.Results.Count,
            run.Schedule.Count,
            phases);
    }

    private SeasonAdvanceSummary ToSummary(LeagueSnapshot snapshot, SeasonRun run, SeasonAdvanceOutcome outcome)
    {
        var teamNames = TeamNames(snapshot);

        return new SeasonAdvanceSummary(
            outcome.IsPermitted,
            outcome.FromDay,
            outcome.ToDay,
            Describe(run.Calendar.SeasonStart.AddDays(outcome.FromDay)),
            Describe(run.Calendar.SeasonStart.AddDays(outcome.ToDay)),
            outcome.FromPhase,
            outcome.ToPhase,
            outcome.Fixtures.Count,
            outcome.Played.Count,
            outcome.Fixtures.Select(fixture => ToLine(run, fixture, teamNames)).ToList(),
            outcome.Violations.Select(finding => ToSeasonLine(finding, teamNames)).ToList(),
            outcome.Warnings.Select(finding => ToSeasonLine(finding, teamNames)).ToList(),
            outcome.Notes.Select(finding => ToSeasonLine(finding, teamNames)).ToList(),
            ToSummary(run));
    }

    private StandingsSummary ToSummary(LeagueSnapshot snapshot, Standings standings)
    {
        var teamNames = TeamNames(snapshot);
        var sequence = snapshot.Configuration.ResolvedTieBreaks;

        return new StandingsSummary(
            !sequence.IsEmpty,
            sequence.Steps.Select(step => step.ToString()).ToList(),
            standings.Rows.Select((row, index) => ToLine(row, index + 1)).ToList(),
            standings.Notes.Select(finding => ToSeasonLine(finding, teamNames)).ToList());
    }

    private static StandingsLine ToLine(StandingsRow row, int position) =>
        new(
            position,
            row.TeamId.Value,
            row.TeamName,
            row.ConferenceName,
            row.DivisionName,
            row.Overall.Wins,
            row.Overall.Losses,
            row.GamesPlayed,
            row.DivisionRecord?.Wins,
            row.DivisionRecord?.Losses,
            row.ConferenceRecord?.Wins,
            row.ConferenceRecord?.Losses,
            row.PointsFor,
            row.PointsAgainst,
            row.PointDifferential);

    private static BoxScoreSummary ToSummary(LeagueSnapshot snapshot, SeasonRun run, GameResult result)
    {
        var teamNames = TeamNames(snapshot);
        var playerNames = snapshot.Players.ToDictionary(player => player.Id.Value, player => player, StringComparer.Ordinal);

        return new BoxScoreSummary(
            result.GameId.Value,
            result.Day.Index,
            Describe(run.Calendar.DateOn(result.Day)),
            result.HomeTeamId.Value,
            teamNames.GetValueOrDefault(result.HomeTeamId, result.HomeTeamId.Value),
            result.HomePoints,
            result.AwayTeamId.Value,
            teamNames.GetValueOrDefault(result.AwayTeamId, result.AwayTeamId.Value),
            result.AwayPoints,
            result.BoxScore is not null,
            ToLines(result.BoxScore, result.HomeTeamId, playerNames),
            ToLines(result.BoxScore, result.AwayTeamId, playerNames));
    }

    private static IReadOnlyList<BoxScoreLine> ToLines(
        BoxScore? boxScore,
        TeamId teamId,
        IReadOnlyDictionary<string, Player> players) =>
        boxScore is null
            ? []
            : boxScore.LinesFor(teamId)
                .Select(line => new BoxScoreLine(
                    line.PlayerId.Value,
                    players.TryGetValue(line.PlayerId.Value, out var player) ? player.FullName : line.PlayerId.Value,
                    line.Started,
                    line.Minutes,
                    line.Points,
                    line.Rebounds,
                    line.Assists))
                .ToList();

    private static DepthChartSummary ToSummary(
        LeagueSnapshot snapshot,
        SeasonRun run,
        Team team,
        DepthChartOutcome outcome)
    {
        var players = snapshot.Players.ToDictionary(player => player.Id.Value, player => player, StringComparer.Ordinal);
        var teamNames = TeamNames(snapshot);

        var columns = Enum.GetValues<Position>()
            .Select(position =>
            {
                var slots = outcome.Chart.At(position);

                return new DepthChartPositionColumn(
                    GetLeagueOverviewQuery.DescribePosition(position),
                    slots.Count,
                    slots.Sum(slot => slot.Minutes),
                    slots
                        .Select(slot => new DepthChartLine(
                            slot.PlayerId.Value,
                            players.TryGetValue(slot.PlayerId.Value, out var player) ? player.FullName : slot.PlayerId.Value,
                            players.TryGetValue(slot.PlayerId.Value, out var rated) ? rated.Rating.Overall : 0,
                            slot.DepthRank,
                            slot.Minutes,
                            slot.IsStarter))
                        .ToList());
            })
            .ToList();

        return new DepthChartSummary(
            team.Id.Value,
            team.Name,
            run.CurrentDay.Index,
            Describe(run.Calendar.DateOn(run.CurrentDay)),
            outcome.Chart.TotalMinutes,
            columns,
            outcome.Warnings.Select(finding => ToSeasonLine(finding, teamNames)).ToList(),
            outcome.Notes.Select(finding => ToSeasonLine(finding, teamNames)).ToList());
    }

    private static FixtureLine ToLine(SeasonRun run, Fixture fixture, IReadOnlyDictionary<TeamId, string> teamNames)
    {
        var result = run.ResultOf(fixture.Id);

        return new FixtureLine(
            fixture.Id.Value,
            fixture.Day.Index,
            Describe(run.Calendar.DateOn(fixture.Day)),
            fixture.Phase.ToString(),
            fixture.HomeTeamId.Value,
            teamNames.GetValueOrDefault(fixture.HomeTeamId, fixture.HomeTeamId.Value),
            fixture.AwayTeamId.Value,
            teamNames.GetValueOrDefault(fixture.AwayTeamId, fixture.AwayTeamId.Value),
            result is not null,
            result?.HomePoints,
            result?.AwayPoints);
    }

    private static SeasonFindingLine ToSeasonLine(RuleFinding finding, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(
            finding.RuleCode,
            finding.Explanation,
            finding.TeamId is null ? null : teamNames.GetValueOrDefault(finding.TeamId, finding.TeamId.Value));

    private static IReadOnlyDictionary<TeamId, string> TeamNames(LeagueSnapshot snapshot) =>
        snapshot.Teams.ToDictionary(team => team.Id, team => team.Name);

    private static string DescribePhase(SeasonRun run, SeasonDay day)
    {
        var phase = run.Calendar.PhaseOn(day);
        return phase.IsSuccess ? phase.Value.ToString() : "SeasonComplete";
    }

    private static string Describe(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DomainOperationResult<T> NoSeason<T>() =>
        DomainOperationResult<T>.Failure(new DomainError(
            NoSeasonCode,
            "No season is under way in this session. Start one before advancing the calendar or reading a table."));
}
