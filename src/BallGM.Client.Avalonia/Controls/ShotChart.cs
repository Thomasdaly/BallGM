using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// A half court in five zone regions, each filled by how the shooter's FG% from there compares with
/// the league's: a diverging scale, cold and hot arms of three steps around a neutral level band, from
/// the chart tokens. Every zone prints its figures on a dark plate, so colour is never the only signal.
/// The court is stylised — corners drawn wider than scale so their figures fit — and the basket is at
/// the top, the floor running away from it.
/// </summary>
public sealed class ShotChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ShotZoneCell>?> ZonesProperty =
        AvaloniaProperty.Register<ShotChart, IReadOnlyList<ShotZoneCell>?>(nameof(Zones));

    // Court units: feet, 50 wide; the chart shows 34 feet from the baseline.
    private const double CourtWidth = 50;
    private const double CourtDepth = 34;
    private const double BasketX = 25;
    private const double BasketY = 5.25;
    private const double ArcRadius = 23.75;
    private const double CornerWidth = 9;
    private const double CornerDepth = 14;
    private const double PaintHalfWidth = 8;
    private const double PaintDepth = 19;
    private const double RimRadius = 4;

    // Where each zone's figures sit, in court feet from the baseline: inside its own region.
    private const double PaintLabelY = 14;
    private const double MidRangeLabelY = 24.5;
    private const double AboveBreakLabelY = 31.5;

    static ShotChart()
    {
        AffectsRender<ShotChart>(ZonesProperty);
    }

    public IReadOnlyList<ShotZoneCell>? Zones
    {
        get => GetValue(ZonesProperty);
        set => SetValue(ZonesProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(width, width * CourtDepth / CourtWidth);
    }

    public override void Render(DrawingContext context)
    {
        if (Zones is not { Count: 5 } zones)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / CourtWidth, Bounds.Height / CourtDepth);
        Point At(double x, double y) => new(x * scale, y * scale);
        IBrush Fill(ShotZone zone) => Brush(zones[(int)zone].Step);
        var line = new Pen(Brush("StrokeBrush"), 1.5);

        using (context.PushClip(new Rect(0, 0, CourtWidth * scale, CourtDepth * scale)))
        {
            // Painter's order: the floor beyond the arc, inside the arc, the corners, the paint, the rim.
            context.DrawRectangle(Fill(ShotZone.AboveBreak3), null, new Rect(At(0, 0), At(CourtWidth, CourtDepth)));

            var arcY = BasketY + Math.Sqrt((ArcRadius * ArcRadius) - Math.Pow(BasketX - CornerWidth, 2));
            var inside = new StreamGeometry();
            using (var path = inside.Open())
            {
                path.BeginFigure(At(CornerWidth, 0), isFilled: true);
                path.LineTo(At(CornerWidth, arcY));
                path.ArcTo(At(CourtWidth - CornerWidth, arcY), new Size(ArcRadius * scale, ArcRadius * scale), 0, isLargeArc: false, SweepDirection.CounterClockwise);
                path.LineTo(At(CourtWidth - CornerWidth, 0));
                path.EndFigure(isClosed: true);
            }

            context.DrawGeometry(Fill(ShotZone.MidRange), line, inside);
            context.DrawRectangle(Fill(ShotZone.Corner3), line, new Rect(At(0, 0), At(CornerWidth, CornerDepth)));
            context.DrawRectangle(Fill(ShotZone.Corner3), line, new Rect(At(CourtWidth - CornerWidth, 0), At(CourtWidth, CornerDepth)));
            context.DrawRectangle(Fill(ShotZone.Paint), line, new Rect(At(BasketX - PaintHalfWidth, 0), At(BasketX + PaintHalfWidth, PaintDepth)));
            context.DrawEllipse(Fill(ShotZone.Rim), line, At(BasketX, BasketY), RimRadius * scale, RimRadius * scale);
            context.DrawRectangle(null, line, new Rect(At(0, 0), At(CourtWidth, CourtDepth)));
        }

        // Each zone's figures on a dark plate, so they read on any fill.
        Label(context, zones[(int)ShotZone.Rim], At(BasketX, BasketY));
        Label(context, zones[(int)ShotZone.Paint], At(BasketX, PaintLabelY));
        Label(context, zones[(int)ShotZone.MidRange], At(BasketX, MidRangeLabelY));
        Label(context, zones[(int)ShotZone.Corner3], At(CornerWidth / 2, CornerDepth / 2));
        Label(context, zones[(int)ShotZone.AboveBreak3], At(BasketX, AboveBreakLabelY));
    }

    private void Label(DrawingContext context, ShotZoneCell zone, Point centre)
    {
        var text = new FormattedText(
            zone.Label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), weight: FontWeight.SemiBold),
            12,
            Brush("TextPrimaryBrush"))
        {
            TextAlignment = TextAlignment.Center,
        };

        // Kept inside the court, so a zone near the edge never pushes its figures out of sight.
        var left = Math.Clamp(centre.X - (text.Width / 2) - 5, 0, Math.Max(0, Bounds.Width - text.Width - 10));
        var plate = new Rect(left, centre.Y - (text.Height / 2) - 3, text.Width + 10, text.Height + 6);
        context.DrawRectangle(Brush("ChartPlateBrush"), null, plate, 3, 3);
        context.DrawText(text, new Point(left + 5, centre.Y - (text.Height / 2)));
    }

    private IBrush Brush(int step) => Brush(step switch
    {
        <= -3 => "ChartCold3Brush",
        -2 => "ChartCold2Brush",
        -1 => "ChartCold1Brush",
        0 => "ChartNeutralBrush",
        1 => "ChartHot1Brush",
        2 => "ChartHot2Brush",
        _ => "ChartHot3Brush",
    });

    private IBrush Brush(string token) => this.TryFindResource(token, out var found) && found is IBrush brush ? brush : Brushes.Transparent;
}
