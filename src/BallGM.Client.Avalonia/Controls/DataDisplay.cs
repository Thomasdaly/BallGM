using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using BallGM.Client.Avalonia.Imaging;
using BallGM.Client.Avalonia.Theming;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// A card or dashboard tile (spec 03 section 7): H3 caps title, optional action link, content.
/// <see cref="IsFeatured"/> puts it on the team gradient with a 16px cut corner and accent stripe.
/// </summary>
public sealed class Tile : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Tile, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<Tile, string?>(nameof(ActionText));

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<Tile, ICommand?>(nameof(ActionCommand));

    public static readonly StyledProperty<bool> IsFeaturedProperty =
        AvaloniaProperty.Register<Tile, bool>(nameof(IsFeatured));

    public static readonly StyledProperty<object?> HeaderExtraProperty =
        AvaloniaProperty.Register<Tile, object?>(nameof(HeaderExtra));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public bool IsFeatured
    {
        get => GetValue(IsFeaturedProperty);
        set => SetValue(IsFeaturedProperty, value);
    }

    /// <summary>Anything shown at the right of the title row (a count, a chip).</summary>
    public object? HeaderExtra
    {
        get => GetValue(HeaderExtraProperty);
        set => SetValue(HeaderExtraProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFeaturedProperty)
        {
            PseudoClasses.Set(":featured", IsFeatured);
        }
        else if (change.Property == ActionTextProperty || change.Property == TitleProperty)
        {
            PseudoClasses.Set(":has-action", !string.IsNullOrEmpty(ActionText));
        }
    }
}

/// <summary>
/// A number that counts up to its value the first time it is shown (spec 01 section 10, 400ms); under
/// reduced motion, and whenever the value is not a plain number, it shows the final text at once.
/// </summary>
public sealed class CountUpText : TextBlock
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<CountUpText, string?>(nameof(Value));

    private DispatcherTimer? _timer;

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            Start(change.GetOldValue<string?>() is null && VisualRoot is not null);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Start(animate: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void Start(bool animate)
    {
        _timer?.Stop();
        var final = Value ?? string.Empty;
        if (!animate || UiServices.ReducedMotion || !TryParse(final, out var prefix, out var target, out var decimals, out var suffix))
        {
            Text = final;
            return;
        }

        var started = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var progress = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 400.0);
            var eased = 1 - Math.Pow(1 - progress, 3);
            Text = progress >= 1 ? final : prefix + (target * eased).ToString("F" + decimals, CultureInfo.InvariantCulture) + suffix;
            if (progress >= 1)
            {
                _timer?.Stop();
            }
        });
        Text = prefix + 0.ToString("F" + decimals, CultureInfo.InvariantCulture) + suffix;
        _timer.Start();
    }

    /// <summary>Splits "$141.2M" into "$", 141.2, 1 decimal, "M"; false for anything else.</summary>
    internal static bool TryParse(string text, out string prefix, out double value, out int decimals, out string suffix)
    {
        prefix = suffix = string.Empty;
        value = 0;
        decimals = 0;
        var start = text.IndexOfAny("0123456789".ToCharArray());
        if (start < 0)
        {
            return false;
        }

        var end = start;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        var number = text[start..end];
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || text[..start].Any(char.IsDigit))
        {
            return false;
        }

        prefix = text[..start];
        suffix = text[end..];
        decimals = number.Contains('.', StringComparison.Ordinal) ? number.Length - number.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
        return true;
    }
}

/// <summary>Up, down, or level, for deltas (▲/▼ plus colour, never colour alone).</summary>
public enum DeltaDirection
{
    None,
    Up,
    Down,
}

/// <summary>A big number with its caps label and an optional delta (vs league average, or a rank).</summary>
public sealed class StatTile : TemplatedControl
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Value));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Label));

    public static readonly StyledProperty<string?> DeltaProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Delta));

    public static readonly StyledProperty<DeltaDirection> DirectionProperty =
        AvaloniaProperty.Register<StatTile, DeltaDirection>(nameof(Direction));

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Delta
    {
        get => GetValue(DeltaProperty);
        set => SetValue(DeltaProperty, value);
    }

    public DeltaDirection Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    /// <summary>The delta with its arrow, so direction never rests on colour alone.</summary>
    public string DeltaText => Direction switch
    {
        DeltaDirection.Up => "▲ " + Delta,
        DeltaDirection.Down => "▼ " + Delta,
        _ => Delta ?? string.Empty,
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DirectionProperty || change.Property == DeltaProperty)
        {
            PseudoClasses.Set(":up", Direction == DeltaDirection.Up);
            PseudoClasses.Set(":down", Direction == DeltaDirection.Down);
            PseudoClasses.Set(":has-delta", !string.IsNullOrEmpty(Delta));
            RaisePropertyChanged(DeltaTextProperty, string.Empty, DeltaText);
        }
    }

    public static readonly DirectProperty<StatTile, string> DeltaTextProperty =
        AvaloniaProperty.RegisterDirect<StatTile, string>(nameof(DeltaText), tile => tile.DeltaText);
}

