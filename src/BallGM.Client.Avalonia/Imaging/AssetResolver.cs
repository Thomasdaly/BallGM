using System.Collections.Concurrent;
using Avalonia.Media;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// Resolves every image the UI draws (spec 05 sections 3-5). Order: the loaded pack's mapping, then the
/// generated vector fallback. Files are read and decoded on worker threads only, at the bucketed size
/// they are drawn at, and kept in a byte-budgeted LRU cache. A bad file never throws into the UI: it
/// becomes the fallback plus one <see cref="AssetIssue"/> (and one log line) per file per session.
/// </summary>
public sealed class AssetResolver : IAssetResolver
{
    public const long DefaultBudgetBytes = 256L * 1024 * 1024;
    public const int MaxSourceEdge = 4096;
    private static readonly int[] Buckets = [32, 64, 128, 256, 512, 1024];

    private readonly Func<AssetCatalog> _catalogSource;
    private readonly IImageDecoder _decoder;
    private readonly Func<double> _scale;
    private readonly Action<string>? _log;
    private readonly AssetCache _cache;
    private readonly SemaphoreSlim _decodeSlots = new(4);
    private readonly ConcurrentDictionary<(string File, string Reason), AssetIssue> _issues = new();
    private readonly ConcurrentDictionary<(string Path, int Bucket), Task<IImage?>> _inFlight = new();
    private AssetCatalog _catalog;
    private long _hits;
    private long _misses;
    private int _pending;

    public AssetResolver(
        Func<AssetCatalog> catalogSource,
        IImageDecoder? decoder = null,
        long budgetBytes = DefaultBudgetBytes,
        Func<double>? scale = null,
        Action<string>? log = null)
    {
        _catalogSource = catalogSource ?? throw new ArgumentNullException(nameof(catalogSource));
        _decoder = decoder ?? new SkiaImageDecoder();
        _scale = scale ?? (() => 1.0);
        _log = log;
        _cache = new AssetCache(budgetBytes);
        _catalog = catalogSource();
    }

    public event EventHandler? Reloaded;

    public AssetCatalog Catalog => _catalog;

    /// <summary>Loads started and not yet finished (lets a capture wait until every image has landed).</summary>
    public int PendingLoads => Volatile.Read(ref _pending);

    public AssetCache Cache => _cache;

    public AssetStats Stats => new(
        _catalog.Packs.Count,
        _catalog.MappedCount,
        _issues.Values.Where(issue => !issue.IsWarning).Select(issue => issue.File).Distinct(StringComparer.Ordinal).Count(),
        _cache.Count,
        _cache.Bytes,
        _cache.BudgetBytes,
        Interlocked.Read(ref _hits),
        Interlocked.Read(ref _misses));

    public IReadOnlyList<AssetIssue> Issues => _issues.Values.OrderBy(issue => issue.File, StringComparer.Ordinal).ThenBy(issue => issue.Reason, StringComparer.Ordinal).ToList();

    public IImage Fallback(AssetKey key, AssetSlot slot) => FallbackArt.For(_catalog.Find(key), key, slot);

    public AssetEntry? Describe(AssetKey key) => _catalog.Find(key);

    /// <summary>Pixels a slot is decoded at: DIPs x render and UI scale, rounded up to a bucket.</summary>
    public int BucketFor(AssetSlot slot)
    {
        var pixels = (int)Math.Ceiling(slot.SizeDips * Math.Max(0.1, _scale()));
        return Buckets.FirstOrDefault(bucket => bucket >= pixels, Buckets[^1]);
    }

    public bool TryGetCached(AssetKey key, AssetSlot slot, out IImage image)
    {
        if (_catalog.Find(key)?.FilePath is { } path && _cache.TryGet(path, BucketFor(slot), out image))
        {
            Interlocked.Increment(ref _hits);
            return true;
        }

        image = null!;
        return false;
    }

