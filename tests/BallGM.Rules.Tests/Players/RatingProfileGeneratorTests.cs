using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Players;

namespace BallGM.Rules.Tests.Players;

public sealed class RatingProfileGeneratorTests
{
    [Fact]
    public void GenerateIsDeterministicForTheSameSeed()
    {
        var first = RatingProfileGenerator.Generate(65, Position.SmallForward, 24, 30, 100, new SeededRandomSource(42));
        var second = RatingProfileGenerator.Generate(65, Position.SmallForward, 24, 30, 100, new SeededRandomSource(42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void EveryAttributeStaysWithinTheCallersBounds_AndCurrentAbilityLandsOnTheTalentAskedFor()
    {
        var random = new SeededRandomSource(7);
        for (var i = 0; i < 500; i++)
        {
            var talent = 45 + (i % 40);
            var rating = RatingProfileGenerator.Generate(talent, (Position)(1 + (i % 5)), 20 + (i % 14), 30, 100, random);

            Assert.All(rating.Visible, value => Assert.InRange(value, 30, 100));
            Assert.InRange(rating.Overall, talent - 2, talent + 1);
            Assert.InRange(rating.PotentialAbility, rating.Overall, 100);
            Assert.Equal(PositionFamiliarity.Natural, rating.Positions[(Position)(1 + (i % 5))]);
        }
    }

    [Fact]
    public void ACentreIsBiggerInsideAndAPointGuardBetterWithTheBall()
    {
        var random = new SeededRandomSource(11);
        var centres = Enumerable.Range(0, 400).Select(_ => RatingProfileGenerator.Generate(70, Position.Center, 26, 30, 100, random)).ToList();
        var guards = Enumerable.Range(0, 400).Select(_ => RatingProfileGenerator.Generate(70, Position.PointGuard, 26, 30, 100, random)).ToList();

        Assert.True(centres.Average(rating => rating.Body!.HeightCm) > guards.Average(rating => rating.Body!.HeightCm) + 15);
        foreach (var attribute in new[] { PlayerAttribute.Blocking, PlayerAttribute.DefensiveRebounding, PlayerAttribute.InteriorDefence })
        {
            Assert.True(centres.Average(rating => rating[attribute]) > guards.Average(rating => rating[attribute]) + 20, attribute.ToString());
        }

        foreach (var attribute in new[] { PlayerAttribute.BallHandling, PlayerAttribute.Passing, PlayerAttribute.ThreePoint })
        {
            Assert.True(guards.Average(rating => rating[attribute]) > centres.Average(rating => rating[attribute]) + 15, attribute.ToString());
        }
    }

    [Fact]
    public void AttributesThatShareAFactorMoveTogether()
    {
        var random = new SeededRandomSource(20261010);
        var sample = Enumerable.Range(0, 4000).Select(_ => RatingProfileGenerator.Generate(65, Position.SmallForward, 26, 0, 100, random)).ToList();

        double Between(PlayerAttribute a, PlayerAttribute b) =>
            Correlation(sample.Select(rating => (double)rating[a]).ToArray(), sample.Select(rating => (double)rating[b]).ToArray());

        Assert.True(Between(PlayerAttribute.ThreePoint, PlayerAttribute.MidRange) > 0.3);
        Assert.True(Between(PlayerAttribute.Speed, PlayerAttribute.FirstStep) > 0.3);
        Assert.True(Between(PlayerAttribute.OffensiveRebounding, PlayerAttribute.BoxingOut) > 0.3);
        Assert.True(Math.Abs(Between(PlayerAttribute.ThreePoint, PlayerAttribute.Blocking)) < 0.3);
    }

    /// <summary>
    /// sim-audit row 11: over a league-like talent spread and every position, the first principal
    /// component of the visible attributes carries 20–70% of the variance. Talent is shared by every
    /// attribute, so without real factors and archetypes it would carry nearly all of it.
    /// </summary>
    [Fact]
    public void TheFirstPrincipalComponentCarriesAShareOfVarianceInsideTheAuditBand()
    {
        var random = new SeededRandomSource(4500);
        var sample = Enumerable.Range(0, 5000)
            .Select(i => RatingProfileGenerator.Generate(
                (random.NextInt32(40, 91) + random.NextInt32(40, 91)) / 2,
                (Position)(1 + (i % 5)),
                20 + (i % 14),
                0,
                100,
                random))
            .ToList();

        var share = FirstComponentShare(sample.Select(rating => rating.Visible.Select(value => (double)value).ToArray()).ToList());

        Assert.InRange(share, 0.20, 0.70);
    }

    [Fact]
    public void YoungPlayersHaveRoomToGrowAndVeteransDoNot()
    {
        var random = new SeededRandomSource(5);
        double Headroom(int age) => Enumerable.Range(0, 300)
            .Select(_ => RatingProfileGenerator.Generate(60, Position.ShootingGuard, age, 0, 100, random))
            .Average(rating => rating.PotentialAbility - rating.Overall);

        Assert.True(Headroom(19) > 10);
        Assert.Equal(0, Headroom(31));
    }

    [Fact]
    public void AnOlderPacksFiveAttributesExpandIntoTheSameFullSheetEveryTime()
    {
        var first = RatingProfileGenerator.FromLegacy(90, 70, 85, 60, 65, Position.Center, 27, seed: 99);
        var again = RatingProfileGenerator.FromLegacy(90, 70, 85, 60, 65, Position.Center, 27, seed: 99);

        Assert.Equal(first, again);
        Assert.Equal(70, first[PlayerAttribute.Speed]);
        Assert.Equal(65, first[PlayerAttribute.LateralQuickness]);
        Assert.Equal(85, first[PlayerAttribute.Strength]);
        Assert.Equal(60, first[PlayerAttribute.Passing]);
        Assert.Equal(200 + ((90 - 74) * 10 / 25), first.Body!.HeightCm);
    }

    [Fact]
    public void RunFailsWhenLowerBoundExceedsUpperBound()
    {
        Assert.Throws<ArgumentException>(() =>
            RatingProfileGenerator.Generate(50, Position.Center, 25, 60, 40, new SeededRandomSource(1)));
    }

    [Fact]
    public void RunFailsWhenSpreadIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RatingProfileGenerator.Generate(50, Position.Center, 25, 0, 100, spread: -1, new SeededRandomSource(1)));
    }

    /// <summary>The largest eigenvalue of the covariance matrix over its trace, by power iteration.</summary>
    private static double FirstComponentShare(IReadOnlyList<double[]> rows)
    {
        var dimensions = rows[0].Length;
        var means = Enumerable.Range(0, dimensions).Select(d => rows.Average(row => row[d])).ToArray();
        var covariance = new double[dimensions, dimensions];
        foreach (var row in rows)
        {
            for (var i = 0; i < dimensions; i++)
            {
                for (var j = 0; j < dimensions; j++)
                {
                    covariance[i, j] += (row[i] - means[i]) * (row[j] - means[j]) / rows.Count;
                }
            }
        }

        var vector = Enumerable.Repeat(1.0, dimensions).ToArray();
        var eigenvalue = 0.0;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var next = new double[dimensions];
            for (var i = 0; i < dimensions; i++)
            {
                for (var j = 0; j < dimensions; j++)
                {
                    next[i] += covariance[i, j] * vector[j];
                }
            }

            eigenvalue = Math.Sqrt(next.Sum(value => value * value));
            vector = next.Select(value => value / eigenvalue).ToArray();
        }

        var trace = Enumerable.Range(0, dimensions).Sum(i => covariance[i, i]);
        return eigenvalue / trace;
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
}
