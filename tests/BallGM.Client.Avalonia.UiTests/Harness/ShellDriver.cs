using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>
/// Drives the real shell: the composition root builds the session from the fixture league pack, the
/// season is started and advanced with the calendar screen's own commands to a mid-season state, and
/// the user's team (Harbor City Mariners) is selected the way a player selects it.
/// </summary>
internal sealed class ShellDriver
{
    /// <summary>The user's team in every screenshot (spec 06 section 1.1).</summary>
    public const string UserTeamName = "Harbor City Mariners";

    /// <summary>Season day the fixture is captured on: about 35 games into the 82-game regular season, after 14 preseason days.</summary>
    public const int MidSeasonDay = 49;

    private ShellDriver(MainWindow window, MainWindowViewModel viewModel)
    {
        Window = window;
        ViewModel = viewModel;
    }

    public MainWindow Window { get; }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>Opens the shell on <paramref name="packPath"/> (the UI fixture league by default), mid-season, viewing the user's team.</summary>
    public static ShellDriver Open(int width, int height, string? packPath = null, bool midSeason = true)
    {
        Controls.UiServices.ReducedMotion = true;
        Environment.SetEnvironmentVariable(LeagueClientComposition.LeaguePackEnvironmentVariable, packPath ?? RepoPaths.UiLeaguePack);
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();

        if (viewModel.HasLeague)
        {
            if (midSeason && viewModel.Season is { } season)
            {
                season.StartSeasonCommand.Execute(null);
                season.AdvanceDays = MidSeasonDay;
                season.AdvanceCommand.Execute(null);
            }

            viewModel.SelectedTeam = viewModel.Teams.FirstOrDefault(team => team.TeamName == UserTeamName) ?? viewModel.Teams[0];
        }

        var window = new MainWindow { DataContext = viewModel, Width = width, Height = height };
        window.Show();
        Settle();
        return new ShellDriver(window, viewModel);
    }

    public void Resize(int width, int height)
    {
        Window.Width = width;
        Window.Height = height;
        Settle();
    }

    public void Navigate(string section)
    {
        ViewModel.SelectedSection = section;
        Settle();
    }

    /// <summary>Runs queued dispatcher work and layout until the frame is stable.</summary>
    public static void Settle()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    public WriteableBitmap Capture()
    {
        Settle();
        return Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The headless window rendered no frame.");
    }

    public void Close() => Window.Close();
}
