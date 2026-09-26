using Avalonia.Headless.XUnit;

namespace BallGM.Client.Avalonia.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_OpensOnTheFixtureLeagueWithoutADisplay()
    {
        var window = new MainWindow
        {
            DataContext = LeagueClientComposition.CreateMainWindowViewModel(),
        };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal("BallGM", window.Title);
    }
}
