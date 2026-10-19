using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BallGM.Client.Avalonia.Diagnostics;
using BallGM.Client.Avalonia.Settings;
using BallGM.Client.Avalonia.Theming;

namespace BallGM.Client.Avalonia;

public sealed partial class App : global::Avalonia.Application
{
    public override void Initialize()
    {
        BallGmFonts.Register();
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = UiPaths.FromEnvironment();
            UiLog.Initialise(paths.LogsFolder);
            CrashReporter.Install();
            UiLog.Info("BallGM client starting");

            if (desktop.Args?.Contains("--gallery") == true)
            {
                // The dev-only Component Gallery (S33), never reachable from release navigation.
                desktop.MainWindow = new Window
                {
                    Title = "BallGM - Component Gallery",
                    Width = 1600,
                    Height = 900,
                    Content = new Views.Gallery.ComponentGalleryView { DataContext = new ViewModels.ComponentGalleryViewModel() },
                };
            }
            else
            {
                desktop.MainWindow = CreateMainWindow(new UiSettingsStore(paths));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The game window with the saved interface options applied and its placement remembered.</summary>
    internal static MainWindow CreateMainWindow(UiSettingsStore store)
    {
        var settings = store.Load();
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();
        viewModel.Settings = settings;
        var window = new MainWindow { DataContext = viewModel };
        WindowPlacementKeeper.Restore(window, settings);
        window.Closing += (_, _) =>
        {
            try
            {
                store.Save(WindowPlacementKeeper.Capture(window, viewModel.Settings));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                UiLog.Warn("Could not save interface settings: " + exception.Message);
            }
        };
        return window;
    }
}
