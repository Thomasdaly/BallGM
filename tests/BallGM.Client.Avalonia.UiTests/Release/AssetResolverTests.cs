using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using BallGM.Client.Avalonia.Imaging;
using BallGM.Client.Avalonia.UiTests.Harness;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>The asset resolver's contract (spec 05 sections 3-5): safety, threading, robustness, budget.</summary>
[Trait("Category", "UiRelease")]
public sealed class AssetResolverTests
{
    [AvaloniaFact]
    public void Assets_PathSafety()
    {
        var root = Directory.CreateTempSubdirectory("ballgm-guard-").FullName;
        var outsideDirectory = Directory.CreateTempSubdirectory("ballgm-outside-").FullName;
        var outside = Path.Combine(outsideDirectory, "face.png");
        File.WriteAllBytes(outside, [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(root, "faces"));
        File.WriteAllBytes(Path.Combine(root, "faces", "ok.png"), [1, 2, 3]);

        Assert.True(AssetPathGuard.IsInsidePack("faces/ok.png", root, out _));
        Assert.True(AssetPathGuard.IsInsidePack(Path.Combine(root, "faces", "ok.png"), root, out _));
        Assert.False(AssetPathGuard.IsInsidePack("../" + Path.GetFileName(outsideDirectory) + "/face.png", root, out _));
        Assert.False(AssetPathGuard.IsInsidePack("faces/../ok.png", root, out _));
        Assert.False(AssetPathGuard.IsInsidePack(outside, root, out _));

        var link = Path.Combine(root, "faces", "escape.png");
        var linkDirectory = Path.Combine(root, "linked");
        var canLink = TryLink(() => File.CreateSymbolicLink(link, outside)) && TryLink(() => Directory.CreateSymbolicLink(linkDirectory, outsideDirectory));
        if (canLink)
        {
            Assert.False(AssetPathGuard.IsInsidePack("faces/escape.png", root, out _));
            Assert.False(AssetPathGuard.IsInsidePack("linked/face.png", root, out _));
        }

        // Through the resolver: the unsafe mapping becomes the fallback plus an issue, and the decoder
        // never sees the file.
        var decoder = new RecordingDecoder();
        var resolver = Resolver(decoder, new AssetEntry(Player("p1"), "AB", outside, "pack", root, "#0B1D3A", "#19A7A0"));
        var image = UiWait.For(resolver.ResolveAsync(Player("p1"), AssetSlot.Headshot(48), CancellationToken.None));

        Assert.Same(resolver.Fallback(Player("p1"), AssetSlot.Headshot(48)), image);
        Assert.Contains(resolver.Issues, issue => issue.Reason == AssetIssueReasons.UnsafePath);
        Assert.Equal(0, decoder.Probes);
    }

    [AvaloniaFact]
    public void Assets_NoUiThreadDecode()
    {
        var uiThread = Environment.CurrentManagedThreadId;
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var pack = ModFixtures.Build(ModFixtures.Kind.Good);
        var face = Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(pack)!, "faces"), "*.png").First();
        var decoder = new RecordingDecoder(delay: TimeSpan.FromMilliseconds(400));
        var resolver = Resolver(decoder, new AssetEntry(Player("p1"), "AB", face, "pack", Path.GetDirectoryName(pack), null, null));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var task = resolver.ResolveAsync(Player("p1"), AssetSlot.Headshot(96), CancellationToken.None);
        var returnedAfter = started.Elapsed;
        var fallback = resolver.Fallback(Player("p1"), AssetSlot.Headshot(96));

        Assert.True(returnedAfter < TimeSpan.FromMilliseconds(100), $"ResolveAsync held the UI thread for {returnedAfter.TotalMilliseconds:F0} ms.");
        Assert.False(task.IsCompleted);
        Assert.NotNull(fallback);

