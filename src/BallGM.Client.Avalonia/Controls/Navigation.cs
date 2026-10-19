using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// A page's header (spec 03 section 5): breadcrumb, H1 caps title, optional subtitle, and right-aligned
/// actions (one primary at most - the caller's responsibility, checked in visual QA).
/// </summary>
public sealed class PageHeader : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> BreadcrumbProperty =
        AvaloniaProperty.Register<PageHeader, object?>(nameof(Breadcrumb));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Breadcrumb
    {
        get => GetValue(BreadcrumbProperty);
        set => SetValue(BreadcrumbProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}

/// <summary>One crumb: its label and, except for the current page, where it goes.</summary>
public sealed record BreadcrumbItem(string Label, ICommand? Command = null, object? Parameter = null)
{
    public bool IsLink => Command is not null;

    /// <summary>The first crumb has no separator before it.</summary>
    public bool IsFirst { get; init; }
}

/// <summary>A breadcrumb trail that collapses its middle crumbs to "…" when there is no room.</summary>
public sealed class Breadcrumb : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<BreadcrumbItem>?> ItemsProperty =
        AvaloniaProperty.Register<Breadcrumb, IReadOnlyList<BreadcrumbItem>?>(nameof(Items));

    public static readonly DirectProperty<Breadcrumb, IReadOnlyList<BreadcrumbItem>> DisplayItemsProperty =
        AvaloniaProperty.RegisterDirect<Breadcrumb, IReadOnlyList<BreadcrumbItem>>(nameof(DisplayItems), crumb => crumb.DisplayItems);

    /// <summary>Below this width only the first and last crumbs are shown.</summary>
    public const double CollapseBelow = 420;

    private IReadOnlyList<BreadcrumbItem> _displayItems = [];

    public IReadOnlyList<BreadcrumbItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IReadOnlyList<BreadcrumbItem> DisplayItems
    {
        get => _displayItems;
        private set => SetAndRaise(DisplayItemsProperty, ref _displayItems, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty || change.Property == BoundsProperty)
        {
            Recompute();
        }
    }

    private void Recompute()
    {
        var items = Items ?? [];
        IReadOnlyList<BreadcrumbItem> shown = items.Count > 2 && Bounds.Width > 0 && Bounds.Width < CollapseBelow
            ? [items[0], new BreadcrumbItem("…"), items[^1]]
            : items;
        DisplayItems = shown.Select((item, index) => item with { IsFirst = index == 0 }).ToList();
    }
}

/// <summary>A wizard step and where the user is relative to it.</summary>
public enum StepState
{
    Upcoming,
    Current,
    Done,

    /// <summary>A step whose input fails validation.</summary>
    Invalid,
}

public sealed record StepItem(int Number, string Title, StepState State)
{
    public bool IsDone => State == StepState.Done;

    public bool IsCurrent => State == StepState.Current;

    public bool IsInvalid => State == StepState.Invalid;

    public bool IsFirst => Number == 1;
}

/// <summary>Wizard progress: numbered steps, done / current / upcoming (spec 03 section 5).</summary>
public sealed class Stepper : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<StepItem>?> StepsProperty =
        AvaloniaProperty.Register<Stepper, IReadOnlyList<StepItem>?>(nameof(Steps));

    public IReadOnlyList<StepItem>? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }
}

/// <summary>
/// Page sub-tabs as parallelogram flags (spec 03 section 5): the active flag slides to the selected tab
/// (no slide under reduced motion), Left/Right move the selection. Ctrl+Tab cycling is the shell's.
/// </summary>
public sealed class SkewTabStrip : TabStrip
{
    private Control? _indicator;
    private Control? _host;

    protected override Type StyleKeyOverride => typeof(SkewTabStrip);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _indicator = e.NameScope.Find<Control>("PART_Indicator");
        _host = e.NameScope.Find<Control>("PART_ItemsPresenter");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty || change.Property == BoundsProperty)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(PlaceIndicator, global::Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        PlaceIndicator();
        return size;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var count = ItemCount;
        if (count > 0 && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right)
        {
            var next = SelectedIndex + (e.Key == Key.Right ? 1 : -1);
            SelectedIndex = Math.Clamp(next, 0, count - 1);
            ContainerFromIndex(SelectedIndex)?.Focus(NavigationMethod.Directional);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void PlaceIndicator()
    {
        if (_indicator is null || _host is null)
        {
            return;
        }

        if (SelectedIndex < 0 || ContainerFromIndex(SelectedIndex) is not Control selected || selected.Bounds.Width <= 0)
        {
            _indicator.IsVisible = false;
            return;
        }

        var origin = selected.TranslatePoint(default, _host) ?? default;
        _indicator.IsVisible = true;
        _indicator.Width = selected.Bounds.Width;
        _indicator.Height = selected.Bounds.Height;
        _indicator.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateX({origin.X}px)"));
    }
}

/// <summary>2-5 mutually exclusive options in one strip (Per game / Totals / Per 36). Arrow keys move.</summary>
public sealed class SegmentedControl : ListBox
{
    protected override Type StyleKeyOverride => typeof(SegmentedControl);
}

/// <summary>One nav rail entry: a section that exists, its icon resource, and an attention count.</summary>
public sealed record NavEntry(string Key, string Label, string IconKey, int BadgeCount = 0, string? Shortcut = null)
{
    public bool HasBadge => BadgeCount > 0;

    public string ToolTipText => Shortcut is null ? Label : $"{Label} ({Shortcut})";
}

/// <summary>
/// The shell's section list (spec 03 section 5): icon + label, 3px accent stripe on the active item,
/// count badges, collapsed to icons (with tooltip labels) by <see cref="IsCollapsed"/>.
/// </summary>
public sealed class NavRail : ListBox
{
    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<NavRail, bool>(nameof(IsCollapsed));

    protected override Type StyleKeyOverride => typeof(NavRail);

    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCollapsedProperty)
        {
            PseudoClasses.Set(":collapsed", IsCollapsed);
        }
    }
}

/// <summary>A count on a nav item or tab, or a dot when <see cref="IsDot"/>; hidden at zero.</summary>
public sealed class Badge : TemplatedControl
{
    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<Badge, int>(nameof(Count));

    public static readonly StyledProperty<bool> IsDotProperty =
        AvaloniaProperty.Register<Badge, bool>(nameof(IsDot));

    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public bool IsDot
    {
        get => GetValue(IsDotProperty);
        set => SetValue(IsDotProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CountProperty || change.Property == IsDotProperty)
        {
            IsVisible = Count > 0;
            PseudoClasses.Set(":dot", IsDot);
        }
    }
}

/// <summary>A 1px hairline in the Stroke token, horizontal or vertical. Decorative only.</summary>
public sealed class Divider : Control
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Divider, Orientation>(nameof(Orientation));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Divider, IBrush?>(nameof(Stroke));

    static Divider()
    {
        AffectsMeasure<Divider>(OrientationProperty);
        AffectsRender<Divider>(StrokeProperty);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Orientation == Orientation.Horizontal ? new Size(0, 1) : new Size(1, 0);

    public override void Render(DrawingContext context)
    {
        if (Stroke is { } stroke)
        {
            context.FillRectangle(stroke, new Rect(Bounds.Size));
        }
    }
}
