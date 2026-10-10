using System.Globalization;
using BallGM.Application.Players;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One column of a stat table: the same header, tooltip, width, formatting and sort order on every
/// screen that shows it. <see cref="ShownBasis"/> is the column's whole basis policy — which basis it
/// actually shows for the one a table asks for, or null for a column that ignores basis (games played,
/// a percentage). <see cref="SortKey"/> is null where there is nothing to rank (no attempts, no games).
/// </summary>
public sealed record StatColumn(
    string Header,
    string Tooltip,
    double Width,
    Func<StatBasis, StatBasis?> ShownBasis,
    Func<CountingStats, StatBasis, string> Format,
    Func<CountingStats, StatBasis, double?> SortKey)
{
    /// <summary>This column's header, its tooltip saying which basis the figures are on.</summary>
    public StatHeader HeaderFor(StatBasis basis) => new(Header, Tooltip + (ShownBasis(basis) switch
    {
        StatBasis.PerGame => " Per game.",
        StatBasis.Per36 => " Per 36 minutes.",
        _ => string.Empty,
    }), Width);
}

/// <summary>A column header as a table draws it.</summary>
public sealed record StatHeader(string Header, string Tooltip, double Width)
{
    /// <summary>A figure for a column that is not in the catalogue (a projection, a recent-games average), at this header's width.</summary>
    public StatValue Cell(string value) => new(value, Tooltip, Width, Header);
}

/// <summary>
/// One figure in a row, already formatted, at its column's width. <see cref="AccessibleName"/> pairs
/// it with its header, because a screen reader reading a row cell by cell would otherwise say "25"
/// with nothing to say it is points.
/// </summary>
public sealed record StatValue(string Value, string Tooltip, double Width, string Header = "")
{
    public string AccessibleName => Header.Length == 0 ? Value : $"{Header} {Value}";
}

/// <summary>
/// Every stat column the client shows, defined once. A box score, a roster, a game sheet and a profile
/// all take their columns from here, so "REB" means and looks the same everywhere and a stat added to
/// <see cref="CountingStats"/> is added to every surface by adding it to a column set. Formatting
/// only: every rate is computed by <see cref="StatFormulas"/>, never here.
/// </summary>
public static class StatColumns
{
    /// <summary>The placeholder every stat surface shows for a figure that does not exist yet.</summary>
    public const string Missing = "—";

    private const double Narrow = 38;
    private const double Wide = 46;
    private const double MadeAttempted = 48;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static readonly StatColumn Games = new(
        "GP", "Games played.", Narrow, _ => null, (s, _) => s.Games.ToString(Invariant), (s, _) => s.Games);

    /// <summary>Minutes stay per game on a per-36 view: scaled to 36 they would read 36.0 for everyone.</summary>
    public static readonly StatColumn Minutes = Counting(
        "MIN", "Minutes played", stats => stats.Minutes, shown: basis => basis == StatBasis.Per36 ? StatBasis.PerGame : basis);

    public static readonly StatColumn Points = Counting("PTS", "Points", stats => stats.Points);
    public static readonly StatColumn OffensiveRebounds = Counting("OREB", "Offensive rebounds", stats => stats.OffensiveRebounds, Wide);
    public static readonly StatColumn DefensiveRebounds = Counting("DREB", "Defensive rebounds", stats => stats.DefensiveRebounds, Wide);
    public static readonly StatColumn Rebounds = Counting("REB", "Rebounds, offensive and defensive", stats => stats.Rebounds);
    public static readonly StatColumn Assists = Counting("AST", "Assists: passes leading directly to a teammate's basket", stats => stats.Assists);

    public static readonly StatColumn Steals = Counting("STL", "Steals: live-ball turnovers forced", stats => stats.Steals);
    public static readonly StatColumn Turnovers = Counting("TOV", "Turnovers: possessions lost without a shot", stats => stats.Turnovers);
    public static readonly StatColumn Blocks = Counting("BLK", "Blocks: opponent shots blocked", stats => stats.Blocks);
    public static readonly StatColumn PersonalFouls = Counting("PF", "Personal fouls", stats => stats.PersonalFouls);

    /// <summary>A count of games, so always a total: per game it would read as a fraction of a game.</summary>
    public static readonly StatColumn FoulOuts = Counting(
        "FOULED OUT", "Games fouled out: reached the foul limit and took no further part", stats => stats.FoulOuts, Wide, shown: _ => StatBasis.Totals);

    /// <summary>Signed: the score difference while this player was on the floor.</summary>
    public static readonly StatColumn PlusMinus = Counting(
        "+/-", "Plus-minus: points scored minus points allowed while this player was on the floor", stats => stats.PlusMinus, Wide, signed: true);

    public static readonly StatColumn FieldGoals = MadeOf("FG", "Field goals made-attempted", stats => (stats.FieldGoalsMade, stats.FieldGoalsAttempted));
    public static readonly StatColumn Threes = MadeOf("3P", "Three-pointers made-attempted", stats => (stats.ThreePointsMade, stats.ThreePointsAttempted));
    public static readonly StatColumn FreeThrows = MadeOf("FT", "Free throws made-attempted", stats => (stats.FreeThrowsMade, stats.FreeThrowsAttempted));

