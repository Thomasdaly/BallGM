using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Infrastructure.Seasons;

/// <summary>
/// Saves and loads one season in progress.
/// <para>
/// Loading <b>replays the season through the aggregate's own methods</b> rather than assigning
/// fields — the same discipline <c>NegotiationSerializer</c> keeps. A file claiming a game played on
/// a day the league had not reached, a result for a fixture the schedule does not contain, or a game
/// recorded twice is refused by exactly the rule that would have refused it live.
/// </para>
/// <para>
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> is set for the usual reason: a file from a
/// later build fails structurally instead of silently dropping half a season.
/// </para>
/// </summary>
public sealed class SeasonSerializer
{
    private const string MalformedFileCode = "season_save.malformed_file";
    private const string UnsupportedSchemaVersionCode = "season_save.unsupported_schema_version";
    private const string InvalidFieldCode = "season_save.invalid_field";
    private const string UnknownGameCode = "season_save.result_for_unscheduled_game";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string Serialize(SeasonRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var envelope = new SeasonEnvelope(
            SeasonEnvelope.CurrentSchemaVersion,
            run.Season.Year,
            run.Seed.Value,
            run.Calendar.SeasonStart.ToString(DateFormat, CultureInfo.InvariantCulture),
            run.CurrentDay.Index,
            run.Calendar.Phases
                .Select(phase => new CalendarPhaseEnvelope(
                    phase.Phase.ToString(),
                    phase.StartDay.Index,
                    phase.EndDayExclusive.Index))
                .ToList(),
            run.Schedule.Fixtures
                .Select(fixture => new FixtureEnvelope(
                    fixture.Id.Value,
                    fixture.Day.Index,
                    fixture.HomeTeamId.Value,
                    fixture.AwayTeamId.Value,
                    fixture.Phase.ToString()))
                .ToList(),
            run.ResultsInPlayOrder.Select(ToEnvelope).ToList(),
            run.Injuries
                .Select(spell => new InjurySpellEnvelope(
                    spell.PlayerId.Value,
                    spell.Description,
                    spell.FromDay.Index,
                    spell.UntilDayExclusive.Index))
                .ToList());

        return JsonSerializer.Serialize(envelope, Options);
    }

