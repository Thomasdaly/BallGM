namespace BallGM.Mods.Manifests;

public sealed record DataPackManifest
{
    public const int CurrentSchemaVersion = 1;

    public DataPackManifest(int schemaVersion, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version must be positive.");
        }

        SchemaVersion = schemaVersion;
        Name = name;
    }

    public int SchemaVersion { get; }

    public string Name { get; }
}
