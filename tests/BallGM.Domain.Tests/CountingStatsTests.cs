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
        var first = new PlayerStatLine(new PlayerId("P"), team, 30, 25, 2, 5, 4, 15, 9, 6, 3, 5, 4, started: true, plusMinus: 7, steals: 0, turnovers: 0, blocks: 0);
        var second = new PlayerStatLine(new PlayerId("P"), team, 20, 12, 1, 3, 2, 10, 5, 2, 1, 2, 1, started: false, plusMinus: -3, steals: 0, turnovers: 0, blocks: 0);

        var total = CountingStats.Sum([first, second]);

        Assert.Equal(new CountingStats(2, 1, 50, 37, 14, 25, 4, 8, 5, 7, 3, 8, 6, 4, 0, 0, 0), total);
        Assert.Equal(11, total.Rebounds);
        Assert.Equal(10, total.TwoPointsMade);
        Assert.Equal(17, total.TwoPointsAttempted);
    }
}
