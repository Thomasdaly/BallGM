namespace BallGM.Client.Avalonia.Settings;

/// <summary>
/// Where the client keeps its own files: settings, logs, saves and league packs. One root, so tests
/// point it at a temporary folder (<see cref="DataRootEnvironmentVariable"/>) and never touch the
/// real user folders (spec 00 section 5 rule 9).
/// </summary>
public sealed class UiPaths
{
    public const string DataRootEnvironmentVariable = "BALLGM_DATA_ROOT";

    public UiPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "ui-settings.json");

    public string LogsFolder => Path.Combine(Root, "logs");

    public string SavesFolder => Path.Combine(Root, "saves");

    /// <summary>League packs the New career screen offers, one folder per pack holding its pack.json.</summary>
    public string ModsFolder => Path.Combine(Root, "mods");

    /// <summary>The environment's root if set, otherwise the platform's per-user application data folder.</summary>
    public static UiPaths FromEnvironment()
    {
        var stated = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        return new UiPaths(string.IsNullOrWhiteSpace(stated)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "BallGM")
            : stated);
    }
}