/// <summary>Badge sizes S / M / L / XL.</summary>
public enum BadgeSize
{
    Small,
    Medium,
    Large,
    ExtraLarge,
}

/// <summary>
/// A rating chip in its tier colour (spec 01 section 12): the number is always shown, the tier name is
/// the tooltip, chip text is pure black or white. Follows the colour-vision palette live.
/// </summary>
public sealed class RatingBadge : TemplatedControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<RatingBadge, int>(nameof(Value));

    public static readonly StyledProperty<BadgeSize> SizeProperty =
        AvaloniaProperty.Register<RatingBadge, BadgeSize>(nameof(Size), BadgeSize.Medium);

    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<RatingBadge, string?>(nameof(Caption));

    private IDisposable? _fill;
    private IDisposable? _ink;

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public BadgeSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>OVR / POT under the number at XL.</summary>
    public string? Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string TierName => RatingTiers.For(Value).Name;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindTier();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _fill?.Dispose();
        _ink?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            BindTier();
        }
        else if (change.Property == SizeProperty)
        {
            PseudoClasses.Set(":small", Size == BadgeSize.Small);
            PseudoClasses.Set(":large", Size == BadgeSize.Large);
            PseudoClasses.Set(":xl", Size == BadgeSize.ExtraLarge);
        }
    }

    private void BindTier()
    {
        var tier = RatingTiers.For(Value);
        ToolTip.SetTip(this, $"{tier.Name} · {Value}");
        global::Avalonia.Automation.AutomationProperties.SetName(this, $"{Caption ?? "Rating"} {Value}, {tier.Name}");
        _fill?.Dispose();
        _ink?.Dispose();
        _fill = Bind(BackgroundProperty, this.GetResourceObservable(tier.BrushKey));
        _ink = Bind(ForegroundProperty, this.GetResourceObservable(tier.OnBrushKey));
    }
}

/// <summary>
/// One attribute (spec 01 section 12): label, a 4px tier-coloured bar on a Surface3 track, the value
/// right-aligned, an optional delta, and in compare mode a tick for the second player.
/// </summary>
public sealed class AttributeBar : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<AttributeBar, string?>(nameof(Label));

    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<AttributeBar, int>(nameof(Value));

    public static readonly StyledProperty<int?> CompareValueProperty =
        AvaloniaProperty.Register<AttributeBar, int?>(nameof(CompareValue));

    public static readonly StyledProperty<int?> DeltaProperty =
        AvaloniaProperty.Register<AttributeBar, int?>(nameof(Delta));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int? CompareValue
    {
        get => GetValue(CompareValueProperty);
        set => SetValue(CompareValueProperty, value);
    }

    public int? Delta
    {
        get => GetValue(DeltaProperty);
        set => SetValue(DeltaProperty, value);
    }
}

/// <summary>The 4px bar inside <see cref="AttributeBar"/>: track, tier fill, compare tick. Drawn, not laid out.</summary>
public sealed class RatingTrack : Control
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<RatingTrack, int>(nameof(Value));

    public static readonly StyledProperty<int?> CompareValueProperty =
        AvaloniaProperty.Register<RatingTrack, int?>(nameof(CompareValue));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<RatingTrack, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> TickProperty =
        AvaloniaProperty.Register<RatingTrack, IBrush?>(nameof(Tick));

    static RatingTrack()
    {
        AffectsRender<RatingTrack>(ValueProperty, CompareValueProperty, TrackProperty, TickProperty);
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int? CompareValue
    {
        get => GetValue(CompareValueProperty);
        set => SetValue(CompareValueProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public IBrush? Tick
    {
        get => GetValue(TickProperty);
        set => SetValue(TickProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 4);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var top = (Bounds.Height - 4) / 2;
        context.FillRectangle(Track ?? Brushes.Transparent, new Rect(0, top, width, 4));
        var fill = this.FindResource(RatingTiers.For(Value).BrushKey) as IBrush;
        context.FillRectangle(fill ?? Brushes.Gray, new Rect(0, top, width * Math.Clamp(Value, 0, 100) / 100.0, 4));
        if (CompareValue is { } compare && Tick is { } tick)
        {
            var x = width * Math.Clamp(compare, 0, 100) / 100.0;
            context.FillRectangle(tick, new Rect(x - 1, top - 3, 2, 10));
        }
    }
}

/// <summary>Kinds of status a <see cref="StatusChip"/> carries; each has a colour and is always spelled out.</summary>
public enum StatusKind
{
    Neutral,
    Positive,
    Negative,
    Warning,
    Info,
    Accent,
}

/// <summary>Status as colour plus text, never colour alone: OUT · 2-3 WEEKS, EXPIRING, ROOKIE SCALE.</summary>
public sealed class StatusChip : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusChip, string?>(nameof(Text));

    public static readonly StyledProperty<StatusKind> KindProperty =
        AvaloniaProperty.Register<StatusChip, StatusKind>(nameof(Kind));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            foreach (var kind in Enum.GetValues<StatusKind>())
            {
                PseudoClasses.Set(":" + kind.ToString().ToLowerInvariant(), kind == Kind);
            }
        }
        else if (change.Property == TextProperty)
        {
            ToolTip.SetTip(this, Text);
        }
    }
}

