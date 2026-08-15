using BallGM.Domain.Leagues;

namespace BallGM.Domain.Tests;

public sealed class SeasonTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsNonPositiveYear(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Season(year));
    }

    [Fact]
    public void ConstructorAcceptsAPositiveYear()
    {
        var season = new Season(2032);

        Assert.Equal(2032, season.Year);
    }
}
