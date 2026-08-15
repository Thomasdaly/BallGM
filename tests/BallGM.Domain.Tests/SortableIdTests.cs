using BallGM.Domain.Common;

namespace BallGM.Domain.Tests;

public sealed class SortableIdTests
{
    [Fact]
    public void NewIdProducesTwentySixCrockfordBase32Characters()
    {
        var id = SortableId.NewId();

        Assert.Equal(26, id.Length);
        Assert.All(id, c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
    }

    [Fact]
    public void NewIdIsUniqueAcrossManyCallsAtTheSameInstant()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 1_000).Select(_ => SortableId.NewId(timestamp)).ToHashSet();

        Assert.Equal(1_000, ids.Count);
    }

    [Fact]
    public void NewIdSortsLexicographicallyByTimestamp()
    {
        var earlier = SortableId.NewId(DateTimeOffset.UtcNow.AddMinutes(-1));
        var later = SortableId.NewId(DateTimeOffset.UtcNow);

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void NewIdRejectsTimestampsBeforeTheUnixEpoch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SortableId.NewId(DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
    }
}
