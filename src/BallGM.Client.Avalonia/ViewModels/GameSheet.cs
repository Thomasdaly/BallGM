using System.Globalization;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One game, both sides of it, side by side: the tale of the tape (record, form, scoring), the
/// season series so far, and the two line-ups — the projected rotation before tip-off, the box
/// score after it. Built from what the session already hands out; nothing here is a rule.
/// <para>
/// A preview's line-up is today's depth chart, so it is labelled projected: a rotation can change
/// between now and the game (an injury, a signing, a trade), and a sheet that called it the line-up
/// would be stating something the league has not decided.
/// </para>
/// </summary>
public sealed partial class GameSheet
{
    /// <summary>How many of a team's most recent games its form and recent scoring are read from.</summary>
    public const int RecentGames = 5;

    private GameSheet(
        string gameId,
        bool isFinal,
        string status,
        string dayLine,
        GameSheetSide away,
        GameSheetSide home,
        IReadOnlyList<MeetingRow> meetings,
        IReadOnlyList<FactorRow> fourFactors)
    {
        GameId = gameId;
        IsFinal = isFinal;
        Status = status;
        DayLine = dayLine;
        Away = away;
        Home = home;
        Meetings = meetings;
        FourFactors = fourFactors;
    }

    public string GameId { get; }

    public bool IsFinal { get; }

    public bool IsPreview => !IsFinal;

    /// <summary>"Final", "Today", "Tomorrow", "In 4 days".</summary>
    public string Status { get; }

    public string DayLine { get; }

    public GameSheetSide Away { get; }

    public GameSheetSide Home { get; }

    /// <summary>The two teams' other meetings this season, played and to come, in day order.</summary>
    public IReadOnlyList<MeetingRow> Meetings { get; }

    public bool HasMeetings => Meetings.Count > 0;

    /// <summary>
    /// The four factors for a final game, away and home — the team-level explanation of the result:
    /// who shot better, looked after the ball, crashed the glass, and got to the line.
    /// </summary>
    public IReadOnlyList<FactorRow> FourFactors { get; }

    public bool HasFourFactors => FourFactors.Count > 0;

    public string LineupHeading => IsFinal ? "Box score" : "Projected rotations";

    // The sheet's three columns that are not counting stats of the row's own line: each defined once,
    // so its header and every cell under it share one label, tooltip and width.
    private static readonly StatHeader ProjectedMinutesHeader = new("MIN", "Projected minutes from today's depth chart.", 38);
    private static readonly StatHeader RecentScoringHeader = new("L5 PTS", $"Points per game over this player's last {RecentGames} team games.", 48);
    private static readonly StatHeader SeasonPpgHeader = new("PPG", "Season points per game, regular season.", 44);

    private static readonly IReadOnlyList<StatColumn> PreviewSeasonColumns =
        [StatColumns.Points, StatColumns.Rebounds, StatColumns.Assists, StatColumns.Steals, StatColumns.Blocks, StatColumns.Turnovers, StatColumns.PersonalFouls, StatColumns.PlusMinus, StatColumns.FieldGoalPercent, StatColumns.TrueShooting];

    private static readonly IReadOnlyList<StatHeader> FinalHeaders =
        [.. StatColumns.Headers(StatColumns.BoxScore), SeasonPpgHeader];

    private static readonly IReadOnlyList<StatHeader> PreviewHeaders =
    [
        ProjectedMinutesHeader,
        .. StatColumns.Headers(PreviewSeasonColumns, StatBasis.PerGame),
        RecentScoringHeader,
    ];

    public string LineupNote => IsFinal
        ? "Game line first; the season's per-game scoring alongside."
        : $"Today's depth charts, projected minutes, season per-game averages, and scoring over each player's last {RecentGames} team games.";

    public static GameSheet Build(GameSheetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var game = input.Game;
        var isFinal = game.Played && game.HomePoints is not null && game.AwayPoints is not null;
        var awayWon = isFinal && game.AwayPoints > game.HomePoints;

        var status = isFinal
            ? "Final"
            : (game.Day - input.CurrentDay) switch
            {
                <= 0 => "Today",
                1 => "Tomorrow",
                var days => $"In {days} days",
            };

        var meetings = input.Fixtures
            .Where(other => other.GameId != game.GameId && SamePair(other, game))
            .OrderBy(other => other.Day)
            .Select(other => MeetingRow.From(other, input.DateLine(other.Day)))
            .ToList();

        return new GameSheet(
            game.GameId,
            isFinal,
            status,
            $"Day {game.Day} · {input.DateLine(game.Day)}",
            Side(input, game.AwayTeamId, game.AwayTeamName, isFinal ? game.AwayPoints : null, isFinal && awayWon, input.AwayChart, input.Played?.AwayLines, input.Played?.AwayTeam),
            Side(input, game.HomeTeamId, game.HomeTeamName, isFinal ? game.HomePoints : null, isFinal && !awayWon, input.HomeChart, input.Played?.HomeLines, input.Played?.HomeTeam),
            meetings,
            input.Played is { AwayTeam: { } awayTeam, HomeTeam: { } homeTeam } ? FactorRows(awayTeam.Totals, homeTeam.Totals) : []);
    }