        var image = UiWait.For(task);
        Assert.NotSame(fallback, image);
        Assert.NotEmpty(decoder.Threads);
        Assert.DoesNotContain(uiThread, decoder.Threads);
    }

    [AvaloniaFact]
    public void Assets_BrokenFilesBecomeFallbacksWithOneIssueEach()
    {
        var pack = ModFixtures.Build(ModFixtures.Kind.Broken);
        var root = Path.GetDirectoryName(pack)!;
        string Face(string name) => Path.Combine(root, "faces", name);

        var cases = new (string File, string Reason, bool ShowsImage)[]
        {
            ("missing.png", AssetIssueReasons.MissingFile, false),
            ("empty.png", AssetIssueReasons.EmptyFile, false),
            ("corrupt.png", AssetIssueReasons.DecodeFailed, false),
            ("truncated.png", AssetIssueReasons.DecodeFailed, false),
            ("huge.png", AssetIssueReasons.Oversized, true),
            ("banner.png", AssetIssueReasons.UnsupportedAspect, false),
        };

        var entries = cases.Select((item, index) => new AssetEntry(Player("p" + index), "AB", Face(item.File), "broken", root, "#000000", "#FF6A13")).ToList();
        entries.Add(new AssetEntry(Player("jpeg"), "AB", Face("really-a-jpeg.png"), "broken", root, null, null));
        entries.Add(new AssetEntry(new AssetKey(AssetEntityKind.Team, "wide"), "W", Path.Combine(root, "logos", "wide.png"), "broken", root, "#4682B4", "#FFFFFF"));
        var resolver = new AssetResolver(() => new AssetCatalog(entries));

        for (var index = 0; index < cases.Length; index++)
        {
            var key = Player("p" + index);
            var image = UiWait.For(resolver.ResolveAsync(key, AssetSlot.Headshot(48), CancellationToken.None));
            var isFallback = ReferenceEquals(image, resolver.Fallback(key, AssetSlot.Headshot(48)));
            Assert.True(isFallback != cases[index].ShowsImage, $"{cases[index].File}: expected {(cases[index].ShowsImage ? "an image" : "the fallback")}.");
            Assert.Contains(resolver.Issues, issue => issue.File == Face(cases[index].File) && issue.Reason == cases[index].Reason);
        }

        // A JPEG named .png decodes by content; a 5:1 logo is shown letterboxed with a warning.
        Assert.IsNotType<DrawingImage>(UiWait.For(resolver.ResolveAsync(Player("jpeg"), AssetSlot.Headshot(48), CancellationToken.None)));
        Assert.IsNotType<DrawingImage>(UiWait.For(resolver.ResolveAsync(new AssetKey(AssetEntityKind.Team, "wide"), AssetSlot.Logo(48), CancellationToken.None)));
        Assert.Contains(resolver.Issues, issue => issue.Reason == AssetIssueReasons.UnsupportedAspect && issue.IsWarning);

        // One issue per file and reason, however often the file is asked for.
        var before = resolver.Issues.Count;
        _ = UiWait.For(resolver.ResolveAsync(Player("p1"), AssetSlot.Headshot(96), CancellationToken.None));
        Assert.Equal(before, resolver.Issues.Count);

        if (File.Exists(Face("escape.png")))
        {
            var unsafeResolver = new AssetResolver(() => new AssetCatalog([new AssetEntry(Player("x"), "AB", Face("escape.png"), "broken", root, null, null)]));
            _ = UiWait.For(unsafeResolver.ResolveAsync(Player("x"), AssetSlot.Headshot(48), CancellationToken.None));
            Assert.Contains(unsafeResolver.Issues, issue => issue.Reason == AssetIssueReasons.UnsafePath);
        }
    }

    [Fact]
    public void Assets_CacheStaysWithinItsBudget()
    {
        var cache = new AssetCache(budgetBytes: 1000);
        var image = new PlainImage();
        for (var index = 0; index < 50; index++)
        {
            cache.Add("file" + index, 64, image, 300);
            Assert.True(cache.Bytes <= 1000, $"Cache at {cache.Bytes} bytes after {index + 1} adds.");
        }

        Assert.True(cache.TryGet("file49", 64, out _));
        Assert.False(cache.TryGet("file0", 64, out _));
        Assert.True(cache.PeakBytes <= 1000);
    }

    [Theory]
    [InlineData(24, 1.0, 32)]
    [InlineData(48, 1.0, 64)]
    [InlineData(48, 1.5, 128)]
    [InlineData(160, 2.0, 512)]
    [InlineData(420, 3.0, 1024)]
    public void Assets_DecodeAtTheBucketForTheDrawnSize(double dips, double scale, int bucket)
    {
        var resolver = new AssetResolver(() => AssetCatalog.Empty, scale: () => scale);
        Assert.Equal(bucket, resolver.BucketFor(AssetSlot.Headshot(dips)));
    }

    [AvaloniaFact]
    public void Assets_FallbacksAreVectorAndThemedPerTeam()
    {
        var navy = new AssetEntry(new AssetKey(AssetEntityKind.Team, "HCM"), "HCM", null, null, null, "#0B1D3A", "#19A7A0");
        var white = navy with { Key = new AssetKey(AssetEntityKind.Team, "NGG"), Label = "NG", PrimaryColour = "#FFFFFF", SecondaryColour = "#7FC8F8" };
        var resolver = new AssetResolver(() => new AssetCatalog([navy, white]));

        var first = resolver.Fallback(navy.Key, AssetSlot.Logo(64));
        var second = resolver.Fallback(white.Key, AssetSlot.Logo(64));

        Assert.IsType<DrawingImage>(first);
        Assert.NotSame(first, second);
        Assert.Same(first, resolver.Fallback(navy.Key, AssetSlot.Logo(64)));
        Assert.IsType<DrawingImage>(resolver.Fallback(new AssetKey(AssetEntityKind.Player, "nobody"), AssetSlot.Headshot(160)));
    }

    private static AssetKey Player(string id) => new(AssetEntityKind.Player, id);

    private static AssetResolver Resolver(IImageDecoder decoder, params AssetEntry[] entries) =>
        new(() => new AssetCatalog(entries), decoder);

    private static bool TryLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>An image that is not an AvaloniaObject, so a plain test can use it off the UI thread.</summary>
    private sealed class PlainImage : IImage
    {
        public global::Avalonia.Size Size => new(1, 1);

        public void Draw(DrawingContext context, global::Avalonia.Rect sourceRect, global::Avalonia.Rect destRect)
        {
        }
    }

    /// <summary>The real decoder, slowed down, recording which threads it ran on.</summary>
    private sealed class RecordingDecoder(TimeSpan delay = default) : IImageDecoder
    {
        private readonly SkiaImageDecoder _inner = new();
        private int _probes;

        public int Probes => _probes;

        public System.Collections.Concurrent.ConcurrentBag<int> Threads { get; } = [];

        public ImageProbe? Probe(byte[] bytes)
        {
            Interlocked.Increment(ref _probes);
            Threads.Add(Environment.CurrentManagedThreadId);
            return _inner.Probe(bytes);
        }

        public IImage Decode(byte[] bytes, int width, int height)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }

            return _inner.Decode(bytes, width, height);
        }
    }
}
