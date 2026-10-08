using System.Globalization;
using BallGM.Application.Seasons;

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
public sealed class GameSheet
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
        IReadOnlyList<MeetingRow> meetings)
    {
        GameId = gameId;
        IsFinal = isFinal;
        Status = status;
        DayLine = dayLine;
        Away = away;
        Home = home;
        Meetings = meetings;
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

    public string LineupHeading => IsFinal ? "Box score" : "Projected rotations";

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
            Side(input, game.AwayTeamId, game.AwayTeamName, isFinal ? game.AwayPoints : null, isFinal && awayWon, input.AwayChart, input.Played?.AwayLines),
            Side(input, game.HomeTeamId, game.HomeTeamName, isFinal ? game.HomePoints : null, isFinal && !awayWon, input.HomeChart, input.Played?.HomeLines),
            meetings);
    }

    private static GameSheetSide Side(
        GameSheetInput input,
        string teamId,
        string teamName,
        int? points,
        bool isWinner,
        DepthChartSummary? chart,
        IReadOnlyList<BoxScoreLine>? boxScore)
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
        string note;

        if (boxScore is { Count: > 0 })
        {
            var rows = boxScore.Select(line => FromBoxScore(line, depth, input.Totals)).ToList();
            starters = rows.Where(row => row.IsStarter).ToList();
            bench = rows.Where(row => !row.IsStarter).ToList();
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
            standing is { GamesPlayed: > 0 } ? PerGame(standing.PointsFor, standing.GamesPlayed) : "—",
            standing is { GamesPlayed: > 0 } ? PerGame(standing.PointsAgainst, standing.GamesPlayed) : "—",
            standing is { GamesPlayed: > 0 }
                ? ((double)standing.PointDifferential / standing.GamesPlayed).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)
                : "—",
            starters,
            bench,
            note);
    }

    private static LineupRow FromDepthChart(
        string position,
        DepthChartLine player,
        IReadOnlyDictionary<string, PlayerSeasonTotals> totals,
        IReadOnlyDictionary<string, (int Games, int Points)> recent)
    {
        var season = totals.GetValueOrDefault(player.PlayerId);
        var last = recent.GetValueOrDefault(player.PlayerId);

        return new LineupRow(
            player.PlayerId,
            position,
            player.FullName,
            player.IsStarter,
            player.Overall,
            true,
            player.Minutes.ToString(CultureInfo.InvariantCulture),
            Average(season?.Points, season?.GamesPlayed),
            Average(season?.Rebounds, season?.GamesPlayed),
            Average(season?.Assists, season?.GamesPlayed),
            last.Games == 0 ? "—" : PerGame(last.Points, last.Games),
            season is { GamesPlayed: > 0 } ? $"{season.GamesPlayed} games played this season" : "No games played this season");
    }

    private static LineupRow FromBoxScore(
        BoxScoreLine line,
        IReadOnlyDictionary<string, (string Position, int Overall)> depth,
        IReadOnlyDictionary<string, PlayerSeasonTotals> totals)
    {
        var season = totals.GetValueOrDefault(line.PlayerId);
        var known = depth.TryGetValue(line.PlayerId, out var rating);

        return new LineupRow(
            line.PlayerId,
            known ? rating.Position : "—",
            line.FullName,
            line.Started,
            known ? rating.Overall : 0,
            known,
            line.Minutes.ToString(CultureInfo.InvariantCulture),
            line.Points.ToString(CultureInfo.InvariantCulture),
            line.Rebounds.ToString(CultureInfo.InvariantCulture),
            line.Assists.ToString(CultureInfo.InvariantCulture),
            Average(season?.Points, season?.GamesPlayed),
            known ? string.Empty : "Not on either team's depth chart today, so no current rating or position.");
    }

    /// <summary>Points per appearance over the team's recent box scores, by player.</summary>
    private static IReadOnlyDictionary<string, (int Games, int Points)> RecentScoring(IReadOnlyList<BoxScoreSummary> boxScores, string teamId)
    {
        var scoring = new Dictionary<string, (int Games, int Points)>(StringComparer.Ordinal);

        foreach (var boxScore in boxScores)
        {
            var lines = boxScore.HomeTeamId == teamId ? boxScore.HomeLines : boxScore.AwayLines;
            foreach (var line in lines.Where(line => line.Minutes > 0))
            {
                var current = scoring.GetValueOrDefault(line.PlayerId);
                scoring[line.PlayerId] = (current.Games + 1, current.Points + line.Points);
            }
        }

        return scoring;
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

    private static string Average(int? total, int? games) =>
        total is { } sum && games is > 0 ? PerGame(sum, games.Value) : "—";

    private static string PerGame(int total, int games) =>
        ((double)total / games).ToString("0.0", CultureInfo.InvariantCulture);
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
    string LineupNote)
{
    public bool HasLineup => Starters.Count + Bench.Count > 0;

    public bool HasForm => Form.Count > 0;
}

/// <summary>
/// One player in a game sheet's line-up. Before tip-off the four figures are projected minutes and
/// season points, rebounds and assists per game, and <see cref="Extra"/> is recent scoring; after it
/// they are the game's own line and <see cref="Extra"/> is season scoring.
/// </summary>
public sealed record LineupRow(
    string PlayerId,
    string Position,
    string Name,
    bool IsStarter,
    int Overall,
    bool HasOverall,
    string Minutes,
    string Points,
    string Rebounds,
    string Assists,
    string Extra,
    string Tip);

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