    public DomainOperationResult<SeasonRun> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        SeasonEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SeasonEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            return DomainOperationResult<SeasonRun>.Failure(
                new DomainError(MalformedFileCode, $"The season save is not valid JSON: {exception.Message}"));
        }

        if (envelope is null)
        {
            return DomainOperationResult<SeasonRun>.Failure(
                new DomainError(MalformedFileCode, "The season save did not contain a season."));
        }

        if (envelope.SchemaVersion != SeasonEnvelope.CurrentSchemaVersion)
        {
            return DomainOperationResult<SeasonRun>.Failure(new DomainError(
                UnsupportedSchemaVersionCode,
                $"Season save schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {SeasonEnvelope.CurrentSchemaVersion}."));
        }

        try
        {
            return Rebuild(envelope);
        }
        catch (ArgumentException exception)
        {
            return DomainOperationResult<SeasonRun>.Failure(new DomainError(InvalidFieldCode, exception.Message));
        }
    }

    private static DomainOperationResult<SeasonRun> Rebuild(SeasonEnvelope envelope)
    {
        if (!DateOnly.TryParseExact(envelope.SeasonStartDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var seasonStart))
        {
            return DomainOperationResult<SeasonRun>.Failure(new DomainError(
                InvalidFieldCode,
                $"'{envelope.SeasonStartDate}' is not a season start date. Expected the form {DateFormat}."));
        }

        var season = new Season(envelope.SeasonYear);
        var phases = new List<CalendarPhase>(envelope.Phases.Count);

        foreach (var phase in envelope.Phases)
        {
            if (!Enum.TryParse<SeasonPhase>(phase.Phase, out var parsed) || !Enum.IsDefined(parsed))
            {
                return DomainOperationResult<SeasonRun>.Failure(new DomainError(
                    InvalidFieldCode,
                    $"'{phase.Phase}' is not a season phase this build knows. Expected one of: {string.Join(", ", Enum.GetNames<SeasonPhase>())}."));
            }

            phases.Add(new CalendarPhase(parsed, new SeasonDay(phase.StartDay), new SeasonDay(phase.EndDayExclusive)));
        }

        var calendarResult = LeagueCalendar.Create(season, seasonStart, phases);
        if (calendarResult.IsFailure)
        {
            return DomainOperationResult<SeasonRun>.Failure(calendarResult.Errors.ToArray());
        }

        var fixtures = new List<Fixture>(envelope.Fixtures.Count);

        foreach (var fixture in envelope.Fixtures)
        {
            if (!Enum.TryParse<SeasonPhase>(fixture.Phase, out var parsed) || !Enum.IsDefined(parsed))
            {
                return DomainOperationResult<SeasonRun>.Failure(new DomainError(
                    InvalidFieldCode,
                    $"Game '{fixture.GameId}' is saved in phase '{fixture.Phase}', which is not a season phase this build knows."));
            }

            fixtures.Add(new Fixture(
                new GameId(fixture.GameId),
                new SeasonDay(fixture.Day),
                new TeamId(fixture.HomeTeamId),
                new TeamId(fixture.AwayTeamId),
                parsed));
        }

        var scheduleResult = SeasonSchedule.Create(fixtures);
        if (scheduleResult.IsFailure)
        {
            return DomainOperationResult<SeasonRun>.Failure(scheduleResult.Errors.ToArray());
        }

        var resultsResult = RebuildResults(envelope, scheduleResult.Value);
        if (resultsResult.IsFailure)
        {
            return DomainOperationResult<SeasonRun>.Failure(resultsResult.Errors.ToArray());
        }

        var injuries = envelope.Injuries
            .Select(spell => new InjurySpell(
                new PlayerId(spell.PlayerId),
                spell.Description,
                new SeasonDay(spell.FromDay),
                new SeasonDay(spell.UntilDayExclusive)))
            .ToList();

        return SeasonRun.Rehydrate(
            season,
            new SeasonSeed(envelope.Seed),
            calendarResult.Value,
            scheduleResult.Value,
            new SeasonDay(envelope.CurrentDay),
            resultsResult.Value,
            injuries);
    }

    private static DomainOperationResult<IReadOnlyList<GameResult>> RebuildResults(
        SeasonEnvelope envelope,
        SeasonSchedule schedule)
    {
        var results = new List<GameResult>(envelope.Results.Count);

        foreach (var saved in envelope.Results)
        {
            var fixture = schedule.Game(new GameId(saved.GameId));
            if (fixture is null)
            {
                return DomainOperationResult<IReadOnlyList<GameResult>>.Failure(new DomainError(
                    UnknownGameCode,
                    $"The save records a result for game '{saved.GameId}', which is not in the schedule it also saves."));
            }

            BoxScore? boxScore = null;

            if (saved.BoxScore is not null)
            {
                var boxScoreResult = BoxScore.Create(
                    fixture.Id,
                    fixture.HomeTeamId,
                    fixture.AwayTeamId,
                    saved.HomePoints,
                    saved.AwayPoints,
                    saved.BoxScore.Select(line => new PlayerStatLine(
                        new PlayerId(line.PlayerId),
                        new TeamId(line.TeamId),
                        line.Minutes,
                        line.Points,
                        line.Rebounds,
                        line.Assists,
                        line.Started)));

                if (boxScoreResult.IsFailure)
                {
                    return DomainOperationResult<IReadOnlyList<GameResult>>.Failure(boxScoreResult.Errors.ToArray());
                }

                boxScore = boxScoreResult.Value;
            }

            var gameResult = GameResult.Create(fixture, saved.HomePoints, saved.AwayPoints, boxScore);
            if (gameResult.IsFailure)
            {
                return DomainOperationResult<IReadOnlyList<GameResult>>.Failure(gameResult.Errors.ToArray());
            }

            results.Add(gameResult.Value);
        }

        return DomainOperationResult<IReadOnlyList<GameResult>>.Success(results);
    }

    private static GameResultEnvelope ToEnvelope(GameResult result) =>
        new(
            result.GameId.Value,
            result.HomePoints,
            result.AwayPoints,
            result.BoxScore is null
                ? null
                : result.BoxScore.Lines
                    .Select(line => new PlayerStatLineEnvelope(
                        line.PlayerId.Value,
                        line.TeamId.Value,
                        line.Minutes,
                        line.Points,
                        line.Rebounds,
                        line.Assists,
                        line.Started))
                    .ToList());
}
