using Avalonia.Media;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// The one service that resolves every image in the UI (spec 05 section 3). Views and view models
/// never open image files; they ask for a key in a slot and draw what comes back.
/// </summary>
public interface IAssetResolver
{
    /// <summary>Instant, vector, never null: the image to show until (or instead of) the real one.</summary>
    IImage Fallback(AssetKey key, AssetSlot slot);

    bool TryGetCached(AssetKey key, AssetSlot slot, out IImage image);

    /// <summary>What the catalog knows about an entity (label, colours, file), or null.</summary>
    AssetEntry? Describe(AssetKey key);

    /// <summary>Resolves off the UI thread; returns the fallback when there is no usable image.</summary>
    Task<IImage> ResolveAsync(AssetKey key, AssetSlot slot, CancellationToken cancellationToken);

    AssetStats Stats { get; }

    IReadOnlyList<AssetIssue> Issues { get; }

    /// <summary>Rescans the packs, clears the cache, and raises <see cref="Reloaded"/>.</summary>
    Task ReloadAsync();

    event EventHandler? Reloaded;
}
