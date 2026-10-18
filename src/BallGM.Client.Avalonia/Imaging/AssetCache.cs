using Avalonia.Media;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// Least-recently-used decoded images keyed by (file, bucket) and sized by estimated bytes (w x h x 4),
/// within a budget (spec 05 section 4). Evicted images are not disposed: one may still be on screen,
/// so they are left for the garbage collector (docs/ui/DESIGN_SYSTEM.md, image disposal).
/// </summary>
public sealed class AssetCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Path, int Bucket), LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _order = new();
    private long _bytes;
    private long _peakBytes;

    public AssetCache(long budgetBytes)
    {
        BudgetBytes = budgetBytes;
    }

    private sealed record Entry((string Path, int Bucket) Key, IImage Image, long Bytes);

    public long BudgetBytes { get; }

    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>The largest size the cache has held since it was created or cleared.</summary>
    public long PeakBytes
    {
        get
        {
            lock (_gate)
            {
                return _peakBytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(string path, int bucket, out IImage image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((path, bucket), out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null!;
        return false;
    }

    public void Add(string path, int bucket, IImage image, long bytes)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((path, bucket), out var existing))
            {
                _order.Remove(existing);
                _bytes -= existing.Value.Bytes;
            }

            var node = _order.AddFirst(new Entry((path, bucket), image, bytes));
            _map[(path, bucket)] = node;
            _bytes += bytes;
            while (_bytes > BudgetBytes && _order.Last is { } last && last != node)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }

            _peakBytes = Math.Max(_peakBytes, _bytes);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
            _bytes = 0;
            _peakBytes = 0;
        }
    }
}
