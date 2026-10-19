using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Visuals;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// The page change (spec 01 section 10): fade plus a 24px slide in the navigation direction, 200ms
/// cubic-out. The built-in page slide moves the full width, which is far too much for a page change.
/// Replaced by null under reduced motion.
/// </summary>
public sealed class PageFadeSlide : IPageTransition
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(200);

    public double Offset { get; init; } = 24;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        var direction = forward ? 1 : -1;
        if (from is not null)
        {
            tasks.Add(Animate(from, 1, 0, 0, -direction * Offset, cancellationToken));
        }

        if (to is not null)
        {
            to.IsVisible = true;
            tasks.Add(Animate(to, 0, 1, direction * Offset, 0, cancellationToken));
        }

        await Task.WhenAll(tasks);
        if (from is not null && !cancellationToken.IsCancellationRequested)
        {
            from.IsVisible = false;
        }
    }

    private Task Animate(Visual target, double fromOpacity, double toOpacity, double fromX, double toX, CancellationToken cancellationToken)
    {
        var animation = new Animation
        {
            Duration = Duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, fromOpacity), new Setter(TranslateTransform.XProperty, fromX) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, toOpacity), new Setter(TranslateTransform.XProperty, toX) } },
            },
        };
        return animation.RunAsync(target, cancellationToken);
    }
}
