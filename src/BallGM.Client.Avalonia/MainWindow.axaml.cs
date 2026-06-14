using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BallGM.Application.Overview;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var overview = new GetBallGmOverviewQuery().Execute();
        DataContext = new MainWindowViewModel(overview);
    }
}