    private static IReadOnlyList<FactorRow> FactorRows(CountingStats away, CountingStats home)
    {
        var a = StatFormulas.FourFactorsFor(away, home);
        var h = StatFormulas.FourFactorsFor(home, away);

        // Two sides whose figures print the same are level; otherwise the raw values decide, with
        // turnover % the one factor where lower is better (see FourFactors).
        static FactorRow Row(string label, double? away, double? home, string tooltip, bool lowerIsBetter = false)
        {
            var (shownAway, shownHome) = (StatColumns.Percent(away), StatColumns.Percent(home));
            var edge = shownAway == shownHome || away is not { } x || home is not { } y ? 0 : Math.Sign(lowerIsBetter ? x - y : y - x);
            return new(label, shownAway, shownHome, tooltip, edge);
        }

        return
        [
            Row("Effective FG%", a.EffectiveFieldGoal, h.EffectiveFieldGoal, "Shooting: (FGM + 0.5 × 3PM) / FGA."),
            Row("Turnover %", a.TurnoverPercent, h.TurnoverPercent, "Ball security: TOV / (FGA + 0.44 × FTA + TOV).", lowerIsBetter: true),
            Row("Offensive rebound %", a.OffensiveReboundPercent, h.OffensiveReboundPercent, "Second chances: OREB / (OREB + opponent DREB)."),
            Row("Free throws per FGA", a.FreeThrowsPerFieldGoal, h.FreeThrowsPerFieldGoal, "Getting to the line: FTM per 100 field-goal attempts."),
        ];
    }

    private static GameSheetSide Side(
        GameSheetInput input,
        string teamId,
        string teamName,
        int? points,
        bool isWinner,
        DepthChartSummary? chart,
        IReadOnlyList<BoxScoreLine>? boxScore,
        BoxScoreTeamLine? teamLine)
    {
        var standing = input.Standings.GetValueOrDefault(teamId);
        var art = input.Art.GetValueOrDefault(teamId) ?? TeamArt.For(teamName);

        var teamGames = input.Fixtures
            .Where(fixture => fixture.Played && fixture.Day < input.CurrentDay && (fixture.HomeTeamId == teamId || fixture.AwayTeamId == teamId))
            .OrderBy(fixture => fixture.Day)
            .ToList();
        var form = teamGames
            .TakeLast(RecentGames)
            .Select(fixture =>
            {
                var home = fixture.HomeTeamId == teamId;
                var scored = home ? fixture.HomePoints!.Value : fixture.AwayPoints!.Value;
                var allowed = home ? fixture.AwayPoints!.Value : fixture.HomePoints!.Value;
                var won = scored > allowed;
                var opponent = home ? fixture.AwayTeamName : fixture.HomeTeamName;
                return new FormChip(won ? "W" : "L", won, $"{(won ? "W" : "L")} {scored}–{allowed} {(home ? "vs" : "@")} {opponent} · day {fixture.Day}");
            })
            .ToList();

        var recent = RecentScoring(input.RecentBoxScores.GetValueOrDefault(teamId) ?? [], teamId);
        var depth = DepthLookup(input.AwayChart, input.HomeChart);

        IReadOnlyList<LineupRow> starters;
        IReadOnlyList<LineupRow> bench;
        IReadOnlyList<StatValue> totals = [];
        var teamRebounds = string.Empty;
        string note;

        if (boxScore is { Count: > 0 })
        {
            var rows = boxScore.Select(line => FromBoxScore(line, depth, input.Totals)).ToList();
            starters = rows.Where(row => row.IsStarter).ToList();
            bench = rows.Where(row => !row.IsStarter).ToList();
            totals = teamLine is null ? [] : [.. StatColumns.Cells(teamLine.Totals, StatColumns.BoxScore), SeasonPpgHeader.Cell(string.Empty)];
            teamRebounds = teamLine is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $"Team rebounds: {teamLine.TeamRebounds} ({teamLine.TeamOffensiveRebounds} offensive, {teamLine.TeamDefensiveRebounds} defensive) — boards no player secured, included in the team row so every miss is accounted for.");
            note = string.Empty;
        }
        else if (input.Game.Played)
        {
            starters = [];
            bench = [];
            note = "This result was recorded without player lines.";
        }
        else if (chart is not null)
        {
            var players = chart.Columns
                .SelectMany(column => column.Players.Select(player => (column.Position, Player: player)))
                .Where(entry => entry.Player.Minutes > 0 || entry.Player.IsStarter)
                .ToList();
            starters = players.Where(entry => entry.Player.IsStarter)
                .Select(entry => FromDepthChart(entry.Position, entry.Player, input.Totals, recent))
                .ToList();
            bench = players.Where(entry => !entry.Player.IsStarter)
                .OrderByDescending(entry => entry.Player.Minutes)
                .ThenBy(entry => entry.Player.DepthRank)
                .Select(entry => FromDepthChart(entry.Position, entry.Player, input.Totals, recent))
                .ToList();
            note = string.Empty;
        }
        else
        {
            starters = [];
            bench = [];
            note = "No rotation could be built for this team today.";
        }

