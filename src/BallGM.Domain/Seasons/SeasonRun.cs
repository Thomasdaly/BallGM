using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;

namespace BallGM.Domain.Seasons;

/// <summary>
/// Everything a season being played consists of: the day it has reached, the calendar it is running
/// against, the fixtures it will play, the results it has so far, who is hurt, and the one seed all
/// of that randomness comes from.
/// <para>
/// Session state, not league state — held by <c>LeagueSession</c> the way an in-flight
/// <c>Negotiation</c> is, and deliberately not on <c>LeagueSnapshot</c>. Putting a schedule in the
/// snapshot would give every read model in the game an opinion about it, and the cap sheet has no
/// business knowing what day it is.
/// </para>
/// <para>
/// Time only moves forwards. <see cref="AdvanceTo"/> refuses a day already passed, because a season
/// that could rewind would replay games that already have results and quietly double every record
/// in the table. Undo exists, but it is <see cref="RestoreTo"/> — a plain state restore, like
/// <c>Negotiation.RestoreTo</c> and <c>Team.RestoreRoster</c>, because an undo that can be refused
/// is not an undo.
/// </para>
/// </summary>
public sealed class SeasonRun
{
    private const string DayInThePastCode = "season.day_already_passed";
    private const string DayBeyondCalendarCode = "season.day_beyond_calendar";
    private const string UnknownGameCode = "season.result_for_unscheduled_game";
    private const string DuplicateResultCode = "season.game_already_played";
    private const string GameNotYetReachedCode = "season.game_not_yet_reached";

    private readonly Dictionary<string, GameResult> _results;
    private readonly List<InjurySpell> _injuries;

    private SeasonRun(
        Season season,
        SeasonSeed seed,
        LeagueCalendar calendar,
        SeasonSchedule schedule,
        SeasonDay currentDay,
        Dictionary<string, GameResult> results,
        List<InjurySpell> injuries)
    {
        Season = season;
        Seed = seed;
        Calendar = calendar;
        Schedule = schedule;
        CurrentDay = currentDay;
        _results = results;
        _injuries = injuries;
    }

    /// <summary>
    /// Starts a season on its opening day with nothing played. A calendar that does not cover the
    /// schedule is refused here rather than at the first advance that walks off the end of it.
    /// </summary>
    public static DomainOperationResult<SeasonRun> Start(
        Season season,
        SeasonSeed seed,
        LeagueCalendar calendar,
        SeasonSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(schedule);

        var uncovered = schedule.Fixtures.Where(fixture => !calendar.Covers(fixture.Day)).ToArray();
        if (uncovered.Length > 0)
        {
            return DomainOperationResult<SeasonRun>.Failure(new DomainError(
                DayBeyondCalendarCode,
                $"{uncovered.Length} fixture(s) fall on days the season {season.Year} calendar does not cover — the first is game '{uncovered[0].Id.Value}' on {uncovered[0].Day}, and the calendar runs {calendar.LengthInDays} days."));
        }

        return DomainOperationResult<SeasonRun>.Success(new SeasonRun(
            season,
            seed,
            calendar,
            schedule,
            SeasonDay.Opening,
            [],
            []));
    }

    public Season Season { get; }

    public SeasonSeed Seed { get; }

    public LeagueCalendar Calendar { get; }

    public SeasonSchedule Schedule { get; private set; }

    /// <summary>The day the league has reached. Games on this day have not been played yet.</summary>
    public SeasonDay CurrentDay { get; private set; }

    public IReadOnlyCollection<GameResult> Results => _results.Values;

    public IReadOnlyList<InjurySpell> Injuries => _injuries;

    public bool IsComplete => CurrentDay >= Calendar.EndDayExclusive;

    public DomainOperationResult<SeasonPhase> CurrentPhase => Calendar.PhaseOn(CurrentDay);

    public GameResult? ResultOf(GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        return _results.GetValueOrDefault(gameId.Value);
    }

    public bool HasResult(GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        return _results.ContainsKey(gameId.Value);
    }

    /// <summary>Fixtures on or after the current day that have not been played.</summary>
    public IReadOnlyList<Fixture> Unplayed =>
        Schedule.Fixtures.Where(fixture => !HasResult(fixture.Id)).ToArray();

    /// <summary>Results in play order, which is the order they have to be read back in.</summary>
    public IReadOnlyList<GameResult> ResultsInPlayOrder =>
        Schedule.Fixtures
            .Select(fixture => ResultOf(fixture.Id))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();

