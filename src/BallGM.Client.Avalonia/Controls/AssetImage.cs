using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BallGM.Client.Avalonia.Imaging;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// Draws one resolver-backed image in a slot (spec 05 section 3): the cached image or the vector
/// fallback immediately, the real image when it arrives. A key change - a recycled list row - cancels
/// the old request and ignores any late result, so a row never flashes another player's face.
/// Headshots fill their frame anchored bottom-centre on the team backdrop; logos fit, centred, never
/// upscaled past 2x.
/// </summary>
public sealed class AssetImage : Control
{
    public static readonly StyledProperty<AssetEntityKind> KindProperty =
        AvaloniaProperty.Register<AssetImage, AssetEntityKind>(nameof(Kind));

    public static readonly StyledProperty<string?> EntityIdProperty =
        AvaloniaProperty.Register<AssetImage, string?>(nameof(EntityId));

    public static readonly StyledProperty<bool> IsCircleProperty =
        AvaloniaProperty.Register<AssetImage, bool>(nameof(IsCircle));

    public static readonly DirectProperty<AssetImage, IImage?> SourceProperty =
        AvaloniaProperty.RegisterDirect<AssetImage, IImage?>(nameof(Source), image => image.Source);

    private CancellationTokenSource? _pending;
    private IImage? _source;
    private IAssetResolver? _subscribed;

    static AssetImage()
    {
        AffectsRender<AssetImage>(SourceProperty, IsCircleProperty);
        ClipToBoundsProperty.OverrideDefaultValue<AssetImage>(true);
    }

    public AssetEntityKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? EntityId
    {
        get => GetValue(EntityIdProperty);
        set => SetValue(EntityIdProperty, value);
    }

    public bool IsCircle
    {
        get => GetValue(IsCircleProperty);
        set => SetValue(IsCircleProperty, value);
    }

    /// <summary>What is drawn now: the fallback, a cached image, or the resolved one.</summary>
    public IImage? Source
    {
        get => _source;
        private set => SetAndRaise(SourceProperty, ref _source, value);
    }

    /// <summary>The key whose image <see cref="Source"/> belongs to (test hook for recycling).</summary>
    public AssetKey? ShownKey { get; private set; }

    private AssetSlot Slot => Kind == AssetEntityKind.Player
        ? AssetSlot.Headshot(Math.Max(Bounds.Height, Height is > 0 and not double.NaN ? Height : 48))
        : new AssetSlot(Kind == AssetEntityKind.League ? AssetSlotKind.LeagueLogo : AssetSlotKind.Logo, Math.Max(Bounds.Width, Width is > 0 and not double.NaN ? Width : 32));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _subscribed = UiServices.Assets;
        _subscribed.Reloaded += OnReloaded;
        Load();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_subscribed is not null)
        {
            _subscribed.Reloaded -= OnReloaded;
            _subscribed = null;
        }

        _pending?.Cancel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == EntityIdProperty || change.Property == KindProperty) && VisualRoot is not null)
        {
            Load();
        }
    }

    private void OnReloaded(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Load);

    private void Load()
    {
        _pending?.Cancel();
        _pending = null;
        if (EntityId is not { Length: > 0 } id)
        {
            Source = null;
            ShownKey = null;
            return;
        }

        var resolver = UiServices.Assets;
        var key = new AssetKey(Kind, id);
        var slot = Slot;
        ShownKey = key;
        if (resolver.TryGetCached(key, slot, out var cached))
        {
            Source = cached;
            return;
        }

        Source = resolver.Fallback(key, slot);
        var pending = new CancellationTokenSource();
        _pending = pending;
        _ = Resolve(resolver, key, slot, pending.Token);
    }

    private async Task Resolve(IAssetResolver resolver, AssetKey key, AssetSlot slot, CancellationToken cancellationToken)
    {
        IImage image;
        try
        {
            image = await resolver.ResolveAsync(key, slot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            // A recycled control has moved on to another key: the late image is not ours.
            if (cancellationToken.IsCancellationRequested || ShownKey != key || ReferenceEquals(Source, image))
            {
                return;
            }

            Source = image;
            if (!UiServices.ReducedMotion)
            {
                Opacity = 0;
                Transitions ??= [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
            }

            Opacity = 1;
        });
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Source is not { } source || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var clip = IsCircle ? context.PushGeometryClip(new EllipseGeometry(bounds)) : default(DrawingContext.PushedState?);
        if (source is not Bitmap bitmap)
        {
            // Vector fallbacks are drawn to fill the frame.
            source.Draw(context, new Rect(source.Size), bounds);
            return;
        }

        var size = bitmap.Size;
        if (Kind == AssetEntityKind.Player)
        {
            if (UiServices.Assets.Describe(new AssetKey(Kind, EntityId ?? string.Empty)) is { } entry)
            {
                // Cutouts sit on the team backdrop rather than on whatever is behind the frame.
                var backdrop = FallbackArt.Backdrop(entry);
                backdrop.Draw(context, new Rect(backdrop.Size), bounds);
            }

            // UniformToFill, anchored bottom-centre: crop the sides or the top, never the shoulders.
            var scale = Math.Max(bounds.Width / size.Width, bounds.Height / size.Height);
            var sourceWidth = bounds.Width / scale;
            var sourceHeight = bounds.Height / scale;
            var sourceRect = new Rect((size.Width - sourceWidth) / 2, size.Height - sourceHeight, sourceWidth, sourceHeight);
            context.DrawImage(bitmap, sourceRect, bounds);
            return;
        }

        // Uniform, centred, at most twice the decoded size.
        var fit = Math.Min(Math.Min(bounds.Width / size.Width, bounds.Height / size.Height), 2.0);
        var drawn = new Size(size.Width * fit, size.Height * fit);
        var destination = new Rect(bounds.Center.X - (drawn.Width / 2), bounds.Center.Y - (drawn.Height / 2), drawn.Width, drawn.Height);
        context.DrawImage(bitmap, new Rect(size), destination);
    }
}