        return new GameSheetSide(
            teamId,
            teamName,
            art,
            standing is null ? "—" : $"{standing.Wins}-{standing.Losses}",
            points?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            isWinner,
            form,
            StatColumns.Fixed(standing is null ? null : StatFormulas.PerGame(standing.PointsFor, standing.GamesPlayed)),
            StatColumns.Fixed(standing is null ? null : StatFormulas.PerGame(standing.PointsAgainst, standing.GamesPlayed)),
            StatColumns.Signed(standing is null ? null : StatFormulas.PerGame(standing.PointDifferential, standing.GamesPlayed)),
            starters,
            bench,
            totals,
            teamRebounds,
            boxScore is { Count: > 0 } ? FinalHeaders : PreviewHeaders,
            note);
    }

    private static LineupRow FromDepthChart(
        string position,
        DepthChartLine player,
        IReadOnlyDictionary<string, PlayerSeasonTotals> totals,
        IReadOnlyDictionary<string, CountingStats> recent)
    {
        var season = totals.GetValueOrDefault(player.PlayerId)?.Stats;
        var last = recent.GetValueOrDefault(player.PlayerId);

        return new LineupRow(
            player.PlayerId,
            position,
            player.FullName,
            player.IsStarter,
            player.Overall,
            true,
            [
                ProjectedMinutesHeader.Cell(player.Minutes.ToString(CultureInfo.InvariantCulture)),
                .. StatColumns.Cells(season, PreviewSeasonColumns, StatBasis.PerGame),
                RecentScoringHeader.Cell(StatColumns.Fixed(last is null ? null : StatFormulas.PerGame(last.Points, last.Games))),
            ],
            season is { Games: > 0 } ? $"{season.Games} games played this season" : "No games played this season",
            Rating: RatingView.ForAverage(totals.GetValueOrDefault(player.PlayerId)?.AverageRating));
    }

    private static LineupRow FromBoxScore(
        BoxScoreLine line,
        IReadOnlyDictionary<string, (string Position, int Overall)> depth,
        IReadOnlyDictionary<string, PlayerSeasonTotals> totals)
    {
        var season = totals.GetValueOrDefault(line.PlayerId)?.Stats;
        var known = depth.TryGetValue(line.PlayerId, out var rating);

        return new LineupRow(
            line.PlayerId,
            known ? rating.Position : "—",
            line.FullName,
            line.Started,
            known ? rating.Overall : 0,
            known,
            [
                .. StatColumns.Cells(line.Stats, StatColumns.BoxScore),
                SeasonPpgHeader.Cell(StatColumns.Fixed(season is null ? null : StatFormulas.PerGame(season.Points, season.Games))),
            ],
            known ? string.Empty : "Not on either team's depth chart today, so no current rating or position.",
            line.Stats.FoulOuts > 0,
            RatingView.ForGame(line.Stats));
    }

    /// <summary>Points per appearance over the team's recent box scores, by player.</summary>
    private static IReadOnlyDictionary<string, CountingStats> RecentScoring(IReadOnlyList<BoxScoreSummary> boxScores, string teamId)
    {
        var recent = new Dictionary<string, CountingStats>(StringComparer.Ordinal);

        foreach (var boxScore in boxScores)
        {
            var lines = boxScore.HomeTeamId == teamId ? boxScore.HomeLines : boxScore.AwayLines;
            foreach (var line in lines.Where(line => line.Stats.Minutes > 0))
            {
                recent[line.PlayerId] = recent.GetValueOrDefault(line.PlayerId, CountingStats.None) + line.Stats;
            }
        }

        return recent;
    }

    private static Dictionary<string, (string Position, int Overall)> DepthLookup(params DepthChartSummary?[] charts)
    {
        var lookup = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        foreach (var chart in charts.OfType<DepthChartSummary>())
        {
            foreach (var column in chart.Columns)
            {
                foreach (var player in column.Players)
                {
                    lookup.TryAdd(player.PlayerId, (column.Position, player.Overall));
                }
            }
        }

        return lookup;
    }

    private static bool SamePair(FixtureLine a, FixtureLine b) =>
        (a.HomeTeamId == b.HomeTeamId && a.AwayTeamId == b.AwayTeamId) ||
        (a.HomeTeamId == b.AwayTeamId && a.AwayTeamId == b.HomeTeamId);

}

