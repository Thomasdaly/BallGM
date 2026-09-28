using BallGM.Application.Players;

namespace BallGM.Application.Tests;

/// <summary>The derived statistics, checked against hand-computed values from their glossary definitions.</summary>
public sealed class StatFormulasTests
{
    [Fact]
    public void TrueShooting_CountsFreeThrowsAtPointFourFour()
    {
        // 30 / (2 × (20 + 0.44 × 5)) = 30 / 44.4
        Assert.Equal(30 / 44.4, StatFormulas.TrueShooting(30, 20, 5)!.Value, 10);
    }

    [Fact]
    public void EffectiveFieldGoal_CountsAThreeAsOneAndAHalf()
    {
        // (8 + 0.5 × 4) / 20 = 0.5
        Assert.Equal(0.5, StatFormulas.EffectiveFieldGoal(8, 4, 20));
    }

    [Fact]
    public void PointsDistribution_SplitsPointsBySource()
    {
        // 10 FGM of which 4 threes = 12 two-point + 12 three-point, plus 6 FT = 30 points.
        var split = StatFormulas.PointsDistribution(10, 4, 6, 30)!.Value;

        Assert.Equal(0.4, split.TwoPoint, 10);
        Assert.Equal(0.4, split.ThreePoint, 10);
        Assert.Equal(0.2, split.FreeThrow, 10);
    }

    [Fact]
    public void AssistPercent_UsesTheMinutesShareOfTeamFieldGoals()
    {
        // Played 24 of 240 team minutes (a fifth of a 48-minute slot × 5 = half the floor time):
        // (24 / (240 / 5)) × 40 team FGM − 6 own FGM = 14 team-mate baskets; 7 assists = 50%.
        Assert.Equal(0.5, StatFormulas.AssistPercent(7, 24, 240, 40, 6));
    }

    [Fact]
    public void ReboundPercent_UsesTheMinutesShareOfAvailableRebounds()
    {
        // 10 rebounds × (240 / 5) / (48 × 100 available) = 10%
        Assert.Equal(0.1, StatFormulas.ReboundPercent(10, 48, 240, 100));
    }

    [Fact]
    public void RatesWithNoDenominator_AreNotZeroButAbsent()
    {
        Assert.Null(StatFormulas.Percent(0, 0));
        Assert.Null(StatFormulas.TrueShooting(0, 0, 0));
        Assert.Null(StatFormulas.Per36(10, 0));
        Assert.Null(StatFormulas.PointsDistribution(0, 0, 0, 0));
    }
}
