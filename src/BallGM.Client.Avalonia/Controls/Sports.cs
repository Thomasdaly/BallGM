using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>A status as text plus kind, for chips on cards and rows.</summary>
public sealed record StatusItem(string Text, StatusKind Kind);

/// <summary>What a <see cref="PlayerCard"/> or player hover card shows; every field comes from existing read models.</summary>
public sealed record PlayerCardModel(
    string PlayerId,
    string Name,
    string? TeamId,
    string? TeamName,
    string Positions,
    int Age,
    int Overall,
    int Potential,
    string? ContractLine,
    IReadOnlyList<StatusItem> Statuses,
    bool IsInjured = false,
    string? Number = null)
{
    public string Meta => string.Create(CultureInfo.InvariantCulture, $"{Positions} · AGE {Age}");

    public bool HasContract => !string.IsNullOrEmpty(ContractLine);

    /// <summary>Potential as the scouts' stars, where the game keeps the figure hidden (replaces the POT badge).</summary>
    public string? PotentialStars { get; init; }

    public bool HasPotentialStars => !string.IsNullOrEmpty(PotentialStars);
}

/// <summary>What a <see cref="TeamCard"/> or team hover card shows.</summary>
public sealed record TeamCardModel(string TeamId, string Name, string Record, string? Standing, string? Payroll)
{
    public string Meta => Standing is null ? Record : $"{Record} · {Standing}";
}

/// <summary>A player at a glance: headshot, name, positions, OVR/POT, age, contract line, status chips.</summary>
public sealed class PlayerCard : TemplatedControl
{
    public static readonly StyledProperty<PlayerCardModel?> ModelProperty =
        AvaloniaProperty.Register<PlayerCard, PlayerCardModel?>(nameof(Model));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<PlayerCard, bool>(nameof(IsCompact));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<PlayerCard, ICommand?>(nameof(OpenCommand));

    public PlayerCardModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty)
        {
            PseudoClasses.Set(":compact", IsCompact);
        }
    }
}

/// <summary>A team at a glance: logo, name, record, standing, payroll.</summary>
public sealed class TeamCard : TemplatedControl
{
    public static readonly StyledProperty<TeamCardModel?> ModelProperty =
        AvaloniaProperty.Register<TeamCard, TeamCardModel?>(nameof(Model));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<TeamCard, bool>(nameof(IsCompact));

    public TeamCardModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty)
        {
            PseudoClasses.Set(":compact", IsCompact);
        }
    }
}

/// <summary>
/// The rich tooltip on an entity link (spec 03 section 7): headshot or logo, key facts, OVR, status, and
/// the entity ID when "Show entity IDs" is on.
/// </summary>
public sealed class HoverCard : TemplatedControl
{
    public static readonly StyledProperty<PlayerCardModel?> PlayerProperty =
        AvaloniaProperty.Register<HoverCard, PlayerCardModel?>(nameof(Player));

    public static readonly StyledProperty<TeamCardModel?> TeamProperty =
        AvaloniaProperty.Register<HoverCard, TeamCardModel?>(nameof(Team));

    public HoverCard()
    {
        ShowEntityIds = UiServices.ShowEntityIds;
    }

    public PlayerCardModel? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public TeamCardModel? Team
    {
        get => GetValue(TeamProperty);
        set => SetValue(TeamProperty, value);
    }

    public bool ShowEntityIds { get; }

    public static readonly DirectProperty<HoverCard, string?> EntityIdProperty =
        AvaloniaProperty.RegisterDirect<HoverCard, string?>(nameof(EntityId), card => card.EntityId);

    public string? EntityId => Player?.PlayerId ?? Team?.TeamId;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty || change.Property == TeamProperty)
        {
            RaisePropertyChanged(EntityIdProperty, null, EntityId);
            PseudoClasses.Set(":player", Player is not null);
            PseudoClasses.Set(":team", Team is not null && Player is null);
            PseudoClasses.Set(":ids", ShowEntityIds);
        }
    }
}

