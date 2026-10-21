using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class SeasonView : UserControl
{
    private SeasonViewModel? _viewModel;

    public SeasonView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = DataContext as SeasonViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    /// <summary>Opening or closing a game is a page change: both pages start at the top.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeasonViewModel.IsSheetOpen))
        {
            this.FindControl<ScrollViewer>("CalendarScroll")?.ScrollToHome();
            this.FindControl<ScrollViewer>("SheetScroll")?.ScrollToHome();
        }
    }
}
