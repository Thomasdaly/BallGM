using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// A parallelogram background leaning <see cref="Angle"/> degrees - the interface's signature shape
/// (spec 01 section 6). Only the background leans; the child stays upright, inset by height x tan(angle)
/// on each side so it never touches a slanted edge. Hit testing follows the slanted outline, so the
/// transparent corners never swallow a click meant for a neighbour. At angle 0 it is a plain
/// (optionally rounded) border.
/// </summary>
public class SkewBorder : Decorator, ICustomHitTest
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<SkewBorder, double>(nameof(Angle));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<SkewBorder>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<SkewBorder>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SkewBorder, double>(nameof(StrokeThickness));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<SkewBorder>();

    private Geometry? _shape;
    private Size _shapeSize;
    private double _shapeAngle;
    private CornerRadius _shapeRadius;
    private Pen? _pen;

    static SkewBorder()
    {
        AffectsRender<SkewBorder>(BackgroundProperty, BorderBrushProperty, StrokeThicknessProperty, AngleProperty, CornerRadiusProperty);
        AffectsMeasure<SkewBorder>(AngleProperty);
    }

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Horizontal run of the slant over a given height.</summary>
    public double InsetFor(double height) => Math.Abs(height * Math.Tan(Angle * Math.PI / 180));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BorderBrushProperty || change.Property == StrokeThicknessProperty)
        {
            _pen = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var inner = availableSize.Deflate(padding);
        Child?.Measure(inner);
        var childSize = Child?.DesiredSize ?? default;
        var height = childSize.Height + padding.Top + padding.Bottom;
        var width = childSize.Width + padding.Left + padding.Right + (2 * InsetFor(height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var inset = InsetFor(finalSize.Height);
        var padding = Padding;
        var rect = new Rect(
            padding.Left + inset,
            padding.Top,
            Math.Max(0, finalSize.Width - padding.Left - padding.Right - (2 * inset)),
            Math.Max(0, finalSize.Height - padding.Top - padding.Bottom));
        Child?.Arrange(rect);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || (Background is null && (BorderBrush is null || StrokeThickness <= 0)))
        {
            return;
        }

        var shape = Shape(size);
        if (BorderBrush is { } stroke && StrokeThickness > 0)
        {
            _pen ??= new Pen(stroke, StrokeThickness);
        }

        context.DrawGeometry(Background, StrokeThickness > 0 ? _pen : null, shape);
    }

    public bool HitTest(Point point) => Shape(Bounds.Size).FillContains(point);

    private Geometry Shape(Size size)
    {
        if (_shape is not null && _shapeSize == size && _shapeAngle == Angle && _shapeRadius == CornerRadius)
        {
            return _shape;
        }

        var half = StrokeThickness / 2;
        var bounds = new Rect(size).Deflate(half);
        if (Angle == 0)
        {
            _shape = new RectangleGeometry(bounds, CornerRadius.TopLeft, CornerRadius.TopLeft);
        }
        else
        {
            var inset = InsetFor(bounds.Height);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(bounds.Left + inset, bounds.Top), isFilled: true);
                context.LineTo(new Point(bounds.Right, bounds.Top));
                context.LineTo(new Point(bounds.Right - inset, bounds.Bottom));
                context.LineTo(new Point(bounds.Left, bounds.Bottom));
                context.EndFigure(isClosed: true);
            }

            _shape = geometry;
        }

        _shapeSize = size;
        _shapeAngle = Angle;
        _shapeRadius = CornerRadius;
        return _shape;
    }
}
