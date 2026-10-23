using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Development;
using BallGM.Rules.Players;

namespace BallGM.Rules.Tests.Development;

public sealed class AttributeDevelopmentModelTests
{
    private static readonly DevelopmentRules Rules = DevelopmentRules.Create(
        peakAgeStart: 26,
        peakAgeEnd: 29,
        growthCurve: BandedScale.Create([new ScaleBand(0, 5), new ScaleBand(22, 3)]).Value,
        declineCurve: BandedScale.Create([new ScaleBand(0, 0), new ScaleBand(30, 2), new ScaleBand(34, 5)]).Value,
        varianceRange: 0).Value;

    private static PlayerRating Prospect(int seed) => RatingProfileGenerator.Generate(55, Position.SmallForward, 19, 30, 100, new SeededRandomSource(seed));

    [Fact]
    public void AYoungPlayerGrowsTowardHisPotentialAndNoFurther()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var rating = Prospect(seed);
            for (var age = 19; age < 26; age++)
            {
                var grown = AttributeDevelopmentModel.Develop(rating, age, Rules, new SeededRandomSource(seed * 100 + age));
                Assert.True(grown.Overall >= rating.Overall - 1, $"Seed {seed}, age {age}: a growing player fell from {rating.Overall} to {grown.Overall}.");
                rating = grown;
            }

            Assert.True(rating.Overall <= rating.PotentialAbility + 1, $"Seed {seed}: grew to {rating.Overall} past his potential of {rating.PotentialAbility}.");
        }
    }

    [Fact]
    public void AnAgeingPlayerLosesHisLegsBeforeHisJumpShot()
    {
        var rating = RatingProfileGenerator.Generate(80, Position.ShootingGuard, 33, 30, 100, new SeededRandomSource(7));
        var start = rating;
        for (var age = 33; age < 37; age++)
        {
            rating = AttributeDevelopmentModel.Develop(rating, age, Rules, new SeededRandomSource(age));
        }

        var speedLost = start[PlayerAttribute.Speed] - rating[PlayerAttribute.Speed];
        var shootingLost = start[PlayerAttribute.ThreePoint] - rating[PlayerAttribute.ThreePoint];
        Assert.True(rating.Overall < start.Overall);
        Assert.True(speedLost > shootingLost * 3, $"Speed fell {speedLost}, the three-point shot {shootingLost}.");
        Assert.Equal(rating.Overall, rating.PotentialAbility);
    }

    [Fact]
    public void DevelopmentIsDeterministicAndAnUnconfiguredLeagueNeverAges()
    {
        var rating = Prospect(3);
        Assert.Equal(
            AttributeDevelopmentModel.Develop(rating, 20, Rules, new SeededRandomSource(9)),
            AttributeDevelopmentModel.Develop(rating, 20, Rules, new SeededRandomSource(9)));
        Assert.Same(rating, AttributeDevelopmentModel.Develop(rating, 20, DevelopmentRules.None, new SeededRandomSource(9)));
    }

    [Fact]
    public void AnUnsignedFringePlayerRetiresFarMoreOftenThanAStarStillUnderContract()
    {
        var odds = BandedScale.Create([new ScaleBand(0, 0), new ScaleBand(30, 1_500)]).Value;
        var rules = RetirementRules.Create(30, 0, odds).Value;

        int Count(int percentile, bool underContract) => Enumerable.Range(0, 4_000)
            .Count(seed => RetirementModel.Assess(33, percentile, underContract, rules, new SeededRandomSource(seed)).Retires);

        var fringe = Count(percentile: 5, underContract: false);
        var star = Count(percentile: 98, underContract: true);
        Assert.True(fringe > star * 8, $"{fringe} fringe free agents and {star} contracted stars retired out of 4,000.");
    }
}
