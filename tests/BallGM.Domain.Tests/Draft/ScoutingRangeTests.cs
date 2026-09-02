using BallGM.Domain.Draft;

namespace BallGM.Domain.Tests.Draft;

public sealed class ScoutingRangeTests
{
    [Fact]
    public void CreateSucceedsWithAValidBand()
    {
        var result = ScoutingRange.Create(60, 80, confidence: 40);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.LowerBound);
        Assert.Equal(80, result.Value.UpperBound);
        Assert.Equal(40, result.Value.Confidence);
    }

    [Fact]
    public void CreateFailsWhenLowerBoundExceedsUpperBound()
    {
        var result = ScoutingRange.Create(80, 60, confidence: 40);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "scouting_range.lower_above_upper");
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, 101)]
    public void CreateFailsWhenABoundFallsOutsideTheRatingRange(int lower, int upper)
    {
        var result = ScoutingRange.Create(lower, upper, confidence: 50);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "scouting_range.bound_out_of_rating_range");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void CreateFailsWhenConfidenceIsOutOfRange(int confidence)
    {
        var result = ScoutingRange.Create(50, 60, confidence);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "scouting_range.confidence_out_of_range");
    }

    [Fact]
    public void CertainCollapsesTheBandOntoTheTrueValue()
    {
        var range = ScoutingRange.Certain(72);

        Assert.Equal(72, range.LowerBound);
        Assert.Equal(72, range.UpperBound);
        Assert.Equal(100, range.Confidence);
    }
}