/// <summary>
/// Form over the last games (spec 01 section 12): a 1.5px line in the secondary text colour with the
/// last point in the accent. Its figures are always printed beside it by the host.
/// </summary>
public sealed class FormSparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<FormSparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<FormSparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> LastPointProperty =
        AvaloniaProperty.Register<FormSparkline, IBrush?>(nameof(LastPoint));

    static FormSparkline()
    {
        AffectsRender<FormSparkline>(ValuesProperty, StrokeProperty, LastPointProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? LastPoint
    {
        get => GetValue(LastPointProperty);
        set => SetValue(LastPointProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Values is not { Count: > 0 } values || Stroke is not { } stroke)
        {
            return;
        }

        const double Dot = 3;
        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(1e-9, max - min);
        var width = Bounds.Width - (2 * Dot);
        var height = Bounds.Height - (2 * Dot);
        Point At(int index) => new(
            Dot + (values.Count == 1 ? width : width * index / (values.Count - 1)),
            Dot + (height * (max - values[index]) / range));

        var pen = new Pen(stroke, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (var index = 1; index < values.Count; index++)
        {
            context.DrawLine(pen, At(index - 1), At(index));
        }

        context.DrawEllipse(LastPoint ?? stroke, null, At(values.Count - 1), Dot, Dot);
    }
}

/// <summary>One game in a W/L strip.</summary>
public sealed record FormResult(bool Won, string? Detail = null)
{
    public string Letter => Won ? "W" : "L";

    public bool Lost => !Won;
}

/// <summary>Last results as letters: W filled positive, L outlined negative - the letter is always there.</summary>
public sealed class WinLossStrip : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<FormResult>?> ResultsProperty =
        AvaloniaProperty.Register<WinLossStrip, IReadOnlyList<FormResult>?>(nameof(Results));

    public IReadOnlyList<FormResult>? Results
    {
        get => GetValue(ResultsProperty);
        set => SetValue(ResultsProperty, value);
    }
}

/// <summary>Head-to-head bar: each side in its team's accent, the values printed at the ends.</summary>
public sealed class ComparisonBar : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ComparisonBar, string?>(nameof(Label));

    public static readonly StyledProperty<double> LeftValueProperty =
        AvaloniaProperty.Register<ComparisonBar, double>(nameof(LeftValue));

    public static readonly StyledProperty<double> RightValueProperty =
        AvaloniaProperty.Register<ComparisonBar, double>(nameof(RightValue));

    public static readonly StyledProperty<string?> LeftTextProperty =
        AvaloniaProperty.Register<ComparisonBar, string?>(nameof(LeftText));

    public static readonly StyledProperty<string?> RightTextProperty =
        AvaloniaProperty.Register<ComparisonBar, string?>(nameof(RightText));

    public static readonly StyledProperty<IBrush?> LeftBrushProperty =
        AvaloniaProperty.Register<ComparisonBar, IBrush?>(nameof(LeftBrush));

    public static readonly StyledProperty<string?> LeftTeamProperty =
        AvaloniaProperty.Register<ComparisonBar, string?>(nameof(LeftTeam));

    public static readonly StyledProperty<string?> RightTeamProperty =
        AvaloniaProperty.Register<ComparisonBar, string?>(nameof(RightTeam));

    /// <summary>The left side's team abbreviation, printed above its end.</summary>
    public string? LeftTeam
    {
        get => GetValue(LeftTeamProperty);
        set => SetValue(LeftTeamProperty, value);
    }

    public string? RightTeam
    {
        get => GetValue(RightTeamProperty);
        set => SetValue(RightTeamProperty, value);
    }

    public static readonly StyledProperty<IBrush?> RightBrushProperty =
        AvaloniaProperty.Register<ComparisonBar, IBrush?>(nameof(RightBrush));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double LeftValue
    {
        get => GetValue(LeftValueProperty);
        set => SetValue(LeftValueProperty, value);
    }

    public double RightValue
    {
        get => GetValue(RightValueProperty);
        set => SetValue(RightValueProperty, value);
    }

    public string? LeftText
    {
        get => GetValue(LeftTextProperty);
        set => SetValue(LeftTextProperty, value);
    }

    public string? RightText
    {
        get => GetValue(RightTextProperty);
        set => SetValue(RightTextProperty, value);
    }

    public IBrush? LeftBrush
    {
        get => GetValue(LeftBrushProperty);
        set => SetValue(LeftBrushProperty, value);
    }

    public IBrush? RightBrush
    {
        get => GetValue(RightBrushProperty);
        set => SetValue(RightBrushProperty, value);
    }
}

