using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// An indeterminate progress arc. Its rotation is a style animation (Styles.axaml), so reduced motion
/// removes it and leaves a still arc.
/// </summary>
public sealed class Spinner : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        global::Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<Spinner>();

    static Spinner()
    {
        AffectsRender<Spinner>(ForegroundProperty);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 2 || Foreground is not { } brush)
        {
            return;
        }

        var radius = (size / 2) - 1.5;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var geometry = new StreamGeometry();
        using (var arc = geometry.Open())
        {
            arc.BeginFigure(new Point(centre.X, centre.Y - radius), isFilled: false);
            arc.ArcTo(new Point(centre.X + radius, centre.Y), new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise);
            arc.ArcTo(new Point(centre.X, centre.Y + radius), new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise);
            arc.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(brush, 2, lineCap: PenLineCap.Round), geometry);
    }
}
