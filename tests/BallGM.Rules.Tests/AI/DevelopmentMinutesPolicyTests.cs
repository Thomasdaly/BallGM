using BallGM.Domain.Seasons;
using BallGM.Rules.AI;

namespace BallGM.Rules.Tests.AI;

public sealed class DevelopmentMinutesPolicyTests
{
    [Fact]
    public void ATankingTeamPlaysItsYoungPlayersAndSitsItsVeterans()
    {
        var tanked = new TeamRecord(20, 62);

        Assert.True(DevelopmentMinutesPolicy.PriorityFor(tanked, 21) > 0);
        Assert.True(DevelopmentMinutesPolicy.PriorityFor(tanked, 33) < 0);
        Assert.Equal(0, DevelopmentMinutesPolicy.PriorityFor(tanked, 27));
    }

    [Fact]
    public void ATeamPlayingToWin_OrWithNoRecordYet_PlaysItsBest()
    {
        Assert.Equal(0, DevelopmentMinutesPolicy.PriorityFor(new TeamRecord(41, 41), 21));
        Assert.Equal(0, DevelopmentMinutesPolicy.PriorityFor(null, 21));
        Assert.Equal(0, DevelopmentMinutesPolicy.PriorityFor(TeamRecord.None, 33));
    }
}
