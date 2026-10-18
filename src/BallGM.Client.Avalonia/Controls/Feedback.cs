using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// What appears where data will (spec 03 section 8): an icon, a headline, one line saying what shows up
/// here and how to get it, and at most one action ("Clear filters").
/// </summary>
public sealed class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(IconKey), "Icon.Info");

    public static readonly StyledProperty<string?> HeadlineProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Headline));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(ActionCommand));

    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string? Headline
    {
        get => GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActionTextProperty)
        {
            PseudoClasses.Set(":has-action", !string.IsNullOrEmpty(ActionText));
        }
    }
}

/// <summary>A plain-language failure with Retry and Copy details (spec 03 section 8).</summary>
public sealed class ErrorState : TemplatedControl
{
    public static readonly StyledProperty<string?> HeadlineProperty =
        AvaloniaProperty.Register<ErrorState, string?>(nameof(Headline), "Something went wrong");

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ErrorState, string?>(nameof(Message));

    public static readonly StyledProperty<string?> DetailsProperty =
        AvaloniaProperty.Register<ErrorState, string?>(nameof(Details));

    public static readonly StyledProperty<ICommand?> RetryCommandProperty =
        AvaloniaProperty.Register<ErrorState, ICommand?>(nameof(RetryCommand));

    public string? Headline
    {
        get => GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Copy") is { } copy)
        {
            copy.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync($"{Headline}\n{Message}\n{Details}");
                }
            };
        }
    }
}

/// <summary>A season-phase or deadline strip under the page header: TRADE DEADLINE IN 3 DAYS.</summary>
public sealed class Banner : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(Text));

    public static readonly StyledProperty<StatusKind> KindProperty =
        AvaloniaProperty.Register<Banner, StatusKind>(nameof(Kind), StatusKind.Info);

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
            PseudoClasses.Set(":warning", Kind == StatusKind.Warning);
            PseudoClasses.Set(":negative", Kind == StatusKind.Negative);
        }
    }
}

/// <summary>Skeleton shapes that match the final layout.</summary>
public enum SkeletonShape
{
    Text,
    Row,
    Card,
    Headshot,
}

/// <summary>A loading placeholder in the shape of what is coming; the shimmer stops under reduced motion.</summary>
public sealed class Skeleton : TemplatedControl
{
    public static readonly StyledProperty<SkeletonShape> ShapeProperty =
        AvaloniaProperty.Register<Skeleton, SkeletonShape>(nameof(Shape));

    public static readonly StyledProperty<int> LinesProperty =
        AvaloniaProperty.Register<Skeleton, int>(nameof(Lines), 3);

    public Skeleton()
    {
        PseudoClasses.Set(":text", true);
    }

    public SkeletonShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public int Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public static readonly DirectProperty<Skeleton, IReadOnlyList<int>> LineIndexesProperty =
        AvaloniaProperty.RegisterDirect<Skeleton, IReadOnlyList<int>>(nameof(LineIndexes), skeleton => skeleton.LineIndexes);

    public IReadOnlyList<int> LineIndexes => Enumerable.Range(0, Math.Max(1, Lines)).ToList();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LinesProperty)
        {
            RaisePropertyChanged(LineIndexesProperty, [], LineIndexes);
        }

        if (change.Property == ShapeProperty)
        {
            foreach (var shape in Enum.GetValues<SkeletonShape>())
            {
                PseudoClasses.Set(":" + shape.ToString().ToLowerInvariant(), shape == Shape);
            }
        }
    }
}

/// <summary>
/// Progress over the page area while a long operation runs (spec 03 section 8): determinate or not,
/// the current step, and Stop when the operation can stop.
/// </summary>
public sealed class ProgressOverlay : TemplatedControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ProgressOverlay, bool>(nameof(IsActive));

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<ProgressOverlay, bool>(nameof(IsIndeterminate), true);

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ProgressOverlay, double>(nameof(Progress));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ProgressOverlay, string?>(nameof(Title));

    public static readonly StyledProperty<string?> StepTextProperty =
        AvaloniaProperty.Register<ProgressOverlay, string?>(nameof(StepText));

    public static readonly StyledProperty<ICommand?> StopCommandProperty =
        AvaloniaProperty.Register<ProgressOverlay, ICommand?>(nameof(StopCommand));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? StepText
    {
        get => GetValue(StepTextProperty);
        set => SetValue(StepTextProperty, value);
    }

    public ICommand? StopCommand
    {
        get => GetValue(StopCommandProperty);
        set => SetValue(StopCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            IsVisible = IsActive;
        }
        else if (change.Property == StopCommandProperty)
        {
            PseudoClasses.Set(":can-stop", StopCommand is not null);
        }
    }
}

/// <summary>Toast kinds: info, success, warning, error (errors stay until dismissed).</summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>One notification for the toast stack.</summary>
public sealed class ToastMessage
{
    public ToastMessage(ToastKind kind, string title, string? message = null, string? actionText = null, ICommand? actionCommand = null)
    {
        Kind = kind;
        Title = title;
        Message = message;
        ActionText = actionText;
        ActionCommand = actionCommand;
    }

    public ToastKind Kind { get; }

    public string Title { get; }

    public string? Message { get; }

    public string? ActionText { get; }

    public ICommand? ActionCommand { get; }

    public bool HasAction => ActionText is not null && ActionCommand is not null;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string IconKey => Kind switch
    {
        ToastKind.Success => "Icon.Success",
        ToastKind.Warning => "Icon.Warning",
        ToastKind.Error => "Icon.Error",
        _ => "Icon.Info",
    };

    public bool IsInfo => Kind == ToastKind.Info;

    public bool IsSuccess => Kind == ToastKind.Success;

