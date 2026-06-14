using System.Text.Json;

namespace BallGM.Infrastructure.Saves;

public sealed class LeagueSaveSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Serialize(LeagueSaveEnvelope saveEnvelope)
    {
        ArgumentNullException.ThrowIfNull(saveEnvelope);
        Validate(saveEnvelope);

        return JsonSerializer.Serialize(saveEnvelope, Options);
    }

    public LeagueSaveEnvelope Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var saveEnvelope = JsonSerializer.Deserialize<LeagueSaveEnvelope>(json, Options)
            ?? throw new InvalidOperationException("The league save envelope could not be deserialized.");

        Validate(saveEnvelope);

        return saveEnvelope;
    }

    private static void Validate(LeagueSaveEnvelope saveEnvelope)
    {
        if (saveEnvelope.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("The league save envelope must declare a positive schema version.");
        }

        if (string.IsNullOrWhiteSpace(saveEnvelope.LeagueName))
        {
            throw new InvalidOperationException("The league save envelope must declare a league name.");
        }

        if (saveEnvelope.CurrentSeasonYear <= 0)
        {
            throw new InvalidOperationException("The league save envelope must declare a positive current season year.");
        }
    }
}