/// <summary>The two-colour bar inside <see cref="ComparisonBar"/>.</summary>
public sealed class SplitBar : Control
{
    public static readonly StyledProperty<double> LeftValueProperty =
        AvaloniaProperty.Register<SplitBar, double>(nameof(LeftValue));

    public static readonly StyledProperty<double> RightValueProperty =
        AvaloniaProperty.Register<SplitBar, double>(nameof(RightValue));

    public static readonly StyledProperty<IBrush?> LeftBrushProperty =
        AvaloniaProperty.Register<SplitBar, IBrush?>(nameof(LeftBrush));

    public static readonly StyledProperty<IBrush?> RightBrushProperty =
        AvaloniaProperty.Register<SplitBar, IBrush?>(nameof(RightBrush));

    static SplitBar()
    {
        AffectsRender<SplitBar>(LeftValueProperty, RightValueProperty, LeftBrushProperty, RightBrushProperty);
    }

    public double LeftValue
    {
        get => GetValue(LeftValueProperty);
        set => SetValue(LeftValueProperty, value);
    }

    public double RightValue
    {
        get => GetValue(RightValueProperty);
        set => SetValue(RightValueProperty, value);
    }

    public IBrush? LeftBrush
    {
        get => GetValue(LeftBrushProperty);
        set => SetValue(LeftBrushProperty, value);
    }

    public IBrush? RightBrush
    {
        get => GetValue(RightBrushProperty);
        set => SetValue(RightBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 8);

    public override void Render(DrawingContext context)
    {
        var total = Math.Max(1e-9, Math.Abs(LeftValue) + Math.Abs(RightValue));
        var split = Bounds.Width * Math.Abs(LeftValue) / total;
        const double Gap = 2;
        context.FillRectangle(LeftBrush ?? Brushes.Gray, new Rect(0, 0, Math.Max(0, split - Gap), Bounds.Height));
        context.FillRectangle(RightBrush ?? Brushes.Gray, new Rect(split + Gap, 0, Math.Max(0, Bounds.Width - split - Gap), Bounds.Height));
    }
}

/// <summary>One stacked segment of a <see cref="CapBar"/>: committed salary of one kind.</summary>
public sealed record CapSegment(string Label, long Amount);

/// <summary>A labelled vertical marker on a <see cref="CapBar"/>: the cap, the tax line, an apron.</summary>
public sealed record CapMarker(string Label, long Amount);

/// <summary>
/// Committed salary as a stacked bar with the thresholds the league models marked and labelled
/// (spec 01 section 12). Whatever lies beyond the first marker is drawn in Warning, beyond the tax line in
/// Negative.
/// </summary>
public sealed class CapBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<CapSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<CapBar, IReadOnlyList<CapSegment>?>(nameof(Segments));

    public static readonly StyledProperty<IReadOnlyList<CapMarker>?> MarkersProperty =
        AvaloniaProperty.Register<CapBar, IReadOnlyList<CapMarker>?>(nameof(Markers));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<CapBar, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CapBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> OverProperty =
        AvaloniaProperty.Register<CapBar, IBrush?>(nameof(Over));

    public static readonly StyledProperty<IBrush?> TaxProperty =
        AvaloniaProperty.Register<CapBar, IBrush?>(nameof(Tax));

    public static readonly StyledProperty<IBrush?> MarkerProperty =
        AvaloniaProperty.Register<CapBar, IBrush?>(nameof(Marker));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<CapBar, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<CapBar>();