    public bool IsWarning => Kind == ToastKind.Warning;

    public bool IsError => Kind == ToastKind.Error;
}

/// <summary>
/// The toast stack (top-right, under the header): information that needs no decision. Toasts dismiss
/// after 5 seconds, except errors, which stay; each can be dismissed by keyboard.
/// </summary>
public sealed class ToastService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    public ObservableCollection<ToastMessage> Toasts { get; } = [];

    public ICommand DismissCommand => new ParameterCommandShim(toast => Dismiss((ToastMessage)toast!));

    public void Show(ToastMessage toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        Toasts.Insert(0, toast);
        while (Toasts.Count > 4)
        {
            Toasts.RemoveAt(Toasts.Count - 1);
        }

        if (toast.Kind != ToastKind.Error && Dispatcher.UIThread.CheckAccess())
        {
            DispatcherTimer.RunOnce(() => Dismiss(toast), Lifetime);
        }
    }

    public void Dismiss(ToastMessage toast) => Toasts.Remove(toast);

    /// <summary>A command over an object parameter, so the toast template binds without a view model.</summary>
    private sealed class ParameterCommandShim(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is not null;

        public void Execute(object? parameter) => execute(parameter);
    }
}

/// <summary>The toast stack: newest first, each with a dismiss button bound to <see cref="DismissCommand"/>.</summary>
public sealed class ToastHost : ItemsControl
{
    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<ToastHost, ICommand?>(nameof(DismissCommand));

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }
}

/// <summary>A dialog request: confirm, destructive confirm, or a form (custom content).</summary>
public sealed class DialogRequest
{
    private readonly TaskCompletionSource<bool> _result = new();

    public DialogRequest(string title, string? message, string confirmText, string? cancelText = "Cancel", bool isDestructive = false, object? content = null)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDestructive = isDestructive;
        Content = content;
    }

    public string Title { get; }

    public string? Message { get; }

    public string ConfirmText { get; }

    public string? CancelText { get; }

    public bool IsDestructive { get; }

    public object? Content { get; }

    public bool HasCancel => CancelText is not null;

    public Task<bool> Result => _result.Task;

    internal void Complete(bool confirmed) => _result.TrySetResult(confirmed);
}

/// <summary>
/// Hosts one modal dialog over its content (spec 03 section 8): a scrim, the card, Esc cancels, Enter
/// confirms the default, Tab cycles inside the card only, and focus returns where it was on close.
/// </summary>
public sealed class DialogHost : ContentControl
{
    public static readonly StyledProperty<DialogRequest?> RequestProperty =
        AvaloniaProperty.Register<DialogHost, DialogRequest?>(nameof(Request));

    private IInputElement? _focusBefore;
    private Button? _confirm;

    public DialogRequest? Request
    {
        get => GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    public bool IsOpen => Request is not null;

    public Task<bool> Show(DialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _focusBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        Request = request;
        return request.Result;
    }

    public void Close(bool confirmed)
    {
        var request = Request;
        Request = null;
        request?.Complete(confirmed);
        _focusBefore?.Focus();
        _focusBefore = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RequestProperty)
        {
            PseudoClasses.Set(":open", Request is not null);
            PseudoClasses.Set(":destructive", Request?.IsDestructive == true);
            if (_confirm is not null)
            {
                _confirm.Classes.Set("danger", Request?.IsDestructive == true);
                _confirm.Classes.Set("primary", Request?.IsDestructive != true);
            }

            if (Request is not null)
            {
                Dispatcher.UIThread.Post(() => _confirm?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _confirm = e.NameScope.Find<Button>("PART_Confirm");
        if (_confirm is not null)
        {
            _confirm.Click += (_, _) => Close(true);
        }

        if (e.NameScope.Find<Button>("PART_Cancel") is { } cancel)
        {
            cancel.Click += (_, _) => Close(false);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Request is not null)
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && e.Source is not TextBox)
            {
                Close(true);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }
}

/// <summary>
/// The branded crash screen (spec 02 section 14): what happened in plain words, where the log is, and
/// Copy details / Open logs folder / Quit.
/// </summary>
public sealed class CrashDialog : TemplatedControl
{
    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<CrashDialog, string?>(nameof(Summary));

    public static readonly StyledProperty<string?> DetailsProperty =
        AvaloniaProperty.Register<CrashDialog, string?>(nameof(Details));

    public static readonly StyledProperty<string?> LogPathProperty =
        AvaloniaProperty.Register<CrashDialog, string?>(nameof(LogPath));

    public static readonly StyledProperty<ICommand?> CopyDetailsCommandProperty =
        AvaloniaProperty.Register<CrashDialog, ICommand?>(nameof(CopyDetailsCommand));

    public static readonly StyledProperty<ICommand?> OpenLogsCommandProperty =
        AvaloniaProperty.Register<CrashDialog, ICommand?>(nameof(OpenLogsCommand));

    public static readonly StyledProperty<ICommand?> QuitCommandProperty =
        AvaloniaProperty.Register<CrashDialog, ICommand?>(nameof(QuitCommand));

    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public string? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public string? LogPath
    {
        get => GetValue(LogPathProperty);
        set => SetValue(LogPathProperty, value);
    }

    public ICommand? CopyDetailsCommand
    {
        get => GetValue(CopyDetailsCommandProperty);
        set => SetValue(CopyDetailsCommandProperty, value);
    }

    public ICommand? OpenLogsCommand
    {
        get => GetValue(OpenLogsCommandProperty);
        set => SetValue(OpenLogsCommandProperty, value);
    }

    public ICommand? QuitCommand
    {
        get => GetValue(QuitCommandProperty);
        set => SetValue(QuitCommandProperty, value);
    }
}