    public async Task<IImage> ResolveAsync(AssetKey key, AssetSlot slot, CancellationToken cancellationToken)
    {
        var entry = _catalog.Find(key);
        if (entry?.FilePath is not { } path)
        {
            return Fallback(key, slot);
        }

        var bucket = BucketFor(slot);
        if (_cache.TryGet(path, bucket, out var cached))
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _pending);
        var load = _inFlight.GetOrAdd((path, bucket), _ => Task.Run(() => LoadAsync(entry, slot.Kind, bucket), CancellationToken.None));
        try
        {
            var image = await load.WaitAsync(cancellationToken).ConfigureAwait(false);
            return image ?? Fallback(key, slot);
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
            if (load.IsCompleted)
            {
                _inFlight.TryRemove(KeyValuePair.Create((path, bucket), load));
            }
        }
    }

    /// <summary>
    /// Checks every mapped file's path and header off the UI thread (no pixel decoding), reporting
    /// progress, so the issues list is complete before anything is drawn.
    /// </summary>
    public Task ScanAsync(IProgress<AssetScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                var entries = _catalog.Entries.Where(entry => entry.FilePath is not null).ToList();
                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[index];
                    var slot = entry.Key.Kind == AssetEntityKind.Player ? AssetSlotKind.Headshot : AssetSlotKind.Logo;
                    _ = ReadChecked(entry, slot, out _);
                    if (index % 50 == 0 || index == entries.Count - 1)
                    {
                        progress?.Report(new AssetScanProgress(index + 1, entries.Count));
                    }
                }
            },
            cancellationToken);

    public async Task ReloadAsync()
    {
        _catalog = _catalogSource();
        _cache.Clear();
        _issues.Clear();
        _inFlight.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        await ScanAsync().ConfigureAwait(false);
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IImage?> LoadAsync(AssetEntry entry, AssetSlotKind kind, int bucket)
    {
        await _decodeSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = ReadChecked(entry, kind, out var probe);
            if (bytes is null || probe is null)
            {
                return null;
            }

            var (width, height) = TargetSize(probe, kind, bucket);
            IImage image;
            try
            {
                image = _decoder.Decode(bytes, width, height);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Report(entry, AssetIssueReasons.DecodeFailed, "Re-export the image as PNG or JPEG.", detail: exception.Message);
                return null;
            }

            _cache.Add(entry.FilePath!, bucket, image, (long)width * height * 4);
            return image;
        }
        finally
        {
            _decodeSlots.Release();
        }
    }

    /// <summary>Path safety, read, header checks. Null when the file cannot be shown.</summary>
    private byte[]? ReadChecked(AssetEntry entry, AssetSlotKind kind, out ImageProbe? probe)
    {
        probe = null;
        var path = entry.FilePath!;
        if (entry.PackRoot is { } root && !AssetPathGuard.IsInsidePack(path, root, out _))
        {
            Report(entry, AssetIssueReasons.UnsafePath, "Keep images inside the pack folder; no '..', absolute paths or links that leave it.");
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            Report(entry, AssetIssueReasons.MissingFile, "Add the file, or fix the path in the pack.");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report(entry, AssetIssueReasons.Unreadable, "Check the file's permissions and that no other program has it locked.", detail: exception.Message);
            return null;
        }

        if (bytes.Length == 0)
        {
            Report(entry, AssetIssueReasons.EmptyFile, "The file is zero bytes; export it again.");
            return null;
        }

        try
        {
            probe = _decoder.Probe(bytes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            probe = null;
            Report(entry, AssetIssueReasons.DecodeFailed, "Re-export the image as PNG or JPEG.", detail: exception.Message);
            return null;
        }

        if (probe is null || probe.Width <= 0 || probe.Height <= 0)
        {
            Report(entry, AssetIssueReasons.DecodeFailed, "The file is not a readable PNG or JPEG; re-export it.");
            probe = null;
            return null;
        }

        if (probe.FrameCount > 1)
        {
            Report(entry, AssetIssueReasons.Animated, "Only the first frame is shown; use a still PNG.", warning: true);
        }

        if (Math.Max(probe.Width, probe.Height) > MaxSourceEdge)
        {
            Report(entry, AssetIssueReasons.Oversized, $"Scale the image to {MaxSourceEdge} px or less on its long edge (512 px is plenty).", warning: true);
        }

        var aspect = (double)Math.Max(probe.Width, probe.Height) / Math.Min(probe.Width, probe.Height);
        if (kind == AssetSlotKind.Headshot && aspect > 2.0)
        {
            Report(entry, AssetIssueReasons.UnsupportedAspect, "Crop player photos to between 1:1 and about 1.37:1.");
            probe = null;
            return null;
        }

        if (kind != AssetSlotKind.Headshot && aspect > 3.0)
        {
            Report(entry, AssetIssueReasons.UnsupportedAspect, "Logos should be roughly square; it is shown letterboxed.", warning: true);
        }

        return bytes;
    }

    private static (int Width, int Height) TargetSize(ImageProbe probe, AssetSlotKind kind, int bucket)
    {
        // Headshots fill their frame, so the short edge reaches the bucket; logos fit, so the long edge does.
        var edge = kind == AssetSlotKind.Headshot ? Math.Min(probe.Width, probe.Height) : Math.Max(probe.Width, probe.Height);
        var scale = Math.Min(1.0, (double)bucket / edge);
        return (Math.Max(1, (int)Math.Round(probe.Width * scale)), Math.Max(1, (int)Math.Round(probe.Height * scale)));
    }

    private void Report(AssetEntry entry, string reason, string fix, bool warning = false, string? detail = null)
    {
        var file = entry.FilePath ?? entry.Key.Id;
        var issue = new AssetIssue(file, reason, entry.PackName, fix, warning);
        if (_issues.TryAdd((file, reason), issue))
        {
            _log?.Invoke($"asset {reason}: {file}{(detail is null ? string.Empty : " (" + detail + ")")}");
        }
    }
}
