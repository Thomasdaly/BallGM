using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BallGM.Client.Avalonia.Theming;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

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
            TeamPaint.Apply(_viewModel.SelectedTeam?.Colours, _viewModel.SelectedTeam?.LogoPath);
        }
    }

    // The accent follows the team being viewed: switching teams repaints the whole client.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTeam))
        {
            TeamPaint.Apply(_viewModel?.SelectedTeam?.Colours, _viewModel?.SelectedTeam?.LogoPath);
        }
    }
}
