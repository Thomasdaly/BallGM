using System.Globalization;

namespace BallGM.Client.Avalonia.Diagnostics;

/// <summary>
/// The client's log: one file per day in the data root's logs folder, the last seven kept (spec 02
/// section 14). Thread-safe, never throws - a log that cannot be written is not worth a crash.
/// </summary>
public static class UiLog
{
    private const int KeptFiles = 7;
    private static readonly Lock Gate = new();
    private static string? _folder;

    public static string? Folder => _folder;

    public static string? CurrentFile => _folder is null ? null : Path.Combine(_folder, $"ballgm-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialise(string folder)
    {
        _folder = folder;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "ballgm-*.log").OrderByDescending(path => path, StringComparer.Ordinal).Skip(KeptFiles))
            {
                File.Delete(old);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _folder = null;
        }
    }

    public static void Write(string level, string message)
    {
        if (CurrentFile is not { } file)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(file, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the client down.
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);
}
