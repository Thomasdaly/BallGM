namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// Which image file each entity maps to, and what its fallback needs. Built from the loaded league
/// pack's own mapping (team logos, player portraits, team colours - the mod format is not changed);
/// rebuilt by <see cref="AssetResolver.ReloadAsync"/> through the supplied source.
/// </summary>
public sealed class AssetCatalog
{
    private readonly Dictionary<AssetKey, AssetEntry> _entries;

    public AssetCatalog(IEnumerable<AssetEntry> entries, IReadOnlyList<AssetPack>? packs = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = [];
        foreach (var entry in entries)
        {
            _entries[entry.Key] = entry;
        }

        Packs = packs ?? [];
    }

    public static AssetCatalog Empty { get; } = new([]);

    public IReadOnlyList<AssetPack> Packs { get; }

    public IReadOnlyCollection<AssetEntry> Entries => _entries.Values;

    public int MappedCount => _entries.Values.Count(entry => entry.FilePath is not null);

    public AssetEntry? Find(AssetKey key) => _entries.GetValueOrDefault(key);
}

/// <summary>A pack as the Mods &amp; Graphics screen lists it.</summary>
public sealed record AssetPack(string Name, string Kind, string Path, int LoadOrder, bool CanToggle = false);
