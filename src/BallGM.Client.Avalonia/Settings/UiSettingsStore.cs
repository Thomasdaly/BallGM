using System.Text.Json;
using System.Text.Json.Serialization;

namespace BallGM.Client.Avalonia.Settings;

/// <summary>
/// Reads and writes <see cref="UiSettings"/> as versioned JSON in the data root. A missing, unreadable
/// or newer-versioned file yields the defaults rather than an error: interface options are never
/// worth refusing to start over.
/// </summary>
public sealed class UiSettingsStore
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly UiPaths _paths;

    public UiSettingsStore(UiPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    private sealed record Envelope(int SchemaVersion, UiSettings Settings);

    public UiSettings Load()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                return new UiSettings();
            }

            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(_paths.SettingsFile), Options);
            return envelope is { SchemaVersion: SchemaVersion, Settings: not null } ? envelope.Settings.Normalised() : new UiSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new UiSettings();
        }
    }

    public void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_paths.Root);
        var temporary = _paths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Envelope(SchemaVersion, settings.Normalised()), Options));
        File.Move(temporary, _paths.SettingsFile, overwrite: true);
    }
}
