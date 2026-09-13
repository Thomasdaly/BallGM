using BallGM.Domain.Randomness;
using BallGM.Rules.Players;

namespace BallGM.Rules.Tests.Players;

public sealed class RatingProfileGeneratorTests
{
    [Fact]
    public void GenerateIsDeterministicForTheSameSeed()
    {
        var first = RatingProfileGenerator.Generate(65, 30, 100, new SeededRandomSource(42));
        var second = RatingProfileGenerator.Generate(65, 30, 100, new SeededRandomSource(42));

        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Speed, second.Speed);
        Assert.Equal(first.Strength, second.Strength);
        Assert.Equal(first.Passing, second.Passing);
        Assert.Equal(first.LateralQuickness, second.LateralQuickness);
    }

    [Fact]
    public void EveryAttributeAndOverallStayWithinTheCallersBounds()
    {
        var random = new SeededRandomSource(7);

        for (var i = 0; i < 500; i++)
        {
            var rating = RatingProfileGenerator.Generate(60, 40, 90, random);

            Assert.InRange(rating.Height, 40, 90);
            Assert.InRange(rating.Speed, 40, 90);
            Assert.InRange(rating.Strength, 40, 90);
            Assert.InRange(rating.Passing, 40, 90);
            Assert.InRange(rating.LateralQuickness, 40, 90);
            Assert.InRange(rating.Overall, 40, 90);
        }
    }

    [Fact]
    public void ZeroSpreadProducesAFlatRatingAtTalentWithNoRandomDraw()
    {
        var rating = RatingProfileGenerator.Generate(55, 0, 100, spread: 0, new ThrowingRandomSource());

        Assert.Equal(55, rating.Height);
        Assert.Equal(55, rating.Speed);
        Assert.Equal(55, rating.Strength);
        Assert.Equal(55, rating.Passing);
        Assert.Equal(55, rating.LateralQuickness);
    }

    [Fact]
    public void RunFailsWhenLowerBoundExceedsUpperBound()
    {
        Assert.Throws<ArgumentException>(() =>
            RatingProfileGenerator.Generate(50, 60, 40, new SeededRandomSource(1)));
    }

    [Fact]
    public void RunFailsWhenSpreadIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RatingProfileGenerator.Generate(50, 0, 100, spread: -1, new SeededRandomSource(1)));
    }

    /// <summary>
    /// The shared "frame" draw is what gives height a real, structural correlation with the other
    /// size-linked attributes — a taller-skewed player should read as slower and less agile on
    /// average, not merely differently random. Measured over a large sample rather than asserted on
    /// one draw, since any single draw's frame could land near zero.
    /// </summary>
    [Fact]
    public void HeightAndLateralQuicknessAreNegativelyCorrelatedAcrossManyDraws()
    {
        var random = new SeededRandomSource(20260913);
        const int n = 5000;
        var heights = new double[n];
        var laterals = new double[n];

        for (var i = 0; i < n; i++)
        {
            var rating = RatingProfileGenerator.Generate(65, 30, 100, random);
            heights[i] = rating.Height;
            laterals[i] = rating.LateralQuickness;
        }

        Assert.True(
            Correlation(heights, laterals) < -0.1,
            "Height and lateral quickness should be measurably negatively correlated across a large sample.");
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        double covariance = 0;
        double varX = 0;
        double varY = 0;

        for (var i = 0; i < x.Length; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            covariance += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        return covariance / Math.Sqrt(varX * varY);
    }

    private sealed class ThrowingRandomSource : IRandomSource
    {
        public int NextInt32(int minInclusive, int maxExclusive) =>
            throw new InvalidOperationException("No randomness should have been drawn.");
    }
}