    public IReadOnlyList<PlayerId> UnavailableOn(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        return _injuries
            .Where(spell => spell.Covers(day))
            .Select(spell => spell.PlayerId)
            .Distinct()
            .OrderBy(playerId => playerId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Moves the league to a later day. Forwards only, and never past the end of the calendar: both
    /// are things a caller can legitimately attempt from a screen, so both are answers rather than
    /// exceptions.
    /// </summary>
    public DomainOperationResult AdvanceTo(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        if (day < CurrentDay)
        {
            return DomainOperationResult.Failure(new DomainError(
                DayInThePastCode,
                $"The league has already reached {CurrentDay} and cannot go back to {day}. Games that have been played stay played."));
        }

        if (day > Calendar.EndDayExclusive)
        {
            return DomainOperationResult.Failure(new DomainError(
                DayBeyondCalendarCode,
                $"Season {Season.Year} runs {Calendar.LengthInDays} days, so it cannot be advanced to {day}."));
        }

        CurrentDay = day;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Records what happened in one game. Refuses a game the schedule does not have, one already
    /// played, and one on a day the league has not reached — a result from the future is either a
    /// simulation bug or a save asserting something that could not have happened, and both should
    /// fail here rather than appear in a table.
    /// </summary>
    public DomainOperationResult RecordResult(GameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var fixture = Schedule.Game(result.GameId);
        if (fixture is null)
        {
            return DomainOperationResult.Failure(new DomainError(
                UnknownGameCode,
                $"Game '{result.GameId.Value}' is not in season {Season.Year}'s schedule."));
        }

        if (_results.ContainsKey(result.GameId.Value))
        {
            return DomainOperationResult.Failure(new DomainError(
                DuplicateResultCode,
                $"Game '{result.GameId.Value}' has already been played. A game recorded twice counts twice in the table."));
        }

        if (fixture.Day > CurrentDay)
        {
            return DomainOperationResult.Failure(new DomainError(
                GameNotYetReachedCode,
                $"Game '{result.GameId.Value}' is scheduled for {fixture.Day} and the league has only reached {CurrentDay}."));
        }

        _results[result.GameId.Value] = result;
        return DomainOperationResult.Success;
    }

    public DomainOperationResult RecordInjury(InjurySpell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        _injuries.Add(spell);
        return DomainOperationResult.Success;
    }

    /// <summary>Adds fixtures — how a postseason bracket joins the schedule once it can be drawn.</summary>
    public DomainOperationResult ExtendSchedule(IEnumerable<Fixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        var supplied = fixtures.ToArray();
        var uncovered = supplied.Where(fixture => !Calendar.Covers(fixture.Day)).ToArray();

        if (uncovered.Length > 0)
        {
            return DomainOperationResult.Failure(new DomainError(
                DayBeyondCalendarCode,
                $"Game '{uncovered[0].Id.Value}' falls on {uncovered[0].Day}, which season {Season.Year}'s calendar does not cover."));
        }

        var extended = Schedule.With(supplied);
        if (extended.IsFailure)
        {
            return DomainOperationResult.Failure(extended.Errors.ToArray());
        }

        Schedule = extended.Value;
        return DomainOperationResult.Success;
    }

    /// <summary>Everything that can change, captured so a failed advance can put it all back.</summary>
    public SeasonRunState Capture() => new(Schedule, CurrentDay, _results.Values.ToArray(), _injuries.ToArray());

    /// <summary>
    /// Puts the season back exactly as it was. Takes no view on any rule, for the same reason
    /// <c>Team.RestoreRoster</c> does not: the state it restores was legal when it was left.
    /// </summary>
    public void RestoreTo(SeasonRunState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Schedule = state.Schedule;
        CurrentDay = state.CurrentDay;

        _results.Clear();
        foreach (var result in state.Results)
        {
            _results[result.GameId.Value] = result;
        }

        _injuries.Clear();
        _injuries.AddRange(state.Injuries);
    }

    /// <summary>
    /// Rebuilds a season from a save. Every result goes back through <see cref="RecordResult"/> and
    /// every advance through <see cref="AdvanceTo"/>, so a file claiming a game played on a day the
    /// league had not reached fails exactly the way it would have failed live — the same replay
    /// discipline <c>NegotiationSerializer</c> already keeps.
    /// </summary>
    public static DomainOperationResult<SeasonRun> Rehydrate(
        Season season,
        SeasonSeed seed,
        LeagueCalendar calendar,
        SeasonSchedule schedule,
        SeasonDay currentDay,
        IEnumerable<GameResult> results,
        IEnumerable<InjurySpell> injuries)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(injuries);

        var startResult = Start(season, seed, calendar, schedule);
        if (startResult.IsFailure)
        {
            return startResult;
        }

        var run = startResult.Value;

        var advanceResult = run.AdvanceTo(currentDay);
        if (advanceResult.IsFailure)
        {
            return DomainOperationResult<SeasonRun>.Failure(advanceResult.Errors.ToArray());
        }

        foreach (var result in results)
        {
            var recordResult = run.RecordResult(result);
            if (recordResult.IsFailure)
            {
                return DomainOperationResult<SeasonRun>.Failure(recordResult.Errors.ToArray());
            }
        }

        foreach (var spell in injuries)
        {
            run.RecordInjury(spell);
        }

        return DomainOperationResult<SeasonRun>.Success(run);
    }
}

/// <summary>A captured <see cref="SeasonRun"/>, for putting one back after a failed advance.</summary>
public sealed record SeasonRunState(
    SeasonSchedule Schedule,
    SeasonDay CurrentDay,
    IReadOnlyList<GameResult> Results,
    IReadOnlyList<InjurySpell> Injuries);
