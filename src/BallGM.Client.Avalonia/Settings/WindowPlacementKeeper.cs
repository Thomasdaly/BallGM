using Avalonia;
using Avalonia.Controls;

namespace BallGM.Client.Avalonia.Settings;

/// <summary>
/// Remembers the window's size, position and state (spec 02 section 12), restoring it safely: a saved
/// position that is now off every screen is dropped and the window opens centred instead.
/// </summary>
public static class WindowPlacementKeeper
{
    public static void Restore(Window window, UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.RememberWindow || settings.Window is not { } placement)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.Width = Math.Max(window.MinWidth, placement.Width);
        window.Height = Math.Max(window.MinHeight, placement.Height);
        var onScreen = window.Screens?.All.Any(screen => screen.WorkingArea.Contains(new PixelPoint(placement.X + 40, placement.Y + 40))) ?? false;
        if (onScreen)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(placement.X, placement.Y);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.WindowState = placement.FullScreen ? WindowState.FullScreen : placement.Maximised ? WindowState.Maximized : WindowState.Normal;
    }

    public static UiSettings Capture(Window window, UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.RememberWindow)
        {
            return settings;
        }

        var normal = window.WindowState == WindowState.Normal;
        var previous = settings.Window;
        return settings with
        {
            Window = new WindowPlacement(
                normal ? window.Position.X : previous?.X ?? window.Position.X,
                normal ? window.Position.Y : previous?.Y ?? window.Position.Y,
                normal ? (int)window.Width : previous?.Width ?? (int)window.Width,
                normal ? (int)window.Height : previous?.Height ?? (int)window.Height,
                window.WindowState == WindowState.Maximized,
                window.WindowState == WindowState.FullScreen),
        };
    }
}
