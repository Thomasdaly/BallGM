using Avalonia;
#if DEBUG
using Zafiro.Avalonia.Mcp.AppHost;
#endif

namespace BallGM.Client.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();
#if DEBUG
        builder = builder.UseMcpDiagnostics();
#endif
        return builder;
    }
}
