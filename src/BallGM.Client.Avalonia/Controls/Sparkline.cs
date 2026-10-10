using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// The last few match ratings as a line on a fixed 3–10 scale — fixed so two players' lines compare,
/// and starting at 3 because a rating below it is a rare disaster the line need not leave room for —
/// with a faint baseline at the 6.5 anchor and the latest game marked with a dot. Brushes come from
/// styles (tokens), never from code; the figures themselves are printed beside it by its host, so the
/// line is never the only way to read them.
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> BaselineProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Baseline));

    private const double Minimum = 3.0;
    private const double Maximum = StatFormulas.MaximumRating / 100.0;
    private const double Anchor = StatFormulas.RatingAnchor / 100.0;
    private const double DotRadius = 4.0;

    private Pen? _linePen;
    private Pen? _baselinePen;

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, BaselineProperty);
    }

    /// <summary>A brush changed: the pens built from it are rebuilt on the next render.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StrokeProperty)
        {
            _linePen = null;
        }
        else if (change.Property == BaselineProperty)
        {
            _baselinePen = null;
        }
    }

    /// <summary>Ratings, oldest first.</summary>
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

    public IBrush? Baseline
    {
        get => GetValue(BaselineProperty);
        set => SetValue(BaselineProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        var width = Bounds.Width - (2 * DotRadius);
        var height = Bounds.Height - (2 * DotRadius);
        if (values is not { Count: > 0 } || width <= 0 || height <= 0)
        {
            return;
        }

        Point At(int index, double value) => new(
            DotRadius + (values.Count == 1 ? width : width * index / (values.Count - 1)),
            DotRadius + (height * (Maximum - Math.Clamp(value, Minimum, Maximum)) / (Maximum - Minimum)));

        if (Baseline is { } baseline)
        {
            var y = At(0, Anchor).Y;
            _baselinePen ??= new Pen(baseline, 1);
            context.DrawLine(_baselinePen, new Point(DotRadius, y), new Point(DotRadius + width, y));
        }

        if (Stroke is not { } stroke)
        {
            return;
        }

        var pen = _linePen ??= new Pen(stroke, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (var index = 1; index < values.Count; index++)
        {
            context.DrawLine(pen, At(index - 1, values[index - 1]), At(index, values[index]));
        }

        context.DrawEllipse(stroke, null, At(values.Count - 1, values[^1]), DotRadius, DotRadius);
    }
}
