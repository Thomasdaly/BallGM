using BallGM.Application.Players;

namespace BallGM.Application.Tests;

public sealed class AbilityStarsTests
{
    [Fact]
    public void AbilityReadsAsHalfStarsFromHalfAStarToFive()
    {
        Assert.Equal(1, AbilityStars.HalfStars(20));
        Assert.Equal(1, AbilityStars.HalfStars(36));
        Assert.Equal(2, AbilityStars.HalfStars(37));
        Assert.Equal(8, AbilityStars.HalfStars(80));
        Assert.Equal(9, AbilityStars.HalfStars(86));
        Assert.Equal(10, AbilityStars.HalfStars(93));
        Assert.Equal(AbilityStars.MaximumHalfStars, AbilityStars.HalfStars(100));
    }
}
