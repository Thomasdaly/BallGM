using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Rulesets;
using BallGM.Mods.Library;
using BallGM.Rules.Configuration;

namespace BallGM.Integration.Tests.Packs;

/// <summary>
/// Pack folders written into a temporary directory at test time: manifests, a small generated league
/// pack, and image files that are only as real as the checker needs — a PNG or JPEG signature and the
/// header carrying the dimensions. Nothing here is committed and no real-world content is involved.
/// The built-in packs are read from the build output, exactly as the game reads them.
/// </summary>
internal sealed class PackFixture : IDisposable
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>The built-in North American major preset, read the way the game reads it.</summary>
    public static readonly Lazy<LeagueRuleset> MajorRuleset = new(() =>
        new LeagueRulesetSerializer().Deserialize(File.ReadAllText(Path.Combine(PackFolders.BuiltIn, "builtin.ruleset.na-major", "ruleset.json"))).Value);

    public PackFixture()
    {
        Directory.CreateDirectory(UserFolder);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ballgm-packs-" + Guid.NewGuid().ToString("N"));

    public string UserFolder => Path.Combine(Root, "mods");

    public PackLibrary Library() => PackLibrary.Scan([new PackRoot(PackFolders.BuiltIn, PackOrigin.BuiltIn), new PackRoot(UserFolder, PackOrigin.User)]);

    public void Dispose() => Directory.Delete(Root, recursive: true);

    /// <summary>A pack folder named after the id, holding the manifest; returns the folder.</summary>
    public string Pack(string id, string kind, string version = "1.0.0", object? extra = null, string? folder = null)
    {
        var directory = Path.Combine(UserFolder, folder ?? id);
        Directory.CreateDirectory(directory);
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["id"] = id,
            ["name"] = id,
            ["version"] = version,
            ["kind"] = kind,
        };

        if (extra is not null)
        {
            foreach (var (key, value) in JsonSerializer.SerializeToNode(extra, Options)!.AsObject())
            {
                manifest[key] = value?.DeepClone();
            }
        }

        File.WriteAllText(Path.Combine(directory, "ballgm-pack.json"), manifest.ToJsonString(Options));
        return directory;
    }

    /// <summary>
    /// A league pack of <paramref name="teams"/> fictional teams of twelve, in one flat table, salaries
    /// on the North American major preset's scale. The first <paramref name="statedKeys"/> players state
    /// keys <c>p0</c>, <c>p1</c>…; the rest are keyed by name and birth date.
    /// </summary>
    public string League(string id, string version = "1.0.0", int teams = 4, int statedKeys = 2, object? extra = null)
    {
        var directory = Pack(id, "league", version, extra);
        var shape = GeneratedLeagueShape.Create(teams, 0, 1, 12).Value;
        var envelope = GeneratedLeagueDataSource.Envelope(7, shape, MajorRuleset.Value);
        var players = envelope.Players!
            .Select((player, index) => index < statedKeys ? player with { Key = $"p{index}" } : player)
            .ToList();
        File.WriteAllText(Path.Combine(directory, "league.json"), JsonSerializer.Serialize(envelope with { Players = players }, Options));
        return directory;
    }

    public static void Png(string path, int width = 256, int height = 256)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        File.WriteAllBytes(path, bytes);
    }

    public static void Jpeg(string path, int width = 256, int height = 256)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0, 0x03, 0, 0, 0 };
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(13), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(15), (ushort)width);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A setup file in the fixture root; returns its path.</summary>
    public string Setup(object setup, string name = "setup.json")
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, JsonSerializer.Serialize(setup, Options));
        return path;
    }
}
