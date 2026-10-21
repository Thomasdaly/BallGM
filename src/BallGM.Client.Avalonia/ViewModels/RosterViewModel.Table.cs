using System.Globalization;
using System.Windows.Input;
using BallGM.Application.Players;
using BallGM.Client.Avalonia.Controls;

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
    private decimal? _overallLow = RatingFloor;
    private decimal? _overallHigh = RatingCeiling;
    private decimal? _ageLow = AgeFloor;
    private decimal? _ageHigh = AgeCeiling;
    private int _layoutIndex;
    private IReadOnlyList<ChipFilter>? _statusFilters;

    /// <summary>The rating scale's ends, so the range filter starts wide open.</summary>
    public const int RatingFloor = 0;

    public const int RatingCeiling = 100;

    public const int AgeFloor = 16;

    public const int AgeCeiling = 45;
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

    public decimal? AgeLow
    {
        get => _ageLow;
        set
        {
            if (SetProperty(ref _ageLow, value))
            {
                RaiseTable();
            }
        }
    }

    public decimal? AgeHigh
    {
        get => _ageHigh;
        set
        {
            if (SetProperty(ref _ageHigh, value))
            {
                RaiseTable();
            }
        }
    }

    /// <summary>Healthy / Injured: the statuses the game records.</summary>
    public IReadOnlyList<ChipFilter> StatusFilters => _statusFilters ??= [new ChipFilter("Healthy", RaiseTable), new ChipFilter("Injured", RaiseTable)];

    public IReadOnlyList<string> LayoutOptions { get; } = ["Table", "Cards"];

    /// <summary>0 the data table, 1 a grid of player cards.</summary>
    public int LayoutIndex
    {
        get => _layoutIndex;
        set
        {
            if (SetProperty(ref _layoutIndex, value))
            {
                RaisePropertyChanged(nameof(IsTableLayout));
                RaisePropertyChanged(nameof(IsCardLayout));
            }
        }
    }

    public bool IsTableLayout => _layoutIndex == 0;

    public bool IsCardLayout => _layoutIndex == 1;

    public IReadOnlyList<PlayerCardModel> Cards => FilteredRows()
        .Select(row => new PlayerCardModel(
            row.PlayerId, row.FullName, null, null, row.Position, row.Age, row.Overall, 0,
            $"{row.Salary} · {row.Years}",
            row.IsInjured ? [new StatusItem(row.InjuryDescription ?? "Injured", StatusKind.Negative)] : [new StatusItem("Healthy", StatusKind.Positive)],
            row.IsInjured)
        {
            PotentialStars = row.Potential,
        })
        .ToList();

    public ICommand OpenCardCommand => new ParameterCommand<string>(ShowProfile);

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
        + (_overallLow is > RatingFloor || _overallHigh is < RatingCeiling ? 1 : 0)
        + (_ageLow is > AgeFloor || _ageHigh is < AgeCeiling ? 1 : 0)
        + (StatusFilters.Any(chip => chip.IsChecked) ? 1 : 0);

    public ICommand ResetFiltersCommand => new RelayCommand(() =>
    {
        _searchText = string.Empty;
        _overallLow = RatingFloor;
        _overallHigh = RatingCeiling;
        _ageLow = AgeFloor;
        _ageHigh = AgeCeiling;
        foreach (var chip in PositionFilters.Concat(StatusFilters))
        {
            chip.Reset();
        }

        RaisePropertyChanged(nameof(SearchText));
        RaisePropertyChanged(nameof(OverallLow));
        RaisePropertyChanged(nameof(OverallHigh));
        RaisePropertyChanged(nameof(AgeLow));
        RaisePropertyChanged(nameof(AgeHigh));
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
            new("age", "AGE", 64, IsNumeric: true),
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
            return FilteredRows()
                .Select(row => new TableRow(row.PlayerId, IsStatsTab ? StatsCells(row, open) : OverviewCells(row, open)))
                .ToList();
        }
    }

    private IEnumerable<RosterRow> FilteredRows()
    {
        var positions = PositionFilters.Where(chip => chip.IsChecked).Select(chip => chip.Label).ToHashSet(StringComparer.Ordinal);
        var statuses = StatusFilters.Where(chip => chip.IsChecked).Select(chip => chip.Label == "Injured").ToHashSet();
        var text = _searchText.Trim();
        return Rows
            .Where(row => text.Length == 0 || row.FullName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Where(row => positions.Count == 0 || positions.Contains(row.Position))
            .Where(row => statuses.Count == 0 || statuses.Contains(row.IsInjured))
            .Where(row => row.Overall >= (int)(_overallLow ?? RatingFloor) && row.Overall <= (int)(_overallHigh ?? RatingCeiling))
            .Where(row => row.Age >= (int)(_ageLow ?? AgeFloor) && row.Age <= (int)(_ageHigh ?? AgeCeiling));
    }

    public string TableSortKey => _sortColumn switch
    {
        RosterSortColumn.Name => "player",
        RosterSortColumn.Position => "pos",
        RosterSortColumn.Salary => "salary",
        RosterSortColumn.Years => "years",
        RosterSortColumn.Rating => "rtg",
        RosterSortColumn.Potential => "pot",
        RosterSortColumn.Age => "age",
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
            case "age": SortBy(RosterSortColumn.Age, 0); break;
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
        new TextCell(row.Age.ToString(CultureInfo.InvariantCulture), IsNumeric: true),
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
            nameof(IsStatsTab), nameof(Tabs), nameof(BasisIndex), nameof(Cards),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