/// <summary>A position code (PG, SF/PF) on a neutral fill.</summary>
public sealed class PositionChip : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PositionChip, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>Headshot sizes (spec 01 section 11).</summary>
public enum HeadshotSize
{
    ExtraSmall,
    Small,
    Medium,
    Large,
    ExtraLarge,
    Hero,
}

/// <summary>
/// A player's photo in a fixed frame, resolver-backed: fallback first, the real image when it arrives,
/// injury dot, optional jersey number tag. XS and S crop to a circle.
/// </summary>
public sealed class PlayerHeadshot : TemplatedControl
{
    public static readonly StyledProperty<string?> PlayerIdProperty =
        AvaloniaProperty.Register<PlayerHeadshot, string?>(nameof(PlayerId));

    public static readonly StyledProperty<string?> PlayerNameProperty =
        AvaloniaProperty.Register<PlayerHeadshot, string?>(nameof(PlayerName));

    public static readonly StyledProperty<HeadshotSize> SizeProperty =
        AvaloniaProperty.Register<PlayerHeadshot, HeadshotSize>(nameof(Size), HeadshotSize.Medium);

    public static readonly StyledProperty<bool> IsInjuredProperty =
        AvaloniaProperty.Register<PlayerHeadshot, bool>(nameof(IsInjured));

    public static readonly StyledProperty<string?> NumberProperty =
        AvaloniaProperty.Register<PlayerHeadshot, string?>(nameof(Number));

    public PlayerHeadshot()
    {
        ApplySize();
    }

    public string? PlayerId
    {
        get => GetValue(PlayerIdProperty);
        set => SetValue(PlayerIdProperty, value);
    }

    public string? PlayerName
    {
        get => GetValue(PlayerNameProperty);
        set => SetValue(PlayerNameProperty, value);
    }

    public HeadshotSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool IsInjured
    {
        get => GetValue(IsInjuredProperty);
        set => SetValue(IsInjuredProperty, value);
    }

    public string? Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public bool IsCircle => Size is HeadshotSize.ExtraSmall or HeadshotSize.Small;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty)
        {
            ApplySize();
        }
        else if (change.Property == PlayerNameProperty)
        {
            global::Avalonia.Automation.AutomationProperties.SetName(this, PlayerName is null ? "Player photo" : $"Photo of {PlayerName}");
        }
        else if (change.Property == IsInjuredProperty)
        {
            PseudoClasses.Set(":injured", IsInjured);
        }
        else if (change.Property == NumberProperty)
        {
            PseudoClasses.Set(":numbered", !string.IsNullOrEmpty(Number));
        }
    }

    private void ApplySize()
    {
        var (width, height) = Size switch
        {
            HeadshotSize.ExtraSmall => (24, 24),
            HeadshotSize.Small => (32, 32),
            HeadshotSize.Medium => (48, 48),
            HeadshotSize.Large => (96, 96),
            HeadshotSize.ExtraLarge => (160, 160),
            _ => (300, 360),
        };
        Width = width;
        Height = height;
        PseudoClasses.Set(":circle", IsCircle);
        PseudoClasses.Set(":large", Size >= HeadshotSize.Large);
    }
}

/// <summary>A team's logo, resolver-backed, with the monogram plate as its fallback.</summary>
public sealed class TeamLogo : TemplatedControl
{
    public static readonly StyledProperty<string?> TeamIdProperty =
        AvaloniaProperty.Register<TeamLogo, string?>(nameof(TeamId));

    public static readonly StyledProperty<string?> TeamNameProperty =
        AvaloniaProperty.Register<TeamLogo, string?>(nameof(TeamName));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<TeamLogo, double>(nameof(Size), 32);

    public TeamLogo()
    {
        Width = Height = Size;
    }

    public string? TeamId
    {
        get => GetValue(TeamIdProperty);
        set => SetValue(TeamIdProperty, value);
    }

    public string? TeamName
    {
        get => GetValue(TeamNameProperty);
        set => SetValue(TeamNameProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty)
        {
            Width = Height = Size;
        }
        else if (change.Property == TeamNameProperty)
        {
            global::Avalonia.Automation.AutomationProperties.SetName(this, TeamName is null ? "Team logo" : $"{TeamName} logo");
        }
    }
}

/// <summary>A label and its value, one row of a <see cref="KeyValueList"/>.</summary>
public sealed record KeyValueItem(string Key, string Value);

/// <summary>Bio facts: caps label over body value, in columns.</summary>
public sealed class KeyValueList : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<KeyValueItem>?> ItemsProperty =
        AvaloniaProperty.Register<KeyValueList, IReadOnlyList<KeyValueItem>?>(nameof(Items));

    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<KeyValueList, int>(nameof(Columns), 2);

    public IReadOnlyList<KeyValueItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }
}

/// <summary>Resolver keys for templates: what an <see cref="AssetImage"/> inside a headshot or logo shows.</summary>
public static class AssetKinds
{
    public static AssetEntityKind Player => AssetEntityKind.Player;

    public static AssetEntityKind Team => AssetEntityKind.Team;
}
