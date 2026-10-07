using Avalonia;
using Avalonia.Controls;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// Lays its children out left to right, each as wide as its <see cref="SpanProperty"/> share of the
/// total — the season ribbon, where a day is one span and a phase is as many spans as it has days.
/// A <see cref="UniformGrid"/> can only do equal widths, and a Grid's columns cannot be bound.
/// </summary>
public sealed class SpanPanel : Panel
{
    public static readonly AttachedProperty<int> SpanProperty =
        AvaloniaProperty.RegisterAttached<SpanPanel, Control, int>("Span", 1);

    static SpanPanel()
    {
        AffectsParentMeasure<SpanPanel>(SpanProperty);
        AffectsParentArrange<SpanPanel>(SpanProperty);
    }

    public static int GetSpan(Control element) => element.GetValue(SpanProperty);

    public static void SetSpan(Control element, int value) => element.SetValue(SpanProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var total = TotalSpan();
        var height = 0d;
        var width = 0d;

        foreach (var child in Children)
        {
            var share = double.IsInfinity(availableSize.Width) || total == 0
                ? double.PositiveInfinity
                : availableSize.Width * Span(child) / total;
            child.Measure(new Size(share, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            width += child.DesiredSize.Width;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var total = TotalSpan();
        var x = 0d;

        foreach (var child in Children)
        {
            var width = total == 0 ? 0 : finalSize.Width * Span(child) / total;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    private static int Span(Control child) => Math.Max(0, GetSpan(child));

    private int TotalSpan() => Children.Sum(Span);
}
