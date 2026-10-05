using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = LeagueClientComposition.CreateMainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
