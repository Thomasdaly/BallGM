using System.Globalization;
using System.Windows.Input;
using BallGM.Client.Avalonia.Controls;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>A toggle chip in a filter bar (a position, a status).</summary>
public sealed class ChipFilter(string label, Action changed) : ViewModelBase
{
    private bool _isChecked;

    public string Label { get; } = label;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
            {
                changed();
            }
        }
    }

    internal void Reset() => SetProperty(ref _isChecked, false, nameof(IsChecked));
}

/// <summary>
/// The roster as the T2 data table draws it (spec 04 S07): Overview and Stats tabs, a filter bar
/// (name, positions, overall range), and sortable columns that reuse the screen's own sort order.
/// </summary>
public sealed partial class RosterViewModel
{
    private static readonly string[] PositionCodes = ["PG", "SG", "SF", "PF", "C"];

    private Action<string>? _openPlayer;
    private string _searchText = string.Empty;
    private decimal? _overallLow = 0;
    private decimal? _overallHigh = 100;
    private int _tabIndex;
    private IReadOnlyList<ChipFilter>? _positionFilters;

    /// <summary>Opens profiles as shell pages (with Back) instead of over the list.</summary>
    public void UseShellProfiles(Action<string> openPlayer) => _openPlayer = openPlayer;

    public IReadOnlyList<string> Tabs => HasSeasonStats ? ["Overview", "Stats"] : ["Overview"];

    public int TabIndex
    {
        get => _tabIndex;
        set
        {
            if (SetProperty(ref _tabIndex, Math.Max(0, value)))
            {
                RaiseTable();
            }
        }
    }

    public bool IsStatsTab => _tabIndex == 1 && HasSeasonStats;

