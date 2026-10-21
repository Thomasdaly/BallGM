using System.Globalization;
using BallGM.Client.Avalonia.Controls;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One standout line from a final, for the top-performer cards.</summary>
public sealed record TopPerformer(string PlayerId, string Name, string TeamId, string TeamName, string Line, RatingView? Rating);

/// <summary>
/// The game sheet as the T7 template draws it (spec 04 S15 pre-game, S17 box score): a score bug,
/// the tale of the tape, top performers on a final, and each side's line-up as a data table.
/// </summary>
public sealed partial class GameSheet
{
    public string Headline => IsFinal ? "BOX SCORE" : "PRE-GAME";

    public ScoreBugSide BugAway => Bug(Away);

    public ScoreBugSide BugHome => Bug(Home);

    public string BugStatus => IsFinal ? "FINAL" : "PREVIEW";

    /// <summary>The three best scoring lines of the game, either side.</summary>
    public IReadOnlyList<TopPerformer> TopPerformers => !IsFinal
        ? []
        : Away.Starters.Concat(Away.Bench).Select(row => (Row: row, Side: Away))
            .Concat(Home.Starters.Concat(Home.Bench).Select(row => (Row: row, Side: Home)))
            .OrderByDescending(item => Stat(item.Row, "PTS"))
            .ThenByDescending(item => Stat(item.Row, "REB"))
            .Take(3)
            .Select(item => new TopPerformer(
                item.Row.PlayerId,
                item.Row.Name,
                item.Side.TeamId,
                item.Side.TeamName,
                string.Create(CultureInfo.InvariantCulture, $"{Stat(item.Row, "PTS"):0} PTS · {Stat(item.Row, "REB"):0} REB · {Stat(item.Row, "AST"):0} AST"),
                item.Row.Rating))
            .ToList();

    public bool HasTopPerformers => TopPerformers.Count > 0;

    public IReadOnlyList<TableColumn> AwayColumns => Columns(Away);

    public IReadOnlyList<TableRow> AwayRows => Rows(Away);

    public double AwayTableHeight => TableHeight(Away);

    public IReadOnlyList<TableColumn> HomeColumns => Columns(Home);

    public IReadOnlyList<TableRow> HomeRows => Rows(Home);

    public double HomeTableHeight => TableHeight(Home);

    private static ScoreBugSide Bug(GameSheetSide side) =>
        new(side.TeamId, side.Art.Initials, side.TeamName, int.TryParse(side.Score, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : null, side.IsWinner);

    private static double Stat(LineupRow row, string header) =>
        row.Stats.FirstOrDefault(stat => stat.Header == header) is { } value && double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static IReadOnlyList<TableColumn> Columns(GameSheetSide side) =>
    [
        new("pos", "POS", 64, IsFrozen: true, CanSort: false),
        new("player", "PLAYER", 240, IsFrozen: true, CanSort: false),
        new("ovr", "OVR", 64, CanSort: false, ToolTip: "Overall: current ability"),
        new("rtg", "RTG", 64, IsNumeric: true, CanSort: false, ToolTip: "Match rating out of ten: this game's on a final, the season's average on a preview."),
        .. side.Headers.Select((header, index) => new TableColumn("s" + index.ToString(CultureInfo.InvariantCulture), header.Header, Math.Max(56, header.Width), IsNumeric: true, CanSort: false, ToolTip: header.Tooltip)),
    ];

    private static IReadOnlyList<TableRow> Rows(GameSheetSide side)
    {
        var rows = side.Starters.Concat(side.Bench)
            .Select(row => new TableRow(
                row.PlayerId,
                [
                    new TextCell(row.Position, IsStrong: row.IsStarter),
                    new PlayerCell(row.FouledOut ? $"{row.Name} (fouled out)" : row.Name, row.PlayerId, false, null),
                    row.HasOverall ? new RatingCell(row.Overall) : new TextCell(StatColumns.Missing),
                    new TextCell(row.Rating?.Text ?? StatColumns.Missing, IsNumeric: true),
                    .. row.Stats.Select(stat => (TableCell)new TextCell(stat.Value, IsNumeric: true)),
                ]))
            .ToList();
        if (side.HasTotals)
        {
            rows.Add(new TableRow(
                side.TeamId + "-totals",
                [new TextCell(string.Empty), new TextCell("Team totals", IsStrong: true), new TextCell(string.Empty), new TextCell(string.Empty), .. side.Totals.Select(stat => (TableCell)new TextCell(stat.Value, IsStrong: true, IsNumeric: true))]));
        }

        return rows;
    }

    private static double TableHeight(GameSheetSide side) => 36 + 8 + (40 * Math.Max(3, side.Starters.Count + side.Bench.Count + (side.HasTotals ? 1 : 0)));
}
