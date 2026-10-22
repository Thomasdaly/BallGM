using System.Text.Json;
using BallGM.Application.Packs;
using BallGM.Domain.Common;
using BallGM.Mods.Manifests;

namespace BallGM.Mods.Setups;

/// <summary>
/// A league setup: everything a new career is started from, named in one place and in load order —
/// the league settings (a ruleset pack, plus any per-setting overrides), the league (a league pack,
/// or a generated fictional one shaped to the settings), and the cosmetic packs layered on top, last
/// one winning wherever two cover the same team or player.
/// <para>
/// The point of the file is that it is the <em>whole</em> answer to "what am I playing". Football
/// Manager spreads the same answer across a database version, editor files ticked at game start, a
/// graphics folder with a cache, and a skin setting; a mismatch between any two of them is a bug
/// report nobody can reproduce. A setup is one file the checker can verify end to end, and that a
/// player can hand to another player.
/// </para>
/// </summary>
public sealed record LeagueSetup(
    string Name,
    PackReference Ruleset,
    IReadOnlyDictionary<string, JsonElement> Settings,
    LeagueChoice League,
    IReadOnlyList<PackReference> Cosmetics,
    int SmokeTestSeed)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The seed a setup's test season is played from when it names none.</summary>
    public const int DefaultSmokeTestSeed = 20_260_701;
}

/// <summary>Where a setup's league comes from: exactly one of a league pack and a generated league.</summary>
public sealed record LeagueChoice(PackReference? Pack, GeneratedLeagueSource? Generated);

/// <summary>Reads a setup file. Never throws on content.</summary>
public static class LeagueSetupReader
{
    public const string MalformedCode = "setup.malformed";
    public const string UnsupportedSchemaCode = "setup.unsupported_schema";
    public const string InvalidFieldCode = "setup.invalid_field";

    public static DomainOperationResult<LeagueSetup> Read(string json)
    {
        LeagueSetupEnvelope? envelope;
        try
        {
            envelope = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LeagueSetupEnvelope>(json, PackJson.Options);
        }
        catch (JsonException exception)
        {
            return Fail(MalformedCode, $"The setup file is not valid: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail(MalformedCode, "The setup file is empty.");
        }

        if (envelope.SchemaVersion != LeagueSetup.CurrentSchemaVersion)
        {
            return Fail(UnsupportedSchemaCode, $"The setup file is schema version {envelope.SchemaVersion}; this build reads version {LeagueSetup.CurrentSchemaVersion}.");
        }

        var errors = new List<DomainError>();
        if (string.IsNullOrWhiteSpace(envelope.Name))
        {
            errors.Add(Invalid("name", "A setup needs a name."));
        }

        var ruleset = Reference(envelope.Ruleset, "ruleset", errors);
        if (envelope.Ruleset is null)
        {
            errors.Add(Invalid("ruleset", "A setup names the ruleset pack its league settings start from."));
        }

        var league = League(envelope.League, errors);
        var cosmetics = (envelope.Cosmetics ?? [])
            .Select((reference, index) => Reference(reference, $"cosmetics[{index}]", errors))
            .OfType<PackReference>()
            .ToList();

        return errors.Count > 0
            ? DomainOperationResult<LeagueSetup>.Failure(errors.ToArray())
            : DomainOperationResult<LeagueSetup>.Success(new LeagueSetup(
                envelope.Name!.Trim(),
                ruleset!,
                envelope.Settings ?? new Dictionary<string, JsonElement>(),
                league!,
                cosmetics,
                envelope.SmokeTestSeed ?? LeagueSetup.DefaultSmokeTestSeed));
    }

    private static LeagueChoice? League(LeagueChoiceEnvelope? league, List<DomainError> errors)
    {
        if (league is null || (league.Pack is null) == (league.Generated is null))
        {
            errors.Add(Invalid("league", "State exactly one of 'pack' (a league pack id) and 'generated' (a generated fictional league)."));
            return null;
        }

        if (league.Pack is not null)
        {
            var reference = Reference(new PackReferenceEnvelope(league.Pack, league.Versions), "league", errors);
            return reference is null ? null : new LeagueChoice(reference, null);
        }

        var generated = league.Generated!;
        if (generated.Teams is not int teams || teams < 2)
        {
            errors.Add(Invalid("league.generated.teams", "A generated league states how many teams it has (at least 2)."));
            return null;
        }

        return new LeagueChoice(
            null,
            new GeneratedLeagueSource(
                generated.Seed ?? 1,
                teams,
                generated.Conferences ?? 0,
                generated.DivisionsPerConference ?? 1,
                generated.RosterSize));
    }

    private static PackReference? Reference(PackReferenceEnvelope? reference, string field, List<DomainError> errors)
    {
        if (reference is null)
        {
            return null;
        }

        if (!PackManifestReader.IsValidId(reference.Pack))
        {
            errors.Add(Invalid(field, $"'{reference.Pack}' is not a pack id."));
            return null;
        }

        if (!VersionRange.TryParse(reference.Versions, out var range))
        {
            errors.Add(Invalid(field, $"'{reference.Versions}' is not a version range."));
            return null;
        }

        return new PackReference(reference.Pack!, range);
    }

    private static DomainError Invalid(string field, string message) => new(InvalidFieldCode, $"{field}: {message}");

    private static DomainOperationResult<LeagueSetup> Fail(string code, string message) =>
        DomainOperationResult<LeagueSetup>.Failure(new DomainError(code, message));
}

/// <summary>The on-disk shape of a setup file (<c>setup.json</c>, or any file a player saves one as).</summary>
public sealed record LeagueSetupEnvelope(
    int SchemaVersion,
    string? Name,
    PackReferenceEnvelope? Ruleset,
    IReadOnlyDictionary<string, JsonElement>? Settings = null,
    LeagueChoiceEnvelope? League = null,
    IReadOnlyList<PackReferenceEnvelope>? Cosmetics = null,
    int? SmokeTestSeed = null);

/// <summary>A league pack by id and range, or a generated league.</summary>
public sealed record LeagueChoiceEnvelope(string? Pack = null, string? Versions = null, GeneratedLeagueEnvelope? Generated = null);

/// <summary>A generated league's shape: teams, conferences (default 0: one flat table), divisions per conference (default 1), seed (default 1) and roster size (default: the settings' maximum).</summary>
public sealed record GeneratedLeagueEnvelope(int? Teams = null, int? Conferences = null, int? DivisionsPerConference = null, int? Seed = null, int? RosterSize = null);
