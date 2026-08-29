using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;

namespace BallGM.Application.Seasons;

/// <summary>One thing the season rules had to say, formatted for a screen.</summary>
public sealed record SeasonFindingLine(string RuleCode, string Explanation, string? TeamName);

/// <summary>
/// What concluding a finished season changed, in the shape the Application port hands back. Mirrors
/// <c>BallGM.Rules.Seasons.ConcludedSeason</c> field for field — Application does not reference Rules,
/// so the port's own type carries the same information rather than exposing the Rules-layer one.
/// </summary>
public sealed record SeasonConclusionOutcome(
    SeasonHistoryEntry Entry,
    IReadOnlyList<PlayerId> PlayersReleasedToFreeAgency,
    int PlayersCreditedService,
    IReadOnlyList<RuleFinding> Notes);

/// <summary>One phase of the calendar, with both the day index and the date it maps onto.</summary>
public sealed record CalendarPhaseLine(
    string Phase,
    int StartDay,
    int EndDayExclusive,
    string StartDate,
    string EndDate,
    bool IsCurrent);

/// <summary>
/// Where the league is in its season.
/// <para>
/// Both the index and the date travel, because they answer different questions: the index is what
/// every rule in the game is expressed in — an offer expiry, a signing window, a fixture — and the
/// date is what a human reads. A screen that showed only one of them would leave a GM unable to
/// check a rule, or unable to tell what time of year it is.
/// </para>
/// </summary>
public sealed record SeasonCalendarSummary(
    int SeasonYear,
    string SeasonStartDate,
    int CurrentDay,
    string CurrentDate,
    string CurrentPhase,
    int LengthInDays,
    bool IsComplete,
    int PlayedGames,
    int ScheduledGames,
    IReadOnlyList<CalendarPhaseLine> Phases);

/// <summary>One fixture, with its result where it has been played.</summary>
public sealed record FixtureLine(
    string GameId,
    int Day,
    string Date,
    string Phase,
    string HomeTeamId,
    string HomeTeamName,
    string AwayTeamId,
    string AwayTeamName,
    bool Played,
    int? HomePoints,
    int? AwayPoints);

/// <summary>Every fixture on one day.</summary>
public sealed record ScheduleDayLine(int Day, string Date, string Phase, IReadOnlyList<FixtureLine> Fixtures);

/// <summary>
/// One line of the table. Division and conference records are null in a league that has no such
/// grouping — null rather than 0-0, so a screen can render "—" instead of a record that reads like
/// a team that lost every group game it never played.
/// </summary>
public sealed record StandingsLine(
    int Position,
    string TeamId,
    string TeamName,
    string? ConferenceName,
    string? DivisionName,
    int Wins,
    int Losses,
    int GamesPlayed,
    int? DivisionWins,
    int? DivisionLosses,
    int? ConferenceWins,
    int? ConferenceLosses,
    int PointsFor,
    int PointsAgainst,
    int PointDifferential);

/// <summary>
/// The table, and every note about how it was ordered.
/// <para>
/// <see cref="Notes"/> is not decoration. It carries every tie the league's stated sequence failed
/// to resolve, and every tie-break the league states that its own shape cannot support. A standings
/// screen that hid those would be presenting an order settled by a team identifier as though it
/// were a ruling, which is precisely the bug the tie-break sequence exists to prevent.
/// </para>
/// </summary>
public sealed record StandingsSummary(
    bool HasStatedTieBreaks,
    IReadOnlyList<string> TieBreakSequence,
    IReadOnlyList<StandingsLine> Rows,
    IReadOnlyList<SeasonFindingLine> Notes);

/// <summary>One player's line in a box score.</summary>
public sealed record BoxScoreLine(
    string PlayerId,
    string FullName,
    bool Started,
    int Minutes,
    int Points,
    int Rebounds,
    int Assists);

/// <summary>
/// One game as a screen shows it. <see cref="HasBoxScore"/> is false for a result recorded without
/// player lines, which is a legitimate way for a result to exist.
/// </summary>
public sealed record BoxScoreSummary(
    string GameId,
    int Day,
    string Date,
    string HomeTeamId,
    string HomeTeamName,
    int HomePoints,
    string AwayTeamId,
    string AwayTeamName,
    int AwayPoints,
    bool HasBoxScore,
    IReadOnlyList<BoxScoreLine> HomeLines,
    IReadOnlyList<BoxScoreLine> AwayLines);

/// <summary>
/// What advancing did, or would do, plus where the calendar ends up. The same record for both, so
/// the advance-date control renders a preview and a result identically.
/// </summary>
public sealed record SeasonAdvanceSummary(
    bool IsPermitted,
    int FromDay,
    int ToDay,
    string FromDate,
    string ToDate,
    string FromPhase,
    string ToPhase,
    int GamesInRange,
    int GamesPlayed,
    IReadOnlyList<FixtureLine> Fixtures,
    IReadOnlyList<SeasonFindingLine> Violations,
    IReadOnlyList<SeasonFindingLine> Warnings,
    IReadOnlyList<SeasonFindingLine> Notes,
    SeasonCalendarSummary Calendar);

/// <summary>One player in a rotation.</summary>
public sealed record DepthChartLine(
    string PlayerId,
    string FullName,
    int Overall,
    int DepthRank,
    int Minutes,
    bool IsStarter);

/// <summary>
/// One position in a team's rotation, columned exactly as the free-agency board columns a market —
/// same positional vocabulary, same ordering, so "our depth at the three" means one thing in this
/// game rather than two.
/// </summary>
public sealed record DepthChartPositionColumn(
    string Position,
    int Depth,
    int AllottedMinutes,
    IReadOnlyList<DepthChartLine> Players);

/// <summary>A team's rotation for one day, with whatever the rules had to say about building it.</summary>
public sealed record DepthChartSummary(
    string TeamId,
    string TeamName,
    int Day,
    string Date,
    int TotalMinutes,
    IReadOnlyList<DepthChartPositionColumn> Columns,
    IReadOnlyList<SeasonFindingLine> Warnings,
    IReadOnlyList<SeasonFindingLine> Notes);

/// <summary>
/// A season as one screen reads it: where the calendar is, what the table says, and what is on next.
/// </summary>
public sealed record SeasonSummary(
    SeasonCalendarSummary Calendar,
    StandingsSummary Standings,
    IReadOnlyList<ScheduleDayLine> UpcomingDays,
    IReadOnlyList<SeasonFindingLine> Warnings,
    IReadOnlyList<SeasonFindingLine> Notes);

/// <summary>One team's line in a concluded season's archived table.</summary>
public sealed record SeasonHistoryLine(
    int Position,
    string TeamId,
    string TeamName,
    int Wins,
    int Losses,
    int PointsFor,
    int PointsAgainst);

/// <summary>What concluding a season left behind, formatted for a screen.</summary>
public sealed record SeasonConclusionSummary(
    int ConcludedSeasonYear,
    string? ChampionTeamId,
    string? ChampionTeamName,
    IReadOnlyList<SeasonHistoryLine> FinalStandings,
    int PlayersReleasedToFreeAgency,
    int PlayersCreditedService,
    int NextSeasonYear,
    IReadOnlyList<SeasonFindingLine> Notes);
