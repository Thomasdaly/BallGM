using BallGM.Application.Leagues;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.Tests;

/// <summary>
/// A season engine the session tests can drive without a calendar, a schedule generator, or a match
/// model. The session's job is orchestration — refusing commands before a league is loaded, holding
/// the season, mapping outcomes onto read models — and that is what these tests are about.
/// </summary>
internal sealed class StubSeasonEngine : ISeasonEngine
{
    public SeasonRun? Started { get; private set; }

    public int LastAdvanceDays { get; private set; }

    public int AdvanceCallCount { get; private set; }

    public DomainError? StartFailure { get; set; }

    public IReadOnlyList<RuleFinding> Violations { get; set; } = [];

    public DomainOperationResult<SeasonStartOutcome> Start(LeagueSnapshot snapshot, DateOnly seasonStart, int seed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (StartFailure is not null)
        {
            return DomainOperationResult<SeasonStartOutcome>.Failure(StartFailure);
        }

        var calendarResult = LeagueCalendar.Create(
            snapshot.CurrentSeason,
            seasonStart,
            [new CalendarPhase(SeasonPhase.RegularSeason, SeasonDay.Opening, new SeasonDay(10))]);

        if (calendarResult.IsFailure)
        {
            return DomainOperationResult<SeasonStartOutcome>.Failure(calendarResult.Errors.ToArray());
        }

        var runResult = SeasonRun.Start(
            snapshot.CurrentSeason,
            new SeasonSeed(seed),
            calendarResult.Value,
            SeasonSchedule.Empty);

        if (runResult.IsFailure)
        {
            return DomainOperationResult<SeasonStartOutcome>.Failure(runResult.Errors.ToArray());
        }

        Started = runResult.Value;

        return DomainOperationResult<SeasonStartOutcome>.Success(new SeasonStartOutcome(
            runResult.Value,
            [],
            [],
            new Dictionary<string, int>()));
    }

    public DomainOperationResult<SeasonAdvanceOutcome> Assess(SeasonRun run, LeagueSnapshot snapshot, int days) =>
        DomainOperationResult<SeasonAdvanceOutcome>.Success(Outcome(run, days));

    public DomainOperationResult<SeasonAdvanceOutcome> Advance(SeasonRun run, LeagueSnapshot snapshot, int days)
    {
        ArgumentNullException.ThrowIfNull(run);

        AdvanceCallCount++;
        LastAdvanceDays = days;

        if (Violations.Count > 0)
        {
            return DomainOperationResult<SeasonAdvanceOutcome>.Failure(
                Violations.Select(finding => new DomainError(finding.RuleCode, finding.Explanation)).ToArray());
        }

        var advance = run.AdvanceTo(run.CurrentDay.Plus(Math.Max(0, days)));

        return advance.IsFailure
            ? DomainOperationResult<SeasonAdvanceOutcome>.Failure(advance.Errors.ToArray())
            : DomainOperationResult<SeasonAdvanceOutcome>.Success(Outcome(run, days));
    }

    public Standings Standings(SeasonRun run, LeagueSnapshot snapshot) => Domain.Seasons.Standings.Empty;

    public DomainOperationResult<DepthChartOutcome> DepthChart(
        SeasonRun run,
        LeagueSnapshot snapshot,
        TeamId teamId,
        SeasonDay day) =>
        DomainOperationResult<DepthChartOutcome>.Success(new DepthChartOutcome(Domain.Seasons.DepthChart.Empty(teamId), [], []));

    public DomainOperationResult<SeasonConclusionOutcome> ConcludeSeason(SeasonRun run, LeagueSnapshot snapshot) =>
        DomainOperationResult<SeasonConclusionOutcome>.Success(new SeasonConclusionOutcome(
            new SeasonHistoryEntry(run.Season, null, []), [], 0, []));

    private SeasonAdvanceOutcome Outcome(SeasonRun run, int days) =>
        new(
            run.CurrentDay.Index,
            run.CurrentDay.Index + Math.Max(0, days),
            "RegularSeason",
            "RegularSeason",
            [],
            Violations,
            [],
            [],
            []);
}
