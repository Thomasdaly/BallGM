using BallGM.Application.Players;

namespace BallGM.Application.Tests;

/// <summary>The derived statistics, checked against hand-computed values from their glossary definitions.</summary>
public sealed class StatFormulasTests
{
    [Fact]
    public void Scaled_ShowsATotalPerGameOrPer36()
    {
        var stats = BallGM.Domain.Seasons.CountingStats.None with { Games = 4, Minutes = 72, Points = 50 };

        Assert.Equal(50, StatFormulas.Scaled(stats.Points, stats, StatBasis.Totals));
        Assert.Equal(12.5, StatFormulas.Scaled(stats.Points, stats, StatBasis.PerGame));
        Assert.Equal(25, StatFormulas.Scaled(stats.Points, stats, StatBasis.Per36));
        Assert.Null(StatFormulas.Scaled(0, BallGM.Domain.Seasons.CountingStats.None, StatBasis.PerGame));
    }

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

    [Fact]
    public void PossessionsRatingsUsageStealsAndFourFactors_FollowTheirGlossaryDefinitions()
    {
        var team = BallGM.Domain.Seasons.CountingStats.None with
        {
            Games = 1,
            Minutes = 240,
            Points = 110,
            FieldGoalsMade = 40,
            FieldGoalsAttempted = 88,
            ThreePointsMade = 12,
            FreeThrowsMade = 18,
            FreeThrowsAttempted = 25,
            OffensiveRebounds = 10,
            DefensiveRebounds = 33,
            Turnovers = 14,
            Assists = 25,
        };
        var opponent = team with { Points = 104, DefensiveRebounds = 30, OffensiveRebounds = 9, Turnovers = 15 };
        var player = BallGM.Domain.Seasons.CountingStats.None with
        {
            Games = 1,
            Minutes = 36,
            FieldGoalsAttempted = 20,
            FreeThrowsAttempted = 5,
            Turnovers = 3,
            Steals = 2,
            Assists = 8,
        };

        Assert.Equal(88 + (0.44 * 25) - 10 + 14, StatFormulas.Possessions(team), 10);
        Assert.Equal(100.0 * 110 / 103, StatFormulas.Rating(team)!.Value, 10);
        Assert.Equal(3 / (20 + 2.2 + 3), StatFormulas.TurnoverPercent(player)!.Value, 10);
        Assert.Equal(8 / 3.0, StatFormulas.AssistToTurnover(player)!.Value, 10);
        Assert.Equal((20 + 2.2 + 3) * 48 / (36 * (88 + 11.0 + 14)), StatFormulas.UsagePercent(player, team)!.Value, 10);
        Assert.Equal(2 * 48 / (36 * StatFormulas.Possessions(opponent)), StatFormulas.StealPercent(player, team, opponent)!.Value, 10);

        var factors = StatFormulas.FourFactorsFor(team, opponent);
        Assert.Equal((40 + 6) / 88.0, factors.EffectiveFieldGoal!.Value, 10);
        Assert.Equal(10 / 40.0, factors.OffensiveReboundPercent!.Value, 10);
        Assert.Equal(18 / 88.0, factors.FreeThrowsPerFieldGoal!.Value, 10);
        Assert.Null(StatFormulas.UsagePercent(BallGM.Domain.Seasons.CountingStats.None, team));
    }
}