    static CapBar()
    {
        AffectsRender<CapBar>(SegmentsProperty, MarkersProperty, TrackProperty, FillProperty, OverProperty, TaxProperty, MarkerProperty, LabelBrushProperty);
        AffectsMeasure<CapBar>(SegmentsProperty);
    }

    public IReadOnlyList<CapSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IReadOnlyList<CapMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Over
    {
        get => GetValue(OverProperty);
        set => SetValue(OverProperty, value);
    }

    public IBrush? Tax
    {
        get => GetValue(TaxProperty);
        set => SetValue(TaxProperty, value);
    }

    public IBrush? Marker
    {
        get => GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Segment shades, strongest first: each contract type keeps its own step of the zone's colour.</summary>
    private static readonly double[] SegmentOpacities = [1.0, 0.7, 0.5, 0.35, 0.25];

    protected override Size MeasureOverride(Size availableSize) => new(0, (Segments?.Count ?? 0) > 1 ? 82 : 58);

    public override void Render(DrawingContext context)
    {
        const double BarTop = 4;
        const double BarHeight = 14;
        var segments = Segments ?? [];
        var markers = Markers ?? [];
        var total = segments.Sum(segment => segment.Amount);
        var scaleMax = Math.Max(1, Math.Max(total, markers.Count == 0 ? 0 : markers.Max(marker => marker.Amount)) * 1.08);
        double X(long amount) => Bounds.Width * amount / scaleMax;

        context.FillRectangle(Track ?? Brushes.Transparent, new Rect(0, BarTop, Bounds.Width, BarHeight));
        var ordered = markers.OrderBy(marker => marker.Amount).ToList();
        var firstLine = ordered.FirstOrDefault()?.Amount ?? long.MaxValue;
        var taxLine = ordered.FirstOrDefault(marker => marker.Label.Contains("TAX", StringComparison.OrdinalIgnoreCase))?.Amount ?? long.MaxValue;

        // Each contract type is its own segment; the part of it past a threshold takes Warning, past the tax Negative.
        var running = 0L;
        for (var index = 0; index < segments.Count; index++)
        {
            var start = running;
            var end = running + segments[index].Amount;
            var opacity = SegmentOpacities[Math.Min(index, SegmentOpacities.Length - 1)];
            void Span(long from, long to, IBrush? brush, IBrush fallback)
            {
                if (to > from)
                {
                    using (context.PushOpacity(opacity))
                    {
                        context.FillRectangle(brush ?? fallback, new Rect(X(from), BarTop, X(to) - X(from), BarHeight));
                    }
                }
            }

            Span(start, Math.Min(end, firstLine), Fill, Brushes.Gray);
            Span(Math.Max(start, firstLine), Math.Min(end, taxLine), Over, Brushes.Orange);
            Span(Math.Max(start, taxLine), end, Tax, Brushes.Red);
            if (index < segments.Count - 1)
            {
                context.FillRectangle(Track ?? Brushes.Black, new Rect(X(end) - 0.5, BarTop, 1, BarHeight));
            }

            running = end;
        }

        // The legend: one swatch per contract type, under the marker labels.
        if (segments.Count > 1)
        {
            var legendTypeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight.SemiBold);
            var x = 0.0;
            for (var index = 0; index < segments.Count; index++)
            {
                var opacity = SegmentOpacities[Math.Min(index, SegmentOpacities.Length - 1)];
                using (context.PushOpacity(opacity))
                {
                    context.FillRectangle(Fill ?? Brushes.Gray, new Rect(x, 66, 10, 10));
                }

                var label = new FormattedText(segments[index].Label.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, legendTypeface, 11, LabelBrush ?? Brushes.White);
                context.DrawText(label, new Point(x + 14, 64));
                x += 14 + label.Width + 16;
            }
        }

        // Every marker is labelled: a label that would collide with the previous one drops to a second row.
        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight.SemiBold);
        var rowEnds = new[] { double.NegativeInfinity, double.NegativeInfinity };
        foreach (var marker in ordered)
        {
            var x = X(marker.Amount);
            var text = new FormattedText(marker.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, LabelBrush ?? Brushes.White);
            var left = Math.Clamp(x - (text.Width / 2), 0, Math.Max(0, Bounds.Width - text.Width));
            var row = left > rowEnds[0] + 6 ? 0 : 1;
            var top = BarTop + BarHeight + 6 + (row * 14);
            context.FillRectangle(Marker ?? Brushes.White, new Rect(x - 1, 0, 2, top - 2));
            context.DrawText(text, new Point(left, top));
            rowEnds[row] = left + text.Width;
        }
    }
}

