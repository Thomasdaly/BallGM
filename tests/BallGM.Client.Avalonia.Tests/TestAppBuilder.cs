using Avalonia;
using Avalonia.Headless;
using BallGM.Client.Avalonia;

[assembly: AvaloniaTestApplication(typeof(BallGM.Client.Avalonia.Tests.TestAppBuilder))]

namespace BallGM.Client.Avalonia.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
