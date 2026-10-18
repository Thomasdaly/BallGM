using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>The shell's Continue states (spec 04 section 4).</summary>
public enum ContinueState
{
    Ready,
    Blocked,
    GameDay,
    Simulating,
}

/// <summary>One "sim to" option the game already supports, for the Continue dropdown.</summary>
public sealed record ContinueOption(string Label, ICommand Command, object? Parameter = null);

/// <summary>
/// The shell's main call to action: label plus next-event subtitle on a skewed accent plate, a dropdown
/// of the sim options that exist, and a state (ready, blocked, game day, simulating) that changes its
/// look. What it does in each state is the shell's decision, not the button's.
/// </summary>
public sealed class ContinueButton : TemplatedControl
{
    public static readonly StyledProperty<ContinueState> StateProperty =
        AvaloniaProperty.Register<ContinueButton, ContinueState>(nameof(State));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ContinueButton, string>(nameof(Label), "CONTINUE");

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<ContinueButton, string?>(nameof(Subtitle));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ContinueButton, ICommand?>(nameof(Command));

    public static readonly StyledProperty<IReadOnlyList<ContinueOption>?> OptionsProperty =
        AvaloniaProperty.Register<ContinueButton, IReadOnlyList<ContinueOption>?>(nameof(Options));

    static ContinueButton()
    {
        FocusableProperty.OverrideDefaultValue<ContinueButton>(false);
    }

    public ContinueButton()
    {
        UpdateState();
    }

    public ContinueState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public IReadOnlyList<ContinueOption>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            UpdateState();
        }
        else if (change.Property == OptionsProperty)
        {
            PseudoClasses.Set(":has-options", Options is { Count: > 0 });
            BuildMenu();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _menuButton = e.NameScope.Find<Button>("PART_Menu");
        BuildMenu();
    }

    private Button? _menuButton;

    private void BuildMenu()
    {
        if (_menuButton is null)
        {
            return;
        }

        var options = Options ?? [];
        _menuButton.IsVisible = options.Count > 0;
        _menuButton.Flyout = options.Count == 0
            ? null
            : new MenuFlyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                ItemsSource = options.Select(option => new MenuItem { Header = option.Label, Command = option.Command, CommandParameter = option.Parameter }).ToList(),
            };
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":ready", State == ContinueState.Ready);
        PseudoClasses.Set(":blocked", State == ContinueState.Blocked);
        PseudoClasses.Set(":gameday", State == ContinueState.GameDay);
        PseudoClasses.Set(":simulating", State == ContinueState.Simulating);
    }
}

/// <summary>What an <see cref="EntityLink"/> names.</summary>
public enum EntityKind
{
    Player,
    Team,
    Staff,
    Competition,
}

/// <summary>
/// Any entity name in the interface (spec 03 section 4): click opens its profile, hovering shows its
/// hover card after 400ms (set <c>ToolTip.Tip</c> to the card), and right-click or Shift+F10 opens the
/// actions that exist (set <c>ContextMenu</c>). Long names trim with the full name as a tooltip when
/// no hover card is given.
/// </summary>
public sealed class EntityLink : Button
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EntityLink, string?>(nameof(Text));

    public static readonly StyledProperty<EntityKind> KindProperty =
        AvaloniaProperty.Register<EntityLink, EntityKind>(nameof(Kind));

    public static readonly StyledProperty<string?> EntityIdProperty =
        AvaloniaProperty.Register<EntityLink, string?>(nameof(EntityId));

    protected override Type StyleKeyOverride => typeof(EntityLink);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public EntityKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? EntityId
    {
        get => GetValue(EntityIdProperty);
        set => SetValue(EntityIdProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            global::Avalonia.Automation.AutomationProperties.SetName(this, Text);
            if (ToolTip.GetTip(this) is null or string)
            {
                // The full name, for when the link is trimmed; a hover card replaces it.
                ToolTip.SetTip(this, Text);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Shift+F10 is the keyboard's right click: open the entity's actions.
        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift && ContextMenu is { } menu)
        {
            menu.Open(this);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