    public static readonly StatColumn FieldGoalPercent = Percentage("FG%", "Field-goal percentage: FGM / FGA.", StatFormulas.FieldGoalPercent);
    public static readonly StatColumn ThreePointPercent = Percentage("3P%", "Three-point percentage: 3PM / 3PA.", StatFormulas.ThreePointPercent);
    public static readonly StatColumn FreeThrowPercent = Percentage("FT%", "Free-throw percentage: FTM / FTA.", StatFormulas.FreeThrowPercent);
    public static readonly StatColumn TrueShooting = Percentage("TS%", "True shooting: PTS / (2 × (FGA + 0.44 × FTA)), efficiency counting threes and free throws.", StatFormulas.TrueShooting);

    /// <summary>One game's line, as a box score shows it.</summary>
    public static IReadOnlyList<StatColumn> BoxScore { get; } =
        [Minutes, Points, FieldGoals, Threes, FreeThrows, OffensiveRebounds, DefensiveRebounds, Rebounds, Assists, Steals, Blocks, Turnovers, PersonalFouls, PlusMinus];

    /// <summary>A run of games, as a roster or a preview shows it: averages and shooting rates.</summary>
    public static IReadOnlyList<StatColumn> Season { get; } =
        [Games, Minutes, Points, Rebounds, Assists, Steals, Blocks, Turnovers, PersonalFouls, PlusMinus, FieldGoalPercent, ThreePointPercent, FreeThrowPercent, TrueShooting];

    /// <summary>A header for a stated rate the catalogue does not compute (a data pack's NETRTG, PIE), at a rate column's width.</summary>
    public static StatHeader RateHeader(string header, string tooltip) => new(header, tooltip, Wide);

    public static IReadOnlyList<StatHeader> Headers(IEnumerable<StatColumn> columns, StatBasis basis = StatBasis.Totals) =>
        columns.Select(column => column.HeaderFor(basis)).ToList();

    /// <summary>A row's cells; a player with no games yet shows a dash in every column, not zeroes.</summary>
    public static IReadOnlyList<StatValue> Cells(CountingStats? stats, IEnumerable<StatColumn> columns, StatBasis basis = StatBasis.Totals) =>
        columns.Select(column => new StatValue(stats is null ? Missing : column.Format(stats, basis), column.Tooltip, column.Width, column.Header)).ToList();

    /// <summary>The one way a derived figure is printed: one decimal, or a dash when there is nothing to divide by.</summary>
    public static string Fixed(double? value) => value is { } number ? number.ToString("0.0", Invariant) : Missing;

    /// <summary>The one way a signed figure is printed: an explicit sign, one decimal, or a dash.</summary>
    public static string Signed(double? value) => value is { } number ? number.ToString("+0.0;-0.0;0.0", Invariant) : Missing;

    /// <summary>The one way a rate is printed as a percentage: one decimal, or a dash when there were no attempts.</summary>
    public static string Percent(double? rate) => rate is { } value ? (value * 100).ToString("0.0", Invariant) : Missing;

    private static StatColumn Counting(
        string header,
        string tooltip,
        Func<CountingStats, int> total,
        double width = Narrow,
        Func<StatBasis, StatBasis>? shown = null,
        bool signed = false)
    {
        StatBasis Shown(StatBasis basis) => shown?.Invoke(basis) ?? basis;
        double? Value(CountingStats stats, StatBasis basis) => StatFormulas.Scaled(total(stats), stats, Shown(basis));
        string Format(CountingStats stats, StatBasis basis) => (Shown(basis), signed) switch
        {
            (StatBasis.Totals, false) => total(stats).ToString(Invariant),
            (StatBasis.Totals, true) => total(stats).ToString("+0;-0;0", Invariant),
            (_, false) => Fixed(Value(stats, basis)),
            _ => Signed(Value(stats, basis)),
        };

        return new(header, tooltip + ".", width, basis => Shown(basis), Format, Value);
    }

    private static StatColumn MadeOf(string header, string tooltip, Func<CountingStats, (int Made, int Attempted)> pick) =>
        new(
            header,
            tooltip + ".",
            MadeAttempted,
            basis => basis,
            (stats, basis) =>
            {
                var (made, attempted) = pick(stats);
                return basis == StatBasis.Totals
                    ? string.Create(Invariant, $"{made}-{attempted}")
                    : $"{Fixed(StatFormulas.Scaled(made, stats, basis))}-{Fixed(StatFormulas.Scaled(attempted, stats, basis))}";
            },
            (stats, basis) => StatFormulas.Scaled(pick(stats).Made, stats, basis));

    private static StatColumn Percentage(string header, string tooltip, Func<CountingStats, double?> rate) =>
        new(header, tooltip, Wide, _ => null, (stats, _) => Percent(rate(stats)), (stats, _) => rate(stats));
}