/// <summary>One team's row of a linescore.</summary>
public sealed record LinescoreRow(string? TeamId, string Abbreviation, IReadOnlyList<int> Periods, int Total, bool IsWinner = false);

/// <summary>Points by period, broadcast style (spec 03 section 7).</summary>
public sealed class Linescore : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<string>?> PeriodLabelsProperty =
        AvaloniaProperty.Register<Linescore, IReadOnlyList<string>?>(nameof(PeriodLabels));

    public static readonly StyledProperty<IReadOnlyList<LinescoreRow>?> RowsProperty =
        AvaloniaProperty.Register<Linescore, IReadOnlyList<LinescoreRow>?>(nameof(Rows));

    public IReadOnlyList<string>? PeriodLabels
    {
        get => GetValue(PeriodLabelsProperty);
        set => SetValue(PeriodLabelsProperty, value);
    }

    public IReadOnlyList<LinescoreRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }
}

/// <summary>One side of a <see cref="ScoreBug"/>.</summary>
public sealed record ScoreBugSide(string? TeamId, string Abbreviation, string Name, int? Score, bool IsWinner = false);

/// <summary>
/// The broadcast score bug: logos, abbreviations, scores in skewed segments, and the status (FINAL,
/// Q4 2:31, 7:30 PM). Only fields the game has.
/// </summary>
public sealed class ScoreBug : TemplatedControl
{
    public static readonly StyledProperty<ScoreBugSide?> AwayProperty =
        AvaloniaProperty.Register<ScoreBug, ScoreBugSide?>(nameof(Away));

    public static readonly StyledProperty<ScoreBugSide?> HomeProperty =
        AvaloniaProperty.Register<ScoreBug, ScoreBugSide?>(nameof(Home));

    public static readonly StyledProperty<string?> StatusProperty =
        AvaloniaProperty.Register<ScoreBug, string?>(nameof(Status));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<ScoreBug, bool>(nameof(IsCompact));

    public ScoreBugSide? Away
    {
        get => GetValue(AwayProperty);
        set => SetValue(AwayProperty, value);
    }

    public ScoreBugSide? Home
    {
        get => GetValue(HomeProperty);
        set => SetValue(HomeProperty, value);
    }

    public string? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty)
        {
            PseudoClasses.Set(":compact", IsCompact);
        }
    }
}

/// <summary>One ticker entry: a score or a headline.</summary>
public sealed record TickerItem(string Text, string? Tag = null)
{
    public bool HasTag => Tag is not null;
}

