using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>
/// The relationships between the model's bounds. This is what makes them bounds rather than magic
/// numbers with better names — <c>docs/competitive-feature-review.md</c> §7 is about a term that
/// could dominate an outcome without a cap, and a cap nobody checks is not a cap.
/// </summary>
public sealed class MatchModelBoundsTests
{
    [Fact]
    public void NoSingleTermCanDominateAGame()
    {
        // The whole §7 point, stated as arithmetic: every term that moves a team's efficiency is
        // small against the base it moves. If any one of them could approach the base, that term
        // alone would decide games.
        Assert.True(
            MatchModelBounds.MaximumStrengthEfficiencySwing * 2 < MatchModelBounds.BaseOffensiveEfficiency,
            "The strength swing must not be able to halve or double a team's efficiency on its own.");

        Assert.True(
            MatchModelBounds.HomeCourtEfficiencyBonus < MatchModelBounds.MaximumStrengthEfficiencySwing,
            "Playing at home must be worth less than being the better team.");

        Assert.True(
            MatchModelBounds.MaximumFatiguePenalty < MatchModelBounds.MaximumStrengthEfficiencySwing,
            "Rest must be worth less than talent.");

        Assert.True(
            MatchModelBounds.MaximumFatiguePenalty < MatchModelBounds.BaseOffensiveEfficiency / 10,
            "A tired team is worse, not beaten before it starts.");
    }

    [Fact]
    public void TheStrengthSwingIsReachableByARealMismatch()
    {
        // A bound nothing can reach is not a bound, it is dead code. The widest gap two teams on a
        // 0-100 scale can have must be able to hit the cap, or the cap is never doing any work.
        var widestGap = 100 * MatchModelBounds.EfficiencyPerRatingPoint / 2;

        Assert.True(
            widestGap > MatchModelBounds.MaximumStrengthEfficiencySwing,
            "The largest possible rating gap should be capped by the swing, otherwise the cap never binds.");
    }

    [Fact]
    public void EveryScoringRateTheEfficiencyTermsCanProduceStaysInsideTheStatedRange()
    {
        var widest = MatchModelBounds.MaximumStrengthEfficiencySwing +
            MatchModelBounds.HomeCourtEfficiencyBonus +
            MatchModelBounds.MaximumFatiguePenalty;

        var highest = Rate(MatchModelBounds.BaseOffensiveEfficiency + widest);
        var lowest = Rate(MatchModelBounds.BaseOffensiveEfficiency - widest);

        // The clamp is the backstop, not the mechanism: the terms alone should already land inside
        // it, so the clamp only ever catches a future tuning mistake.
        Assert.InRange(highest, MatchModelBounds.MinimumScoringRate, MatchModelBounds.MaximumScoringRate);
        Assert.InRange(lowest, MatchModelBounds.MinimumScoringRate, MatchModelBounds.MaximumScoringRate);
    }

    [Fact]
    public void PaceCannotDriftIntoADifferentSport()
    {
        Assert.True(MatchModelBounds.PossessionSpread > 0, "A league with no pace variance plays the same game every night.");

        Assert.True(
            MatchModelBounds.PossessionSpread * 4 < MatchModelBounds.BasePossessionsPerGame,
            "Pace variance must be a drift around the base, not a redefinition of it.");
    }

    [Fact]
    public void OvertimeIsShorterThanRegulationAndFinite()
    {
        Assert.True(MatchModelBounds.OvertimePossessions > 0);
        Assert.True(MatchModelBounds.OvertimePossessions < MatchModelBounds.BasePossessionsPerGame / 4);
        Assert.True(MatchModelBounds.MaximumOvertimePeriods > 0, "The overtime loop has to terminate.");
    }

    [Fact]
    public void UsageAndReboundingSharesAreCoherent()
    {
        Assert.True(MatchModelBounds.MinimumUsageFactor < 100);
        Assert.True(MatchModelBounds.MaximumUsageFactor > 100);

        Assert.True(
            MatchModelBounds.DefensiveReboundShare > MatchModelBounds.OffensiveReboundShare,
            "A miss is rebounded by the defence more often than by the team that took the shot.");

        Assert.True(
            MatchModelBounds.DefensiveReboundShare + MatchModelBounds.OffensiveReboundShare
                <= MatchModelBounds.ProbabilityScale,
            "A missed shot cannot be rebounded more than once.");

        Assert.InRange(MatchModelBounds.AssistShareOfMadeFieldGoals, 1, MatchModelBounds.ProbabilityScale);
        Assert.InRange(MatchModelBounds.ThreePointShareOfScores, 1, MatchModelBounds.ProbabilityScale);
    }

    [Fact]
    public void OneInjuryCannotEndASeason()
    {
        Assert.True(MatchModelBounds.MinimumInjuryDays >= 1, "An injury has to cost at least a day to be one.");

        Assert.True(
            MatchModelBounds.MaximumInjuryDays > MatchModelBounds.MinimumInjuryDays,
            "Injuries need a range to be drawn from.");

        // Two per cent a game for someone playing every available minute: across a season that is a
        // handful of knocks for the players carrying the load, not a treatment room. An injury model
        // a GM cannot plan around is noise rather than a system.
        const int TwoPerCent = MatchModelBounds.ProbabilityScale / 50;

        Assert.True(
            MatchModelBounds.InjuryChancePerFullGame < TwoPerCent,
            $"A full-minutes player's per-game injury risk is {MatchModelBounds.InjuryChancePerFullGame} basis points.");
    }

    private static int Rate(int efficiency) => Math.Clamp(
        efficiency * MatchModelBounds.ProbabilityScale /
            ((2 * MatchModelBounds.ProbabilityScale) + MatchModelBounds.ThreePointShareOfScores),
        MatchModelBounds.MinimumScoringRate,
        MatchModelBounds.MaximumScoringRate);
}
