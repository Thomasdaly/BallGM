namespace BallGM.Client.Avalonia.Imaging;

/// <summary>What an image depicts.</summary>
public enum AssetEntityKind
{
    Player,
    Team,
    League,
}

/// <summary>Which kind of frame an image is drawn in; decides cropping, aspect rules and fallback.</summary>
public enum AssetSlotKind
{
    /// <summary>Player photo: fills its frame (UniformToFill), anchored bottom-centre.</summary>
    Headshot,

    /// <summary>Team logo: fits its frame (Uniform), centred, never upscaled past 2x.</summary>
    Logo,

    /// <summary>League or competition badge.</summary>
    LeagueLogo,
}

/// <summary>The entity an image belongs to (spec 05 section 3): kind plus the entity's ID.</summary>
public readonly record struct AssetKey(AssetEntityKind Kind, string Id);

/// <summary>A drawn slot: kind and size in DIPs (the long edge for logos, the height for headshots).</summary>
public readonly record struct AssetSlot(AssetSlotKind Kind, double SizeDips)
{
    public static AssetSlot Headshot(double size) => new(AssetSlotKind.Headshot, size);

    public static AssetSlot Logo(double size) => new(AssetSlotKind.Logo, size);
}

/// <summary>
/// What the resolver knows about one entity: the image file the pack maps it to (if any), which pack,
/// and what its fallback needs (short label, team colours, jersey number).
/// </summary>
public sealed record AssetEntry(
    AssetKey Key,
    string Label,
    string? FilePath,
    string? PackName,
    string? PackRoot,
    string? PrimaryColour,
    string? SecondaryColour,
    string? Number = null);

/// <summary>One file that could not be shown as it is, and why (spec 05 section 5).</summary>
public sealed record AssetIssue(string File, string Reason, string? Pack, string SuggestedFix, bool IsWarning = false);

/// <summary>Counts for the Mods &amp; Graphics screen.</summary>
public sealed record AssetStats(
    int Packs,
    int Mapped,
    int Failed,
    int CacheEntries,
    long CacheBytes,
    long CacheBudgetBytes,
    long Hits,
    long Misses)
{
    public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
}

/// <summary>Progress of a pack scan.</summary>
public readonly record struct AssetScanProgress(int Done, int Total);

/// <summary>Standard issue reasons, so the screen, the docs and the tests agree on wording.</summary>
public static class AssetIssueReasons
{
    public const string MissingFile = "missing file";
    public const string EmptyFile = "empty file";
    public const string DecodeFailed = "decode failed";
    public const string Oversized = "oversized";
    public const string UnsupportedAspect = "unsupported aspect";
    public const string Animated = "animated image";
    public const string Unreadable = "unreadable";
    public const string UnsafePath = "unsafe path";
    public const string Conflict = "conflict";
}
