using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class CountingStatsTests
{
    [Fact]
    public void SummingGameLinesKeepsEveryCountingStat()
    {
        var team = new TeamId("T");
        var first = new PlayerStatLine(new PlayerId("P"), team, 30, 25, 2, 5, 4, 30, 15, 9, 6, 3, 5, 4, started: true);
        var second = new PlayerStatLine(new PlayerId("P"), team, 20, 12, 1, 3, 2, 20, 10, 5, 2, 1, 2, 1, started: false);

        var total = CountingStats.Sum([first, second]);

        Assert.Equal(new CountingStats(2, 1, 50, 37, 14, 25, 4, 8, 5, 7, 3, 8, 6), total);
        Assert.Equal(11, total.Rebounds);
        Assert.Equal(10, total.TwoPointsMade);
        Assert.Equal(17, total.TwoPointsAttempted);
    }
}
