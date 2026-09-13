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

    [Fact]
    public void TheFlatConstructorSetsEveryAttributeToTheSameValue()
    {
        var rating = new PlayerRating(overall: 72);

        Assert.Equal(72, rating.Height);
        Assert.Equal(72, rating.Speed);
        Assert.Equal(72, rating.Strength);
        Assert.Equal(72, rating.Passing);
        Assert.Equal(72, rating.LateralQuickness);
        Assert.Equal(72, rating.Overall);
    }

    [Fact]
    public void OverallIsTheMeanOfTheFiveAttributes()
    {
        var rating = new PlayerRating(height: 90, speed: 60, strength: 70, passing: 80, lateralQuickness: 50);

        Assert.Equal((90 + 60 + 70 + 80 + 50) / 5, rating.Overall);
    }

    [Fact]
    public void ThePrimaryConstructorThrowsWhenAnAttributeFallsOutsideTheScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlayerRating(height: 101, speed: 50, strength: 50, passing: 50, lateralQuickness: 50));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlayerRating(height: 50, speed: -1, strength: 50, passing: 50, lateralQuickness: 50));
    }

    [Fact]
    public void AdjustShiftsEveryAttributeByTheSameDelta()
    {
        var rating = new PlayerRating(height: 60, speed: 50, strength: 40, passing: 55, lateralQuickness: 45);
        var adjusted = rating.Adjust(5);

        Assert.Equal(65, adjusted.Height);
        Assert.Equal(55, adjusted.Speed);
        Assert.Equal(45, adjusted.Strength);
        Assert.Equal(60, adjusted.Passing);
        Assert.Equal(50, adjusted.LateralQuickness);
    }

    [Fact]
    public void AdjustClampsEachAttributeIndependently()
    {
        var rating = new PlayerRating(height: 98, speed: 5, strength: 50, passing: 50, lateralQuickness: 50);
        var adjusted = rating.Adjust(10);

        Assert.Equal(PlayerRating.MaximumOverall, adjusted.Height);
        Assert.Equal(15, adjusted.Speed);
    }
}