    public IReadOnlyList<ChipFilter> PositionFilters => _positionFilters ??= PositionCodes.Select(code => new ChipFilter(code, RaiseTable)).ToList();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RaiseTable();
            }
        }
    }

    public decimal? OverallLow
    {
        get => _overallLow;
        set
        {
            if (SetProperty(ref _overallLow, value))
            {
                RaiseTable();
            }
        }
    }

    public decimal? OverallHigh
    {
        get => _overallHigh;
        set
        {
            if (SetProperty(ref _overallHigh, value))
            {
                RaiseTable();
            }
        }
    }

    public int ActiveFilterCount =>
        (_searchText.Trim().Length > 0 ? 1 : 0)
        + (PositionFilters.Any(chip => chip.IsChecked) ? 1 : 0)
        + (_overallLow is > 0 || _overallHigh is < 100 ? 1 : 0);

    public ICommand ResetFiltersCommand => new RelayCommand(() =>
    {
        _searchText = string.Empty;
        _overallLow = 0;
        _overallHigh = 100;
        foreach (var chip in PositionFilters)
        {
            chip.Reset();
        }

        RaisePropertyChanged(nameof(SearchText));
        RaisePropertyChanged(nameof(OverallLow));
        RaisePropertyChanged(nameof(OverallHigh));
        RaiseTable();
    });

    /// <summary>0 per game, 1 per 36, 2 totals - the segmented control's index.</summary>
    public int BasisIndex
    {
        get => Basis.Basis switch
        {
            StatBasis.Per36 => 1,
            StatBasis.Totals => 2,
            _ => 0,
        };
        set
        {
            Basis.Basis = value switch
            {
                1 => StatBasis.Per36,
                2 => StatBasis.Totals,
                _ => StatBasis.PerGame,
            };
            RaiseTable();
        }
    }

    public IReadOnlyList<string> BasisOptions { get; } = ["Per game", "Per 36", "Totals"];

    public IReadOnlyList<TableColumn> TableColumns => IsStatsTab
        ? [new("player", "PLAYER", 280, IsFrozen: true), new("rtg", "RTG", 64, IsNumeric: true, ToolTip: StatColumns.RatingTooltip),
            .. StatHeaders.Select(header => new TableColumn("stat" + header.Index.ToString(CultureInfo.InvariantCulture), header.Header.Header, Math.Max(56, header.Header.Width), IsNumeric: true, Priority: header.Index > 8 ? 2 : 0, ToolTip: header.Header.Tooltip))]
        :
        [
            new("player", "PLAYER", 280, IsFrozen: true),
            new("pos", "POS", 72),
            new("ovr", "OVR", 64, IsNumeric: true, ToolTip: "Overall: current ability"),
            new("pot", "POT", 120, ToolTip: "Potential, as the scouts rate it"),
            new("salary", "SALARY", 104, IsNumeric: true),
            new("years", "YEARS", 80, IsNumeric: true),
            new("status", "STATUS", 280, Priority: 1),
        ];

    public IReadOnlyList<TableRow> TableRows
    {
        get
        {
            var open = new ParameterCommand<string>(ShowProfile);
            var positions = PositionFilters.Where(chip => chip.IsChecked).Select(chip => chip.Label).ToHashSet(StringComparer.Ordinal);
            var text = _searchText.Trim();
            return Rows
                .Where(row => text.Length == 0 || row.FullName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Where(row => positions.Count == 0 || positions.Contains(row.Position))
                .Where(row => row.Overall >= (int)(_overallLow ?? 0) && row.Overall <= (int)(_overallHigh ?? 100))
                .Select(row => new TableRow(row.PlayerId, IsStatsTab ? StatsCells(row, open) : OverviewCells(row, open)))
                .ToList();
        }
    }

    public string TableSortKey => _sortColumn switch
    {
        RosterSortColumn.Name => "player",
        RosterSortColumn.Position => "pos",
        RosterSortColumn.Salary => "salary",
        RosterSortColumn.Years => "years",
        RosterSortColumn.Rating => "rtg",
        RosterSortColumn.Potential => "pot",
        RosterSortColumn.Stat => "stat" + _sortStat.ToString(CultureInfo.InvariantCulture),
        _ => "ovr",
    };

    public bool TableSortDescending => _sortDescending;

    public ICommand TableSortCommand => new ParameterCommand<string>(key =>
    {
        switch (key)
        {
            case "player": SortBy(RosterSortColumn.Name, 0); break;
            case "pos": SortBy(RosterSortColumn.Position, 0); break;
            case "salary": SortBy(RosterSortColumn.Salary, 0); break;
            case "years": SortBy(RosterSortColumn.Years, 0); break;
            case "rtg": SortBy(RosterSortColumn.Rating, 0); break;
            case "pot": SortBy(RosterSortColumn.Potential, 0); break;
            case "ovr": SortBy(RosterSortColumn.Overall, 0); break;
            default:
                if (key.StartsWith("stat", StringComparison.Ordinal) && int.TryParse(key[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                {
                    SortBy(RosterSortColumn.Stat, index);
                }

                break;
        }

        RaiseTable();
    });

    public ICommand OpenRowCommand => new ParameterCommand<string>(ShowProfile);

    private static IReadOnlyList<TableCell> OverviewCells(RosterRow row, ICommand open) =>
    [
        new PlayerCell(row.FullName, row.PlayerId, row.IsInjured, open),
        new PositionCell(row.Position),
        new RatingCell(row.Overall),
        new TextCell(row.Potential),
        new TextCell(row.Salary, IsNumeric: true),
        new TextCell(row.Years, IsNumeric: true),
        new StatusCell(row.IsInjured ? row.InjuryDescription ?? "Injured" : string.Empty, StatusKind.Negative),
    ];

    private static IReadOnlyList<TableCell> StatsCells(RosterRow row, ICommand open) =>
    [
        new PlayerCell(row.FullName, row.PlayerId, row.IsInjured, open),
        new TextCell(row.Rating?.Text ?? StatColumns.Missing, IsNumeric: true),
        .. row.Stats.Select(stat => (TableCell)new TextCell(stat.Value, IsNumeric: true)),
    ];

    private void RaiseTable()
    {
        foreach (var name in new[]
        {
            nameof(TableColumns), nameof(TableRows), nameof(TableSortKey), nameof(TableSortDescending), nameof(ActiveFilterCount),
            nameof(IsStatsTab), nameof(Tabs), nameof(BasisIndex),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
