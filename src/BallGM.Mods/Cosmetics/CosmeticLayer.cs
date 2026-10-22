using System.Text.Json;
using BallGM.Application.Leagues;
using BallGM.Application.Packs;
using BallGM.Domain.Common;
using BallGM.Mods.Library;
using BallGM.Mods.Manifests;

namespace BallGM.Mods.Cosmetics;

/// <summary>
/// One cosmetic pack's content, read and keyed: images by team or player key (full, already
/// path-checked file paths), a league logo, or team colours. What is in the pack, not yet what matches
/// the league — matching is the checker's job, because it needs the league.
/// </summary>
public sealed record CosmeticLayer(
    InstalledPack Pack,
    IReadOnlyDictionary<string, string> Images,
    string? LeagueLogo,
    IReadOnlyDictionary<string, TeamColours> Colours)
{
    public PackKind Kind => Pack.Manifest.Kind;
}

/// <summary>
/// Reads a faces, logos or colours pack. Two ways to say which file is whose, and they combine:
/// <list type="number">
/// <item>By name: <c>faces/aldo-brenner-2001-03-14.png</c> is that player's face. No mapping file to
/// write, keep in step or regenerate — the folder is the mapping.</item>
/// <item>By map: a JSON object of <c>"key": "path/inside/the/pack.png"</c>, for files that cannot
/// be renamed (a shared image, a key that is not a safe file name). A map entry wins over a file
/// named for the same key.</item>
/// </list>
/// Problems never stop a cosmetic pack loading: whatever cannot be read is reported and left to the
/// fallback art, and the rest of the pack still applies.
/// </summary>
public static class CosmeticPackReader
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    public static CosmeticLayer Read(InstalledPack pack, List<PackFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(findings);

        return pack.Manifest.Kind switch
        {
            PackKind.Faces or PackKind.Logos => ReadImages(pack, findings),
            PackKind.Colours => ReadColours(pack, findings),
            _ => throw new ArgumentOutOfRangeException(nameof(pack), pack.Manifest.Kind, "Not a cosmetic pack."),
        };
    }

    private static CosmeticLayer ReadImages(InstalledPack pack, List<PackFinding> findings)
    {
        var manifest = pack.Manifest;
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        var folderName = manifest.Content.FolderFor(manifest.Kind);
        var ignored = new List<string>();
        var duplicates = new List<string>();

        if (!PackPaths.TryResolve(pack.Directory, folderName, out var folder, out var reason))
        {
            findings.Add(PackFinding.Warning("cosmetics.unsafe_path", PackCheckStage.Cosmetics, $"The image folder was refused: {reason}.", pack.Id));
        }
        else if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                var key = Path.GetFileNameWithoutExtension(file);
                if (!ImageExtensions.Contains(extension) || key.Length == 0)
                {
                    ignored.Add(Path.GetFileName(file));
                    continue;
                }

                if (!PackPaths.TryResolve(pack.Directory, Path.Combine(folderName, Path.GetFileName(file)), out var full, out reason))
                {
                    findings.Add(PackFinding.Warning("cosmetics.unsafe_path", PackCheckStage.Cosmetics, $"{Path.GetFileName(file)} was refused: {reason}.", pack.Id));
                    continue;
                }

                // Ordinal order puts .jpeg before .jpg before .png; prefer the PNG, which keeps transparency.
                if (images.ContainsKey(key))
                {
                    duplicates.Add(key);
                    if (extension != ".png")
                    {
                        continue;
                    }
                }

                images[key] = full;
            }
        }
        else if (manifest.Content.Map is null)
        {
            findings.Add(PackFinding.Warning(
                "cosmetics.folder_missing",
                PackCheckStage.Cosmetics,
                $"The pack has no '{folderName}' folder and no map, so it provides nothing.",
                pack.Id,
                $"Put the images in a folder called '{folderName}' next to {PackManifest.FileName}, each named after its key."));
        }

        if (ignored.Count > 0)
        {
            findings.Add(PackFinding.Warning(
                "cosmetics.not_an_image",
                PackCheckStage.Cosmetics,
                $"{ExplanationText.Count(ignored.Count, "file")} in '{folderName}' {(ignored.Count == 1 ? "is" : "are")} not PNG or JPEG and {(ignored.Count == 1 ? "is" : "are")} ignored: {Examples(ignored)}.",
                pack.Id,
                "Convert them to PNG (best, keeps transparency) or JPEG."));
        }

        if (duplicates.Count > 0)
        {
            findings.Add(PackFinding.Info(
                "cosmetics.same_key_two_files",
                PackCheckStage.Cosmetics,
                $"{ExplanationText.Count(duplicates.Count, "key")} {(duplicates.Count == 1 ? "has" : "have")} more than one file ({Examples(duplicates)}); the PNG is used.",
                pack.Id));
        }

        if (manifest.Content.Map is not null)
        {
            ReadMap(pack, manifest.Content.Map, images, findings);
        }

        string? leagueLogo = null;
        if (manifest.Content.LeagueLogo is not null)
        {
            if (PackPaths.TryResolve(pack.Directory, manifest.Content.LeagueLogo, out var full, out reason))
            {
                leagueLogo = full;
            }
            else
            {
                findings.Add(PackFinding.Warning("cosmetics.unsafe_path", PackCheckStage.Cosmetics, $"The league logo was refused: {reason}.", pack.Id));
            }
        }

        return new CosmeticLayer(pack, images, leagueLogo, new Dictionary<string, TeamColours>());
    }

    private static void ReadMap(InstalledPack pack, string mapPath, Dictionary<string, string> images, List<PackFinding> findings)
    {
        var map = ReadJson<Dictionary<string, string>>(pack, mapPath, "map", findings);
        if (map is null)
        {
            return;
        }

        var refused = new List<string>();
        foreach (var (key, relative) in map.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!PackPaths.TryResolve(pack.Directory, relative, out var full, out var reason))
            {
                refused.Add($"{key} ({reason})");
                continue;
            }

            images[key] = full;
        }

        if (refused.Count > 0)
        {
            findings.Add(PackFinding.Warning(
                "cosmetics.unsafe_path",
                PackCheckStage.Cosmetics,
                $"{ExplanationText.Count(refused.Count, "map entry", "map entries")} {(refused.Count == 1 ? "was" : "were")} refused before any file was opened: {Examples(refused)}.",
                pack.Id,
                "Map entries name files inside the pack folder, relative to it, like 'faces/name.png'."));
        }
    }

    private static CosmeticLayer ReadColours(InstalledPack pack, List<PackFinding> findings)
    {
        var colours = new Dictionary<string, TeamColours>(StringComparer.Ordinal);
        var table = ReadJson<Dictionary<string, ColoursEnvelope>>(pack, pack.Manifest.Content.FileFor(PackKind.Colours), "colours file", findings);
        var invalid = new List<string>();

        foreach (var (key, entry) in (table ?? []).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var primary = NormaliseHex(entry.Primary);
            var secondary = entry.Secondary is null ? null : NormaliseHex(entry.Secondary);
            if (primary is null || (entry.Secondary is not null && secondary is null))
            {
                invalid.Add(key);
                continue;
            }

            colours[key] = new TeamColours(primary, secondary);
        }

        if (invalid.Count > 0)
        {
            findings.Add(PackFinding.Warning(
                "cosmetics.invalid_colour",
                PackCheckStage.Cosmetics,
                $"{ExplanationText.Count(invalid.Count, "team")} {(invalid.Count == 1 ? "has" : "have")} a colour that is not #RRGGBB and {(invalid.Count == 1 ? "keeps" : "keep")} the league pack's colours: {Examples(invalid)}.",
                pack.Id));
        }

        return new CosmeticLayer(pack, new Dictionary<string, string>(), null, colours);
    }

    private static T? ReadJson<T>(InstalledPack pack, string relative, string description, List<PackFinding> findings)
        where T : class
    {
        if (!PackPaths.TryResolve(pack.Directory, relative, out var path, out var reason))
        {
            findings.Add(PackFinding.Warning("cosmetics.unsafe_path", PackCheckStage.Cosmetics, $"The {description} was refused: {reason}.", pack.Id));
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), PackJson.Options)
                ?? throw new JsonException("it is empty");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            findings.Add(PackFinding.Warning(
                "cosmetics.file_unreadable",
                PackCheckStage.Cosmetics,
                $"The {description} '{relative}' could not be read ({exception.Message}); what it would have supplied falls back.",
                pack.Id));
            return null;
        }
    }

    private static string? NormaliseHex(string? colour) =>
        colour is { Length: 7 } && colour[0] == '#' && colour.Skip(1).All(char.IsAsciiHexDigit) ? colour.ToUpperInvariant() : null;

    internal static string Examples(IReadOnlyList<string> items, int shown = 3) =>
        items.Count <= shown ? string.Join(", ", items) : $"{string.Join(", ", items.Take(shown))} and {items.Count - shown:N0} more";

    private sealed record ColoursEnvelope(string? Primary, string? Secondary = null);
}
