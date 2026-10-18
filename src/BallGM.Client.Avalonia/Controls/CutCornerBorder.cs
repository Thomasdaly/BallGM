using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>Which corners a <see cref="CutCornerBorder"/> cuts.</summary>
[Flags]
public enum CutCorners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
}

/// <summary>
/// A panel shape with 45-degree cut corners (spec 01 section 6: featured tiles and hero bands, one 16px
/// cut top-right by default). With <see cref="ClipContent"/> the child - an image, say - is clipped to
/// the same outline.
/// </summary>
public class CutCornerBorder : Decorator
{
    public static readonly StyledProperty<double> CutSizeProperty =
        AvaloniaProperty.Register<CutCornerBorder, double>(nameof(CutSize), 16);

    public static readonly StyledProperty<CutCorners> CornersProperty =
        AvaloniaProperty.Register<CutCornerBorder, CutCorners>(nameof(Corners), CutCorners.TopRight);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<CutCornerBorder>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<CutCornerBorder>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CutCornerBorder, double>(nameof(StrokeThickness));

    public static readonly StyledProperty<bool> ClipContentProperty =
        AvaloniaProperty.Register<CutCornerBorder, bool>(nameof(ClipContent));

    private Geometry? _shape;
    private Size _shapeSize;

    static CutCornerBorder()
    {
        AffectsRender<CutCornerBorder>(CutSizeProperty, CornersProperty, BackgroundProperty, BorderBrushProperty, StrokeThicknessProperty);
    }

    public double CutSize
    {
        get => GetValue(CutSizeProperty);
        set => SetValue(CutSizeProperty, value);
    }

    public CutCorners Corners
    {
        get => GetValue(CornersProperty);
        set => SetValue(CornersProperty, value);
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

    public bool ClipContent
    {
        get => GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CutSizeProperty || change.Property == CornersProperty)
        {
            _shape = null;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        Clip = ClipContent ? Shape(finalSize) : null;
        return arranged;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var pen = BorderBrush is { } brush && StrokeThickness > 0 ? new Pen(brush, StrokeThickness) : null;
        context.DrawGeometry(Background, pen, Shape(Bounds.Size));
    }

    private Geometry Shape(Size size)
    {
        if (_shape is not null && _shapeSize == size)
        {
            return _shape;
        }

        var cut = Math.Min(CutSize, Math.Min(size.Width, size.Height) / 2);
        var corners = Corners;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            double C(CutCorners corner) => corners.HasFlag(corner) ? cut : 0;
            context.BeginFigure(new Point(C(CutCorners.TopLeft), 0), isFilled: true);
            context.LineTo(new Point(size.Width - C(CutCorners.TopRight), 0));
            context.LineTo(new Point(size.Width, C(CutCorners.TopRight)));
            context.LineTo(new Point(size.Width, size.Height - C(CutCorners.BottomRight)));
            context.LineTo(new Point(size.Width - C(CutCorners.BottomRight), size.Height));
            context.LineTo(new Point(C(CutCorners.BottomLeft), size.Height));
            context.LineTo(new Point(0, size.Height - C(CutCorners.BottomLeft)));
            context.LineTo(new Point(0, C(CutCorners.TopLeft)));
            context.EndFigure(isClosed: true);
        }

        _shape = geometry;
        _shapeSize = size;
        return geometry;
    }
}
