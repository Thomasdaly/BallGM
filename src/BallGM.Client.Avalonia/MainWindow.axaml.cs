using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.Formatting;
using BallGM.Client.Avalonia.Settings;
using BallGM.Client.Avalonia.Theming;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia;

public sealed partial class MainWindow : Window
{
    /// <summary>Below this effective width the rail collapses and the header compacts (spec 04 section 7).</summary>
    public const double CompactWidth = 1440;

    private MainWindowViewModel? _viewModel;
    private bool _railCollapsedByUser;
    private bool? _railChoice;
    private bool _loaded;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _loaded = true;
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerButtons, RoutingStrategies.Tunnel);
        if (this.FindControl<ListBox>("SearchList") is { } results)
        {
            results.Tapped += (_, _) =>
            {
                if (results.SelectedItem is SearchResult result)
                {
                    result.Open.Execute(result);
                }
            };
        }
    }

    /// <summary>Whether the nav rail is showing icons only.</summary>
    public bool IsRailCollapsed => this.FindControl<NavRail>("Nav")?.IsCollapsed == true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTeamTheme();
            ApplySettings(_viewModel.Settings);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty && _loaded)
        {
            ApplyResponsive();
        }
    }

    /// <summary>Applies the interface options: scale, density, motion, colours, units, ticker, IDs.</summary>
    public void ApplySettings(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var scale = settings.UiScalePercent / 100.0;
        if (this.FindControl<LayoutTransformControl>("ScaleHost") is { } host)
        {
            host.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);
        }

        UiServices.ReducedMotion = settings.ReducedMotion;
        UiServices.ShowEntityIds = settings.ShowEntityIds;
        Classes.Set("reduced-motion", settings.ReducedMotion);
        Classes.Set("compact-density", settings.Density == Density.Compact);
        if (this.FindControl<TransitioningContentControl>("Screen") is { } screen)
        {
            screen.PageTransition = settings.ReducedMotion ? null : new PageFadeSlide();
        }

        Fmt.Units = settings.Units;
        Fmt.DatePattern = settings.DatePattern;
        ThemeResources.TeamColoursEnabled = settings.TeamColours;
        if (global::Avalonia.Application.Current is { } application)
        {
            ThemeResources.ApplyRatingPalette(application.Resources, settings.RatingPalette == RatingPalette.ColourVisionSafe);
            var compact = settings.Density == Density.Compact;
            application.Resources["RowHeight"] = compact ? 28.0 : 40.0;
            application.Resources["ListItemHeight"] = compact ? 44.0 : 56.0;
        }

        _railChoice = settings.NavCollapsed;
        ApplyTeamTheme();
        ApplyResponsive();
        UiServices.RaisePreferencesChanged();
    }

    private void ApplyTeamTheme()
    {
        var colours = _viewModel?.SelectedTeam?.Colours;
        ThemeResources.ApplyUserTeam(TeamTheme.From(colours?.Primary, colours?.Secondary));
    }

    /// <summary>Effective width (window width over UI scale) drives the compact header and the rail.</summary>
    private void ApplyResponsive()
    {
        var scale = (_viewModel?.Settings.UiScalePercent ?? 100) / 100.0;
        var width = ClientSize.Width / scale;
        var compact = width < CompactWidth;
        SetRail(_railCollapsedByUser ? IsRailCollapsed : _railChoice ?? compact);
        if (this.FindControl<Breadcrumb>("Crumbs") is { } crumbs)
        {
            crumbs.IsVisible = !compact;
        }

        if (this.FindControl<TextBlock>("SearchLabel") is { } label && this.FindControl<TextBlock>("SearchHint") is { } hint)
        {
            label.IsVisible = !compact;
            hint.IsVisible = !compact;
        }

        if (this.FindControl<DockPanel>("Page") is { } page)
        {
            page.Margin = (Thickness)(this.FindResource(compact ? "PagePaddingCompact" : "PagePadding") ?? default(Thickness));
        }
    }

    private void SetRail(bool collapsed)
    {
        if (this.FindControl<NavRail>("Nav") is { } nav && this.FindControl<Border>("Rail") is { } rail)
        {
            nav.IsCollapsed = collapsed;
            rail.Width = (double)(this.FindResource(collapsed ? "NavRailCollapsedWidth" : "NavRailExpandedWidth") ?? 220.0);
        }
    }

    private void OnToggleRail(object? sender, RoutedEventArgs e)
    {
        _railCollapsedByUser = true;
        SetRail(!IsRailCollapsed);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The accent follows the user's team: switching teams repaints the whole client.
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTeam))
        {
            ApplyTeamTheme();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsSearchOpen) && _viewModel?.IsSearchOpen == true)
        {
            Dispatcher.UIThread.Post(() => this.FindControl<SearchBox>("SearchInput")?.Focus(), DispatcherPriority.Loaded);
        }
    }

    private void OnPointerButtons(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsXButton1Pressed)
        {
            _viewModel?.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (properties.IsXButton2Pressed)
        {
            _viewModel?.ForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The keyboard map of spec 04 section 3. Plain keys never fire while a text field has focus.</summary>
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var inText = FocusManager?.GetFocusedElement() is TextBox;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (_viewModel.IsSearchOpen)
        {
            HandleSearchKeys(e);
            return;
        }

        if (ctrl && e.Key == Key.K || ctrl && e.Key == Key.F && !inText)
        {
            _viewModel.IsSearchOpen = true;
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Left)
        {
            _viewModel.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Right)
        {
            _viewModel.ForwardCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Enter)
        {
            _viewModel.ContinueCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < _viewModel.NavEntries.Count)
            {
                _viewModel.SelectedSection = _viewModel.NavEntries[index].Key;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
        }
    }

    private void HandleSearchKeys(KeyEventArgs e)
    {
        if (_viewModel is null || this.FindControl<ListBox>("SearchList") is not { } list)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.IsSearchOpen = false;
                e.Handled = true;
                break;
            case Key.Down:
                list.SelectedIndex = Math.Min(list.ItemCount - 1, list.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter:
                var result = list.SelectedItem as SearchResult ?? _viewModel.SearchResults.FirstOrDefault();
                if (result is not null)
                {
                    result.Open.Execute(result);
                }

                e.Handled = true;
                break;
        }
    }
}
