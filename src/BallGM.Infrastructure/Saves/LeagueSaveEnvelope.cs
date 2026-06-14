namespace BallGM.Infrastructure.Saves;

public sealed record LeagueSaveEnvelope
{
    public const int CurrentSchemaVersion = 1;

    public LeagueSaveEnvelope(int schemaVersion, string leagueName, int currentSeasonYear)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueName);

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version must be positive.");
        }

        if (currentSeasonYear <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentSeasonYear), currentSeasonYear, "Season year must be positive.");
        }

        SchemaVersion = schemaVersion;
        LeagueName = leagueName;
        CurrentSeasonYear = currentSeasonYear;
    }

    public int SchemaVersion { get; }

    public string LeagueName { get; }

    public int CurrentSeasonYear { get; }
}
