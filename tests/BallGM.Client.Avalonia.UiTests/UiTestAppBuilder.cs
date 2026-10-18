using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(BallGM.Client.Avalonia.UiTests.UiTestAppBuilder))]

namespace BallGM.Client.Avalonia.UiTests;

/// <summary>
/// The real <see cref="App"/> (its themes, fonts and resources) on the headless platform, drawing
/// with Skia rather than the headless stub so <c>CaptureRenderedFrame</c> returns real pixels.
/// </summary>
public static class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
