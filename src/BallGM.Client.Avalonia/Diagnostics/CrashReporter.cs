using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Diagnostics;

/// <summary>
/// Global crash handling (spec 02 section 14): unhandled exceptions from any thread, unobserved tasks
/// and the UI dispatcher are logged and shown in the branded crash dialog with Copy details, Open logs
/// folder and Quit.
/// </summary>
public static class CrashReporter
{
    private static bool _installed;
    private static bool _showing;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            UiLog.Error("Unobserved task exception: " + args.Exception);
            args.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Report(args.Exception);
            args.Handled = true;
        };
    }

    public static void Report(Exception? exception)
    {
        UiLog.Error("Unhandled exception: " + exception);
        if (_showing || !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Report(exception));
            return;
        }

        _showing = true;
        CreateWindow(exception).Show();
    }

    /// <summary>The crash dialog in its own window, so it shows whatever state the main window is in.</summary>
    public static Window CreateWindow(Exception? exception)
    {
        var details = exception?.ToString() ?? "No details were recorded.";
        Window? window = null;
        var dialog = new CrashDialog
        {
            Summary = "Something unexpected stopped the game. Your last save is safe; anything since it may be lost.",
            Details = details,
            LogPath = UiLog.CurrentFile ?? "No log folder could be written.",
            CopyDetailsCommand = new RelayCommand(() => _ = CopyAsync(window, details)),
            OpenLogsCommand = new RelayCommand(() => OpenFolder(UiLog.Folder)),
            QuitCommand = new RelayCommand(() => Environment.Exit(1)),
        };
        window = new Window { Title = "BallGM", Width = 900, Height = 640, MinWidth = 720, MinHeight = 520, Content = dialog };
        return window;
    }

    private static async Task CopyAsync(TopLevel? top, string text)
    {
        if (top?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public static void OpenFolder(string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            UiLog.Warn("Could not open folder " + folder + ": " + exception.Message);
        }
    }
}
