using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// One table column. <see cref="Priority"/> 0 never hides; higher numbers hide first when the table is
/// narrow (spec 04 section 7). Frozen columns stay put while the rest scroll sideways.
/// </summary>
/// <summary>One skeleton cell while a table loads: its column's width and the bone drawn in it.</summary>
public sealed record SkeletonCell(double Width, double BoneWidth, bool HasAvatar, bool AlignRight)
{
    public HorizontalAlignment BoneAlignment => AlignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
}

public sealed record TableColumn(
    string Key,
    string Header,
    double Width,
    bool IsNumeric = false,
    bool IsFrozen = false,
    int Priority = 0,
    bool CanSort = true,
    string? ToolTip = null)
{
    public bool AlignRight => IsNumeric;
}

/// <summary>A cell. The subtype decides how it is drawn; every subtype carries plain text for search and sort.</summary>
public abstract record TableCell(string Text);

public sealed record TextCell(string Text, bool IsStrong = false, bool IsNumeric = false) : TableCell(Text);

public sealed record DeltaCell(string Text, bool IsPositive, bool IsNegative) : TableCell(Text);

public sealed record RatingCell(int Value) : TableCell(Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

public sealed record PositionCell(string Text) : TableCell(Text);

public sealed record StatusCell(string Text, StatusKind Kind) : TableCell(Text)
{
    public bool HasStatus => Text.Length > 0;
}

/// <summary>A player name with headshot, as a link to the profile.</summary>
public sealed record PlayerCell(string Text, string PlayerId, bool IsInjured = false, ICommand? Open = null) : TableCell(Text);

/// <summary>A team name with logo, as a link.</summary>
public sealed record TeamCell(string Text, string TeamId, ICommand? Open = null) : TableCell(Text);

/// <summary>A row: its key (the entity ID), its cells in column order, and whether it is the user's own.</summary>
public sealed record TableRow(string Key, IReadOnlyList<TableCell> Cells, bool IsHighlighted = false);

/// <summary>A row as the table draws it: the frozen cells and the scrolling cells for the visible columns.</summary>
public sealed record TableRowView(TableRow Row, IReadOnlyList<TableCellView> Frozen, IReadOnlyList<TableCellView> Scrolling)
{
    public string Key => Row.Key;

    public bool IsHighlighted => Row.IsHighlighted;
}

/// <summary>A cell placed in its column (width and alignment travel with it).</summary>
public sealed record TableCellView(TableCell Cell, double Width, bool AlignRight);

/// <summary>A header cell as drawn: the column, and whether the table is sorted by it.</summary>
public sealed record TableHeaderView(TableColumn Column, bool IsSorted, bool IsDescending)
{
    public string Glyph => !IsSorted ? string.Empty : IsDescending ? "▼" : "▲";

    public double Width => Column.Width;

    public bool AlignRight => Column.AlignRight;
}

/// <summary>Which overlay a data table shows instead of rows.</summary>
public enum TableState
{
    Ready,
    Loading,
    Empty,
    Error,
}

/// <summary>A column the chooser lists, with whether it is shown.</summary>
public sealed class ColumnChoice : global::Avalonia.AvaloniaObject
{
    public static readonly StyledProperty<bool> IsShownProperty =
        AvaloniaProperty.Register<ColumnChoice, bool>(nameof(IsShown), true);

    public ColumnChoice(TableColumn column, bool isShown)
    {
        Column = column;
        IsShown = isShown;
    }

    public TableColumn Column { get; }

    public string Header => Column.Header;

    public bool IsShown
    {
        get => GetValue(IsShownProperty);
        set => SetValue(IsShownProperty, value);
    }
}

/// <summary>
/// The power-user table (spec 03 section 7): sticky header, sort glyphs, a frozen name column with the
/// rest scrolling inside the table's own frame, zebra rows, an accent stripe on the selected row,
/// right-aligned tabular numbers, a column chooser, low-priority columns hidden first when narrow,
/// and empty / loading / error overlays. Rows are virtualized; Enter or a double-click opens one.
/// </summary>
public sealed class DataTable : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<TableColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<DataTable, IReadOnlyList<TableColumn>?>(nameof(Columns));

    public static readonly StyledProperty<IReadOnlyList<TableRow>?> RowsProperty =
        AvaloniaProperty.Register<DataTable, IReadOnlyList<TableRow>?>(nameof(Rows));

    public static readonly StyledProperty<string?> SortKeyProperty =
        AvaloniaProperty.Register<DataTable, string?>(nameof(SortKey));

    public static readonly StyledProperty<bool> SortDescendingProperty =
        AvaloniaProperty.Register<DataTable, bool>(nameof(SortDescending), true);

    public static readonly StyledProperty<ICommand?> SortCommandProperty =
        AvaloniaProperty.Register<DataTable, ICommand?>(nameof(SortCommand));

    public static readonly StyledProperty<ICommand?> OpenRowCommandProperty =
        AvaloniaProperty.Register<DataTable, ICommand?>(nameof(OpenRowCommand));

    public static readonly StyledProperty<TableState> StateProperty =
        AvaloniaProperty.Register<DataTable, TableState>(nameof(State));

    public static readonly StyledProperty<string?> EmptyHeadlineProperty =
        AvaloniaProperty.Register<DataTable, string?>(nameof(EmptyHeadline), "Nothing to show");

    public static readonly StyledProperty<string?> EmptyMessageProperty =
        AvaloniaProperty.Register<DataTable, string?>(nameof(EmptyMessage));

    public static readonly StyledProperty<string?> EmptyActionTextProperty =
        AvaloniaProperty.Register<DataTable, string?>(nameof(EmptyActionText));

    public static readonly StyledProperty<ICommand?> EmptyActionCommandProperty =
        AvaloniaProperty.Register<DataTable, ICommand?>(nameof(EmptyActionCommand));

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<DataTable, string?>(nameof(ErrorMessage));

    public static readonly StyledProperty<ICommand?> RetryCommandProperty =
        AvaloniaProperty.Register<DataTable, ICommand?>(nameof(RetryCommand));

    public static readonly StyledProperty<IReadOnlyCollection<string>?> HiddenColumnsProperty =
        AvaloniaProperty.Register<DataTable, IReadOnlyCollection<string>?>(nameof(HiddenColumns), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<DataTable, IReadOnlyList<TableHeaderView>> FrozenHeadersProperty =
        AvaloniaProperty.RegisterDirect<DataTable, IReadOnlyList<TableHeaderView>>(nameof(FrozenHeaders), table => table.FrozenHeaders);

    public static readonly DirectProperty<DataTable, IReadOnlyList<TableHeaderView>> ScrollingHeadersProperty =
        AvaloniaProperty.RegisterDirect<DataTable, IReadOnlyList<TableHeaderView>>(nameof(ScrollingHeaders), table => table.ScrollingHeaders);

    public static readonly DirectProperty<DataTable, IReadOnlyList<IReadOnlyList<SkeletonCell>>> SkeletonRowsProperty =
        AvaloniaProperty.RegisterDirect<DataTable, IReadOnlyList<IReadOnlyList<SkeletonCell>>>(nameof(SkeletonRows), table => table.SkeletonRows);

    public static readonly DirectProperty<DataTable, IReadOnlyList<TableRowView>> RowViewsProperty =
        AvaloniaProperty.RegisterDirect<DataTable, IReadOnlyList<TableRowView>>(nameof(RowViews), table => table.RowViews);

    public static readonly DirectProperty<DataTable, IReadOnlyList<ColumnChoice>> ColumnChoicesProperty =
        AvaloniaProperty.RegisterDirect<DataTable, IReadOnlyList<ColumnChoice>>(nameof(ColumnChoices), table => table.ColumnChoices);

    public static readonly DirectProperty<DataTable, double> FrozenWidthProperty =
        AvaloniaProperty.RegisterDirect<DataTable, double>(nameof(FrozenWidth), table => table.FrozenWidth);

    public static readonly DirectProperty<DataTable, double> ScrollingWidthProperty =
        AvaloniaProperty.RegisterDirect<DataTable, double>(nameof(ScrollingWidth), table => table.ScrollingWidth);

    public static readonly DirectProperty<DataTable, ITransform?> ScrollTransformProperty =
        AvaloniaProperty.RegisterDirect<DataTable, ITransform?>(nameof(ScrollTransform), table => table.ScrollTransform);

    public static readonly DirectProperty<DataTable, ICommand> HeaderSortCommandProperty =
        AvaloniaProperty.RegisterDirect<DataTable, ICommand>(nameof(HeaderSortCommand), table => table.HeaderSortCommand);

    /// <summary>Below this table width, columns of priority 2 and above are hidden.</summary>
    public const double CompactWidth = 1100;

    private IReadOnlyList<TableHeaderView> _frozenHeaders = [];
    private IReadOnlyList<TableHeaderView> _scrollingHeaders = [];
    private IReadOnlyList<TableRowView> _rowViews = [];
    private IReadOnlyList<ColumnChoice> _columnChoices = [];
    private double _frozenWidth;
    private double _scrollingWidth;
    private ITransform? _scrollTransform;
    private ScrollBar? _horizontal;
    private ListBox? _list;
    private bool _isCompact;

    public DataTable()
    {
        HeaderSortCommand = new SortRelay(this);
    }

    public IReadOnlyList<TableColumn>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public IReadOnlyList<TableRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public string? SortKey
    {
        get => GetValue(SortKeyProperty);
        set => SetValue(SortKeyProperty, value);
    }

    public bool SortDescending
    {
        get => GetValue(SortDescendingProperty);
        set => SetValue(SortDescendingProperty, value);
    }

    /// <summary>Called with a column key when its header is clicked; the owner re-sorts the rows.</summary>
    public ICommand? SortCommand
    {
        get => GetValue(SortCommandProperty);
        set => SetValue(SortCommandProperty, value);
    }

    /// <summary>Called with a row key on Enter or double-click.</summary>
    public ICommand? OpenRowCommand
    {
        get => GetValue(OpenRowCommandProperty);
        set => SetValue(OpenRowCommandProperty, value);
    }

    public TableState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? EmptyHeadline
    {
        get => GetValue(EmptyHeadlineProperty);
        set => SetValue(EmptyHeadlineProperty, value);
    }

    public string? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public string? EmptyActionText
    {
        get => GetValue(EmptyActionTextProperty);
        set => SetValue(EmptyActionTextProperty, value);
    }

    public ICommand? EmptyActionCommand
    {
        get => GetValue(EmptyActionCommandProperty);
        set => SetValue(EmptyActionCommandProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    /// <summary>Column keys the user turned off in the column chooser (persisted by the owner).</summary>
    public IReadOnlyCollection<string>? HiddenColumns
    {
        get => GetValue(HiddenColumnsProperty);
        set => SetValue(HiddenColumnsProperty, value);
    }

    public IReadOnlyList<TableHeaderView> FrozenHeaders
    {
        get => _frozenHeaders;
        private set => SetAndRaise(FrozenHeadersProperty, ref _frozenHeaders, value);
    }

    public IReadOnlyList<TableHeaderView> ScrollingHeaders
    {
        get => _scrollingHeaders;
        private set => SetAndRaise(ScrollingHeadersProperty, ref _scrollingHeaders, value);
    }

    private IReadOnlyList<IReadOnlyList<SkeletonCell>> _skeletonRows = [];
    private int? _pendingSelection;

    /// <summary>Loading placeholders shaped like this table's columns, as many whole rows as fit.</summary>
    public IReadOnlyList<IReadOnlyList<SkeletonCell>> SkeletonRows
    {
        get => _skeletonRows;
        private set => SetAndRaise(SkeletonRowsProperty, ref _skeletonRows, value);
    }

    public IReadOnlyList<TableRowView> RowViews
    {
        get => _rowViews;
        private set => SetAndRaise(RowViewsProperty, ref _rowViews, value);
    }

    public IReadOnlyList<ColumnChoice> ColumnChoices
    {
        get => _columnChoices;
        private set => SetAndRaise(ColumnChoicesProperty, ref _columnChoices, value);
    }

    public double FrozenWidth
    {
        get => _frozenWidth;
        private set => SetAndRaise(FrozenWidthProperty, ref _frozenWidth, value);
    }

    public double ScrollingWidth
    {
        get => _scrollingWidth;
        private set => SetAndRaise(ScrollingWidthProperty, ref _scrollingWidth, value);
    }

    /// <summary>The horizontal shift every scrolling part (header and rows) shares.</summary>
    public ITransform? ScrollTransform
    {
        get => _scrollTransform;
        private set => SetAndRaise(ScrollTransformProperty, ref _scrollTransform, value);
    }

    public ICommand HeaderSortCommand { get; }

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<DataTable, int>(nameof(SelectedIndex), -1, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The selected row's index (-1 for none).</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The list hosting the rows (for tests of virtualization and keyboard).</summary>
    public ListBox? RowList => _list;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (DataTemplates.Count == 0 && this.FindResource("TableCellTemplates") is global::Avalonia.Controls.Templates.DataTemplates cells)
        {
            DataTemplates.AddRange(cells);
        }

        if (_horizontal is not null)
        {
            _horizontal.Scroll -= OnHorizontalScroll;
        }

        _horizontal = e.NameScope.Find<ScrollBar>("PART_HorizontalBar");
        _list = e.NameScope.Find<ListBox>("PART_Rows");
        if (_horizontal is not null)
        {
            _horizontal.Scroll += OnHorizontalScroll;
        }

        if (_list is not null)
        {
            _list.DoubleTapped += (_, _) => OpenSelected();
            _list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        }

        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty || change.Property == HiddenColumnsProperty)
        {
            BuildChoices();
            Rebuild();
        }
        else if (change.Property == RowsProperty || change.Property == SortKeyProperty || change.Property == SortDescendingProperty)
        {
            Rebuild();
        }
        else if (change.Property == BoundsProperty)
        {
            var compact = Bounds.Width > 0 && Bounds.Width < CompactWidth;
            if (compact != _isCompact)
            {
                _isCompact = compact;
                Rebuild();
            }

            UpdateScrollRange();
            BuildSkeleton();
        }
        else if (change.Property == SelectedIndexProperty && RowViews.Count == 0 && change.GetNewValue<int>() >= 0)
        {
            _pendingSelection = change.GetNewValue<int>();
        }
        else if (change.Property == StateProperty)
        {
            foreach (var state in Enum.GetValues<TableState>())
            {
                PseudoClasses.Set(":" + state.ToString().ToLowerInvariant(), state == State);
            }

            UpdateScrollRange();
        }
    }

    private void BuildChoices()
    {
        var hidden = HiddenColumns ?? [];
        var choices = (Columns ?? []).Where(column => !column.IsFrozen).Select(column => new ColumnChoice(column, !hidden.Contains(column.Key))).ToList();
        foreach (var choice in choices)
        {
            choice.PropertyChanged += (_, args) =>
            {
                if (args.Property == ColumnChoice.IsShownProperty)
                {
                    HiddenColumns = ColumnChoices.Where(item => !item.IsShown).Select(item => item.Column.Key).ToList();
                }
            };
        }

        ColumnChoices = choices;
    }

    private void Rebuild()
    {
        var columns = Columns ?? [];
        var hidden = HiddenColumns ?? [];
        var visible = columns
            .Select((column, index) => (Column: column, Index: index))
            .Where(item => !hidden.Contains(item.Column.Key) && !(_isCompact && item.Column.Priority >= 2))
            .ToList();
        var frozen = visible.Where(item => item.Column.IsFrozen).ToList();
        var scrolling = visible.Where(item => !item.Column.IsFrozen).ToList();

        TableHeaderView Header(TableColumn column) => new(column, column.Key == SortKey, SortDescending);
        FrozenHeaders = frozen.Select(item => Header(item.Column)).ToList();
        ScrollingHeaders = scrolling.Select(item => Header(item.Column)).ToList();
        FrozenWidth = frozen.Sum(item => item.Column.Width);
        ScrollingWidth = scrolling.Sum(item => item.Column.Width);

        TableCellView Cell(TableRow row, (TableColumn Column, int Index) item) =>
            new(item.Index < row.Cells.Count ? row.Cells[item.Index] : new TextCell(string.Empty), item.Column.Width, item.Column.AlignRight);
        // New row views reset the list's selection; keep the selected row where it still exists. A selection
        // asked for before there were rows waits for them.
        var selected = _pendingSelection ?? SelectedIndex;
        RowViews = (Rows ?? []).Select(row => new TableRowView(row, frozen.Select(item => Cell(row, item)).ToList(), scrolling.Select(item => Cell(row, item)).ToList())).ToList();
        if (RowViews.Count > 0)
        {
            _pendingSelection = null;
            SelectedIndex = selected < RowViews.Count ? selected : -1;
        }
        else if (selected >= 0)
        {
            _pendingSelection = selected;
        }

        if (State != TableState.Loading && State != TableState.Error)
        {
            State = RowViews.Count == 0 ? TableState.Empty : TableState.Ready;
        }

        UpdateScrollRange();
        BuildSkeleton();
    }

    private const double SkeletonRowHeight = 40;
    private const double HeaderHeight = 36;

    private void BuildSkeleton()
    {
        var hidden = HiddenColumns ?? [];
        var columns = (Columns ?? []).Where(column => !hidden.Contains(column.Key) && !(_isCompact && column.Priority >= 2)).ToList();
        var width = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        var cells = new List<SkeletonCell>();
        var used = 0.0;
        foreach (var column in columns.OrderBy(column => column.IsFrozen ? 0 : 1))
        {
            if (used + column.Width > width)
            {
                break;
            }

            used += column.Width;
            var avatar = cells.Count == 0 && column.IsFrozen;
            var bone = column.IsNumeric ? Math.Min(40, column.Width * 0.5) : column.Width * (avatar ? 0.5 : 0.6);
            cells.Add(new SkeletonCell(column.Width, bone, avatar, column.IsNumeric));
        }

        var rows = Bounds.Height > 0 ? Math.Max(1, (int)((Bounds.Height - HeaderHeight - 8) / SkeletonRowHeight)) : 5;
        SkeletonRows = Enumerable.Repeat<IReadOnlyList<SkeletonCell>>(cells, rows).ToList();
    }

    private void UpdateScrollRange()
    {
        if (_horizontal is null)
        {
            return;
        }

        var viewport = Math.Max(0, Bounds.Width - FrozenWidth - 16);
        var extra = Math.Max(0, ScrollingWidth - viewport);
        _horizontal.Maximum = extra;
        _horizontal.ViewportSize = viewport;
        _horizontal.IsVisible = extra > 0 && State == TableState.Ready;
        if (_horizontal.Value > extra)
        {
            _horizontal.Value = extra;
        }

        ScrollTransform = new TranslateTransform(-_horizontal.Value, 0);
    }

    private void OnHorizontalScroll(object? sender, ScrollEventArgs e) => ScrollTransform = new TranslateTransform(-e.NewValue, 0);

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OpenSelected())
        {
            e.Handled = true;
        }
    }

    private bool OpenSelected()
    {
        if (_list?.SelectedItem is TableRowView row && OpenRowCommand is { } open && open.CanExecute(row.Key))
        {
            open.Execute(row.Key);
            return true;
        }

        return false;
    }

    /// <summary>Header clicks: forward to the owner's sort command.</summary>
    private sealed class SortRelay(DataTable table) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is TableHeaderView { Column.CanSort: true };

        public void Execute(object? parameter)
        {
            if (parameter is TableHeaderView header && table.SortCommand is { } sort && sort.CanExecute(header.Column.Key))
            {
                sort.Execute(header.Column.Key);
            }
        }
    }
}
