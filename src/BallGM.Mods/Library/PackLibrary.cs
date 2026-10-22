using BallGM.Application.Packs;
using BallGM.Mods.Manifests;

namespace BallGM.Mods.Library;

/// <summary>Where a pack was found: shipped with the game, or put in the player's own folder.</summary>
public enum PackOrigin
{
    BuiltIn,
    User,
}

/// <summary>A folder of packs: each immediate subfolder holding a <c>ballgm-pack.json</c> is one pack.</summary>
public sealed record PackRoot(string Directory, PackOrigin Origin);

/// <summary>A pack the library found and could read the manifest of.</summary>
public sealed record InstalledPack(PackManifest Manifest, string Directory, PackOrigin Origin)
{
    public string Id => Manifest.Id;
}

/// <summary>
/// Every pack the game can see, and everything wrong with the ones it cannot use. Scanning never
/// throws and never stops at the first bad folder: a broken manifest takes its own pack out of the
/// library and nothing else, and the finding says which folder and why.
/// <para>
/// Two packs claiming one id is the case Football Manager leaves to whichever folder the game happens
/// to read last. Here it is decided and said: the higher version wins; at equal versions a user pack
/// wins over a built-in one, then the folder that sorts first. Either way the finding names both.
/// </para>
/// </summary>
public sealed class PackLibrary
{
    private const string LegacyLeaguePackFile = "pack.json";

    private readonly Dictionary<string, InstalledPack> _packs;

    private PackLibrary(Dictionary<string, InstalledPack> packs, IReadOnlyList<PackFinding> findings)
    {
        _packs = packs;
        Findings = findings;
    }

    /// <summary>Every usable pack, ordered by id.</summary>
    public IReadOnlyList<InstalledPack> Packs => _packs.Values.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToList();

    /// <summary>What the scan had to say: unreadable manifests, duplicate ids, folders that are not packs.</summary>
    public IReadOnlyList<PackFinding> Findings { get; }

    public InstalledPack? Find(string id) => _packs.GetValueOrDefault(id);

    /// <summary>The installed packs of one kind, ordered by id.</summary>
    public IReadOnlyList<InstalledPack> OfKind(PackKind kind) => Packs.Where(pack => pack.Manifest.Kind == kind).ToList();

    public static PackLibrary Scan(IEnumerable<PackRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var findings = new List<PackFinding>();
        var candidates = new List<InstalledPack>();

        foreach (var root in roots)
        {
            if (!System.IO.Directory.Exists(root.Directory))
            {
                if (root.Origin == PackOrigin.BuiltIn)
                {
                    findings.Add(PackFinding.Warning(
                        "library.builtin_folder_missing",
                        PackCheckStage.Library,
                        $"The game's own pack folder '{root.Directory}' is missing, so none of the built-in league presets are available.",
                        fix: "Reinstall or verify the game files."));
                }

                continue;
            }

            foreach (var folder in System.IO.Directory.EnumerateDirectories(root.Directory).Order(StringComparer.Ordinal))
            {
                var pack = ReadFolder(folder, root.Origin, findings);
                if (pack is not null)
                {
                    candidates.Add(pack);
                }
            }
        }

        var packs = new Dictionary<string, InstalledPack>(StringComparer.Ordinal);
        foreach (var group in candidates.GroupBy(pack => pack.Id, StringComparer.Ordinal))
        {
            var ranked = group
                .OrderByDescending(pack => pack.Manifest.Version)
                .ThenBy(pack => pack.Origin == PackOrigin.User ? 0 : 1)
                .ThenBy(pack => pack.Directory, StringComparer.Ordinal)
                .ToList();
            packs[group.Key] = ranked[0];

            foreach (var loser in ranked.Skip(1))
            {
                findings.Add(PackFinding.Warning(
                    "library.duplicate_pack_id",
                    PackCheckStage.Library,
                    $"Two folders both contain pack '{group.Key}': '{ranked[0].Directory}' ({ranked[0].Manifest.Version}) is used and '{loser.Directory}' ({loser.Manifest.Version}) is ignored.",
                    group.Key,
                    "Delete the folder you do not want, so it is clear which one the game reads."));
            }
        }

        return new PackLibrary(packs, findings);
    }

    private static InstalledPack? ReadFolder(string folder, PackOrigin origin, List<PackFinding> findings)
    {
        var manifestPath = Path.Combine(folder, PackManifest.FileName);
        var name = Path.GetFileName(folder);

        if (!File.Exists(manifestPath))
        {
            // A folder of images or a bare league pack dropped in the way other games want it: say
            // what is missing instead of ignoring it in silence.
            var hint = File.Exists(Path.Combine(folder, LegacyLeaguePackFile))
                ? $"It holds a {LegacyLeaguePackFile} league pack but no manifest."
                : "It has no manifest.";
            findings.Add(PackFinding.Warning(
                "library.folder_without_manifest",
                PackCheckStage.Library,
                $"Folder '{name}' is not a pack and is ignored. {hint}",
                fix: $"Add a {PackManifest.FileName} describing it (see the modding guide, or run 'ballgm-packs init')."));
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(manifestPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            findings.Add(PackFinding.Error("library.manifest_unreadable", PackCheckStage.Library, $"Folder '{name}': the manifest could not be read: {exception.Message}"));
            return null;
        }

        var manifest = PackManifestReader.Read(text);
        if (manifest.IsFailure)
        {
            foreach (var error in manifest.Errors)
            {
                findings.Add(PackFinding.Error(error.Code, PackCheckStage.Library, $"Folder '{name}': {error.Message}", fix: "Fix the manifest; until then this pack is left out of every setup."));
            }

            return null;
        }

        if (origin == PackOrigin.User && manifest.Value.Id.StartsWith(PackManifest.BuiltInIdPrefix, StringComparison.Ordinal))
        {
            findings.Add(PackFinding.Error(
                "library.reserved_pack_id",
                PackCheckStage.Library,
                $"Folder '{name}' claims the id '{manifest.Value.Id}', and ids starting '{PackManifest.BuiltInIdPrefix}' are reserved for the packs the game ships.",
                manifest.Value.Id,
                "Give the pack an id of your own, like 'yourname.pack-name'."));
            return null;
        }

        return new InstalledPack(manifest.Value, folder, origin);
    }
}
