using BallGM.Domain.Players;

namespace BallGM.Domain.Tests;

public sealed class PlayerRatingTests
{
    [Fact]
    public void AdjustAppliesADeltaWithinRange()
    {
        var rating = new PlayerRating(overall: 50).Adjust(5);

        Assert.Equal(55, rating.Overall);
    }

    [Fact]
    public void AdjustClampsAtTheTopOfTheScale()
    {
        var rating = new PlayerRating(overall: 98).Adjust(10);

        Assert.Equal(PlayerRating.MaximumOverall, rating.Overall);
    }

    [Fact]
    public void AdjustClampsAtTheBottomOfTheScale()
    {
        var rating = new PlayerRating(overall: 2).Adjust(-10);

        Assert.Equal(PlayerRating.MinimumOverall, rating.Overall);
    }
}