/// <summary>Everything <see cref="GameSheet.Build"/> reads, gathered by the screen that opens the sheet.</summary>
public sealed record GameSheetInput(
    FixtureLine Game,
    int CurrentDay,
    IReadOnlyList<FixtureLine> Fixtures,
    IReadOnlyDictionary<string, StandingsLine> Standings,
    IReadOnlyDictionary<string, TeamArt> Art,
    IReadOnlyDictionary<string, PlayerSeasonTotals> Totals,
    IReadOnlyDictionary<string, IReadOnlyList<BoxScoreSummary>> RecentBoxScores,
    DepthChartSummary? AwayChart,
    DepthChartSummary? HomeChart,
    BoxScoreSummary? Played,
    Func<int, string> DateLine);

/// <summary>One side of a game sheet.</summary>
public sealed record GameSheetSide(
    string TeamId,
    string TeamName,
    TeamArt Art,
    string Record,
    string Score,
    bool IsWinner,
    IReadOnlyList<FormChip> Form,
    string PointsFor,
    string PointsAgainst,
    string Differential,
    IReadOnlyList<LineupRow> Starters,
    IReadOnlyList<LineupRow> Bench,
    IReadOnlyList<StatValue> Totals,
    string TeamRebounds,
    IReadOnlyList<StatHeader> Headers,
    string LineupNote)
{
    public bool HasLineup => Starters.Count + Bench.Count > 0;

    public bool HasTotals => Totals.Count > 0;

    public bool HasTeamRebounds => TeamRebounds.Length > 0;

    public bool HasForm => Form.Count > 0;

    public IReadOnlyList<Controls.FormResult> FormResults => Form.Select(chip => new Controls.FormResult(chip.IsWin, chip.Tip)).ToList();
}

/// <summary>
/// One player in a game sheet's line-up. <see cref="Stats"/> line up with <see cref="GameSheetSide.Headers"/>:
/// before tip-off projected minutes, season per-game figures and recent scoring; after it the game's
/// own line and season scoring.
/// </summary>
public sealed record LineupRow(
    string PlayerId,
    string Position,
    string Name,
    bool IsStarter,
    int Overall,
    bool HasOverall,
    IReadOnlyList<StatValue> Stats,
    string Tip,
    bool FouledOut = false,
    RatingView? Rating = null);

/// <summary>One of the four factors, both sides' figures, and what it measures.</summary>
/// <remarks><see cref="Edge"/> is which side won the factor: negative away, positive home, zero level or unknown.</remarks>
public sealed record FactorRow(string Label, string Away, string Home, string Tooltip, int Edge)
{
    /// <summary>The side that lost the factor is set dimmed; weight and ink only, never team paint or status colour.</summary>
    public bool AwayTrails => Edge > 0;

    public bool HomeTrails => Edge < 0;

    public string AccessibleName => $"{Label}: away {Away}, home {Home}" + Edge switch
    {
        < 0 => ", away better",
        > 0 => ", home better",
        _ => string.Empty,
    };
}

/// <summary>Another meeting between the same two teams this season.</summary>
public sealed record MeetingRow(string GameId, string DayLine, string Matchup, string Score, bool IsPlayed, string Winner)
{
    public static MeetingRow From(FixtureLine fixture, string date)
    {
        var played = fixture.Played && fixture.HomePoints is not null && fixture.AwayPoints is not null;
        var winner = !played
            ? string.Empty
            : fixture.HomePoints > fixture.AwayPoints ? fixture.HomeTeamName : fixture.AwayTeamName;

        return new MeetingRow(
            fixture.GameId,
            $"Day {fixture.Day} · {date}",
            $"{Converters.TeamInitialsConverter.Initials(fixture.AwayTeamName)} @ {Converters.TeamInitialsConverter.Initials(fixture.HomeTeamName)}",
            played ? $"{fixture.AwayPoints}–{fixture.HomePoints}" : "To play",
            played,
            winner.Length == 0 ? string.Empty : $"{Converters.TeamInitialsConverter.Initials(winner)} won");
    }
}
