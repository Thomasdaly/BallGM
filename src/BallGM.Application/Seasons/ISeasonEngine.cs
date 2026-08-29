using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.Seasons;

/// <summary>A started season: the run itself, plus everything the rules said about starting it.</summary>
public sealed record SeasonStartOutcome(
    SeasonRun Run,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    IReadOnlyDictionary<string, int> GamesPerTeam);

/// <summary>
/// What an advance would do, or did. The same shape either way, so an assessment and an execution
/// can be rendered by one screen without the client learning two layouts for one answer.
/// </summary>
public sealed record SeasonAdvanceOutcome(
    int FromDay,
    int ToDay,
    string FromPhase,
    string ToPhase,
    IReadOnlyList<Fixture> Fixtures,
    IReadOnlyList<RuleFinding> Violations,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    IReadOnlyList<GameResult> Played)
{
    public bool IsPermitted => Violations.Count == 0;
}

/// <summary>A built rotation, and what the rules had to say about building it.</summary>
public sealed record DepthChartOutcome(
    DepthChart Chart,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes);

/// <summary>
/// The port the Application layer reaches the calendar and the match engine through.
/// <para>
/// Identical in shape to <c>ICapLedger</c>, <c>IDraftAssetLedger</c>, <c>ITradeEngine</c>,
/// <c>ISigningEngine</c> and <c>IFreeAgencyMarket</c>, and for the same reason: the rules that
/// decide a schedule and the model that decides a game both live below a layer this project does
/// not reference, and the configuration travels in per call from the already-loaded
/// <see cref="LeagueConfiguration"/> rather than being loaded a second time behind the port.
/// </para>
/// <para>
/// It traffics in Domain types — <see cref="SeasonRun"/>, <see cref="Standings"/>,
/// <see cref="DepthChart"/> — because Application already depends on Domain. What it never exposes
/// is a Rules or Simulation type, which is what keeps <c>BallGM.Simulation</c> out of the
/// dependency graph of everything that merely wants to know what day it is.
/// </para>
/// </summary>
public interface ISeasonEngine
{
    /// <summary>Builds the calendar and fixtures for a season and opens it on day 0.</summary>
    DomainOperationResult<SeasonStartOutcome> Start(LeagueSnapshot snapshot, DateOnly seasonStart, int seed);

    /// <summary>Works out what advancing would do. Never touches the season.</summary>
    DomainOperationResult<SeasonAdvanceOutcome> Assess(SeasonRun run, LeagueSnapshot snapshot, int days);

    /// <summary>Advances for real, re-validating first and restoring the season in full if any day fails.</summary>
    DomainOperationResult<SeasonAdvanceOutcome> Advance(SeasonRun run, LeagueSnapshot snapshot, int days);

    /// <summary>The table as the recorded results leave it.</summary>
    Standings Standings(SeasonRun run, LeagueSnapshot snapshot);

    /// <summary>The rotation a team would field on a given day.</summary>
    DomainOperationResult<DepthChartOutcome> DepthChart(SeasonRun run, LeagueSnapshot snapshot, TeamId teamId, SeasonDay day);

    /// <summary>
    /// Concludes a finished season: archives the champion and the final table, credits service time
    /// to everyone who was rostered through it, and releases every contract whose last season has
    /// elapsed back into the free-agent pool. Refuses a season that has not been played out.
    /// </summary>
    DomainOperationResult<SeasonConclusionOutcome> ConcludeSeason(SeasonRun run, LeagueSnapshot snapshot);
}