/// <summary>
/// The bottom ticker (spec 03 section 7): scores and headlines scrolling at 40px/s, pausing under the
/// pointer; under reduced motion it pages every 6 seconds instead of scrolling.
/// </summary>
public sealed class Ticker : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<Ticker, string?>(nameof(Label), "SCORES");

    public static readonly StyledProperty<IReadOnlyList<TickerItem>?> ItemsProperty =
        AvaloniaProperty.Register<Ticker, IReadOnlyList<TickerItem>?>(nameof(Items));

    public static readonly DirectProperty<Ticker, IReadOnlyList<TickerItem>> ShownProperty =
        AvaloniaProperty.RegisterDirect<Ticker, IReadOnlyList<TickerItem>>(nameof(Shown), ticker => ticker.Shown);

    private const double Speed = 40;
    private const int PageSize = 4;
    private DispatcherTimer? _timer;
    private Control? _strip;
    private double _offset;
    private int _page;
    private DateTime _last;
    private IReadOnlyList<TickerItem> _shown = [];

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IReadOnlyList<TickerItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>What the strip holds now: every item twice (seamless scroll) or one page (reduced motion).</summary>
    public IReadOnlyList<TickerItem> Shown
    {
        get => _shown;
        private set => SetAndRaise(ShownProperty, ref _shown, value);
    }

    /// <summary>True while paging rather than scrolling.</summary>
    public bool IsPaging => UiServices.ReducedMotion;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _strip = e.NameScope.Find<Control>("PART_Strip");
        Restart();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiServices.PreferencesChanged += OnPreferencesChanged;
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UiServices.PreferencesChanged -= OnPreferencesChanged;
        _timer?.Stop();
        _timer = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            Restart();
        }
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) => Restart();

    private void Restart()
    {
        _timer?.Stop();
        _timer = null;
        _offset = 0;
        _page = 0;
        var items = Items ?? [];
        PseudoClasses.Set(":paging", IsPaging);
        if (_strip is not null)
        {
            _strip.RenderTransform = null;
        }

        if (IsPaging)
        {
            Shown = items.Take(PageSize).ToList();
            if (items.Count > PageSize && VisualRoot is not null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Background, (_, _) =>
                {
                    var pages = (int)Math.Ceiling(items.Count / (double)PageSize);
                    _page = (_page + 1) % pages;
                    Shown = items.Skip(_page * PageSize).Take(PageSize).ToList();
                });
                _timer.Start();
            }

            return;
        }

        Shown = items.Concat(items).ToList();
        if (items.Count == 0 || VisualRoot is null)
        {
            return;
        }

        _last = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Step());
        _timer.Start();
    }

    private void Step()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _last).TotalSeconds;
        _last = now;
        if (_strip is null || IsPointerOver)
        {
            return;
        }

        var half = _strip.Bounds.Width / 2;
        if (half <= 0)
        {
            return;
        }

        _offset -= Speed * elapsed;
        if (-_offset >= half)
        {
            _offset += half;
        }

        _strip.RenderTransform = new TranslateTransform(_offset, 0);
    }
}

/// <summary>One playoff series in a bracket.</summary>
public sealed record BracketSeries(
    int? TopSeed,
    string? TopTeamId,
    string TopName,
    int TopWins,
    int? BottomSeed,
    string? BottomTeamId,
    string BottomName,
    int BottomWins,
    bool IsUserSeries = false)
{
    public bool TopLeads => TopWins > BottomWins;

    public bool BottomLeads => BottomWins > TopWins;
}

/// <summary>A bracket round: name and series.</summary>
public sealed record BracketRound(string Name, IReadOnlyList<BracketSeries> Series);

/// <summary>Broadcast-style playoff bracket: rounds as columns, seeds, logos, series scores.</summary>
public sealed class BracketView : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<BracketRound>?> RoundsProperty =
        AvaloniaProperty.Register<BracketView, IReadOnlyList<BracketRound>?>(nameof(Rounds));

    public IReadOnlyList<BracketRound>? Rounds
    {
        get => GetValue(RoundsProperty);
        set => SetValue(RoundsProperty, value);
    }
}

/// <summary>A game on a calendar day.</summary>
public sealed record CalendarGame(string GameId, string? OpponentId, string OpponentAbbreviation, bool IsHome, string? Result, bool? Won, bool IsBackToBack)
{
    public string Venue => IsHome ? "vs" : "@";

    public bool IsPlayed => Result is not null;

    public bool IsWin => Won == true;

    public bool IsLoss => Won == false;

    public string ResultLetter => Won switch
    {
        true => "W",
        false => "L",
        _ => string.Empty,
    };
}

/// <summary>One cell of a <see cref="CalendarMonth"/>.</summary>
public sealed record CalendarDay(int DayOfMonth, bool InMonth, bool IsToday, CalendarGame? Game)
{
    /// <summary>A line under an unplayed game (the opponent's record, say).</summary>
    public string? Note { get; init; }

    /// <summary>A phase that starts this day ("Trade deadline"), shown on a day with no game.</summary>
    public string? Marker { get; init; }

    public bool HasNote => !string.IsNullOrEmpty(Note) && HasGame && !IsPlayed;

    public bool HasMarker => !string.IsNullOrEmpty(Marker) && !HasGame;

    public string? Tip { get; init; }

    public bool HasGame => Game is not null;

    public string? GameId => Game?.GameId;

    public string Venue => Game?.Venue ?? string.Empty;

    public string? OpponentId => Game?.OpponentId;

    public string OpponentAbbreviation => Game?.OpponentAbbreviation ?? string.Empty;

    public bool IsBackToBack => Game?.IsBackToBack == true;

    public bool IsPlayed => Game?.IsPlayed == true;

    public string ResultLetter => Game?.ResultLetter ?? string.Empty;

    public bool IsWin => Game?.IsWin == true;

    public bool IsLoss => Game?.IsLoss == true;

    public string Result => Game?.Result ?? string.Empty;

    public string Label => DayOfMonth > 0 ? DayOfMonth.ToString(CultureInfo.InvariantCulture) : string.Empty;
}

/// <summary>A month grid of game chips: home/away, opponent logo, result with score, today, back-to-backs.</summary>
public sealed class CalendarMonth : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<CalendarDay>?> DaysProperty =
        AvaloniaProperty.Register<CalendarMonth, IReadOnlyList<CalendarDay>?>(nameof(Days));

    public static readonly StyledProperty<ICommand?> OpenGameCommandProperty =
        AvaloniaProperty.Register<CalendarMonth, ICommand?>(nameof(OpenGameCommand));

    public IReadOnlyList<CalendarDay>? Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public ICommand? OpenGameCommand
    {
        get => GetValue(OpenGameCommandProperty);
        set => SetValue(OpenGameCommandProperty, value);
    }

    public static IReadOnlyList<string> WeekdayLabels { get; } = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];
}

/// <summary>One entry on a <see cref="Timeline"/>.</summary>
public sealed record TimelineEntry(string When, string Title, string? Detail = null)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>Transactions or history down a date rail.</summary>
public sealed class Timeline : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<TimelineEntry>?> EntriesProperty =
        AvaloniaProperty.Register<Timeline, IReadOnlyList<TimelineEntry>?>(nameof(Entries));

    public IReadOnlyList<TimelineEntry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }
}

/// <summary>A news story or inbox message: lower-third headline, meta, and unread / priority / decision markers.</summary>
public sealed class NewsCard : TemplatedControl
{
    public static readonly StyledProperty<string?> HeadlineProperty =
        AvaloniaProperty.Register<NewsCard, string?>(nameof(Headline));

    public static readonly StyledProperty<string?> MetaProperty =
        AvaloniaProperty.Register<NewsCard, string?>(nameof(Meta));

    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<NewsCard, string?>(nameof(Summary));

    public static readonly StyledProperty<bool> IsUnreadProperty =
        AvaloniaProperty.Register<NewsCard, bool>(nameof(IsUnread));

    public static readonly StyledProperty<bool> NeedsDecisionProperty =
        AvaloniaProperty.Register<NewsCard, bool>(nameof(NeedsDecision));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<NewsCard, object?>(nameof(Actions));

    /// <summary>The message's decision buttons, under its summary.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public string? Headline
    {
        get => GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public bool IsUnread
    {
        get => GetValue(IsUnreadProperty);
        set => SetValue(IsUnreadProperty, value);
    }

    public bool NeedsDecision
    {
        get => GetValue(NeedsDecisionProperty);
        set => SetValue(NeedsDecisionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsUnreadProperty || change.Property == NeedsDecisionProperty)
        {
            PseudoClasses.Set(":unread", IsUnread);
            PseudoClasses.Set(":decision", NeedsDecision);
        }
    }
}
