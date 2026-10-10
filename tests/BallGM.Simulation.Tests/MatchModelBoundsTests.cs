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
    public void EveryFieldGoalPercentageTheEfficiencyTermsCanProduceStaysInsideTheStatedRange()
    {
        var widest = MatchModelBounds.MaximumStrengthEfficiencySwing +
            MatchModelBounds.HomeCourtEfficiencyBonus +
            MatchModelBounds.MaximumFatiguePenalty;

        var (highestFg2, highestFg3) = Percentages(MatchModelBounds.BaseOffensiveEfficiency + widest);
        var (lowestFg2, lowestFg3) = Percentages(MatchModelBounds.BaseOffensiveEfficiency - widest);

        // The clamp is the backstop, not the mechanism: the terms alone should already land inside
        // it, so the clamp only ever catches a future tuning mistake.
        Assert.InRange(highestFg2, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage);
        Assert.InRange(highestFg3, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage);
        Assert.InRange(lowestFg2, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage);
        Assert.InRange(lowestFg3, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage);
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
            MatchModelBounds.OffensiveReboundRate * 2 < MatchModelBounds.ProbabilityScale,
            "A miss is rebounded by the defence more often than by the team that took the shot.");

        Assert.InRange(MatchModelBounds.TeamReboundShare, 1, MatchModelBounds.ProbabilityScale / 10);

        // Every possession provably ends: a run of offensive rebounds is capped, and the cap is far
        // past anything the rebound rate makes plausible (each extra shot needs another offensive board).
        Assert.InRange(MatchModelBounds.MaximumShotsPerPossession, 4, 20);

        Assert.InRange(MatchModelBounds.AssistChance, 1, MatchModelBounds.ProbabilityScale);
        Assert.InRange(MatchModelBounds.ThreePointAttemptShare, 1, MatchModelBounds.ProbabilityScale);
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

    private static (int Fg2Percent, int Fg3Percent) Percentages(int efficiency)
    {
        var delta = efficiency - MatchModelBounds.BaseOffensiveEfficiency;
        var swing = delta * MatchModelBounds.FieldGoalPercentSwingAtMaximumStrength /
            MatchModelBounds.MaximumStrengthEfficiencySwing;

        var fg2 = Math.Clamp(
            MatchModelBounds.BaseTwoPointPercentage + swing,
            MatchModelBounds.MinimumFieldGoalPercentage,
            MatchModelBounds.MaximumFieldGoalPercentage);

        var fg3 = Math.Clamp(
            MatchModelBounds.BaseThreePointPercentage + swing,
            MatchModelBounds.MinimumFieldGoalPercentage,
            MatchModelBounds.MaximumFieldGoalPercentage);

        return (fg2, fg3);
    }

    [Fact]
    public void ProbabilitiesAreBasisPointsTheRandomSourceDrawsDirectly()
    {
        // The engine draws every probability with SeededRandomSource.NextBasisPoint, which is only
        // the same as NextInt32(0, ProbabilityScale) while the two scales agree.
        Assert.Equal(BallGM.Domain.Randomness.IRandomSource.BasisPointScale, MatchModelBounds.ProbabilityScale);

        var fast = new BallGM.Domain.Randomness.SeededRandomSource(42);
        var general = new BallGM.Domain.Randomness.SeededRandomSource(42);
        for (var draw = 0; draw < 10_000; draw++)
        {
            Assert.Equal(general.NextInt32(0, MatchModelBounds.ProbabilityScale), fast.NextBasisPoint());
        }
    }

    [Fact]
    public void TheTurnoverChanceStaysARealButMinorityOutcome()
    {
        // The rate is the base moved by two independently capped terms, so its whole range is the
        // base plus or minus both caps: never zero, never more than a quarter of possessions.
        var lowest = MatchModelBounds.BaseTurnoverRate - MatchModelBounds.MaximumTurnoverPassingSwing - MatchModelBounds.MaximumTurnoverQuicknessSwing;
        var highest = MatchModelBounds.BaseTurnoverRate + MatchModelBounds.MaximumTurnoverPassingSwing + MatchModelBounds.MaximumTurnoverQuicknessSwing;

        Assert.True(lowest > MatchModelBounds.ProbabilityScale / 20, "Even the best-protected possession can be turned over.");
        Assert.True(highest < MatchModelBounds.ProbabilityScale / 4, "No mismatch turns the ball over on a quarter of possessions.");
        Assert.True(MatchModelBounds.MaximumTurnoverPassingSwing + MatchModelBounds.MaximumTurnoverQuicknessSwing < MatchModelBounds.BaseTurnoverRate / 2,
            "Shape redistributes turnovers; it does not decide whether a team has any.");
    }

    [Fact]
    public void TheTurnoverAndStealCapsAreReachableByARealSpecialist()
    {
        // A specialist on a 0-100 scale sits well over 20 points from his own Overall in one
        // attribute; each shape cap must bind before that, or it is dead code.
        const int SpecialistShape = 20;

        Assert.True(SpecialistShape * MatchModelBounds.TurnoverPerPassingPoint >= MatchModelBounds.MaximumTurnoverPassingSwing);
        Assert.True(SpecialistShape * 2 * MatchModelBounds.TurnoverPerQuicknessPoint >= MatchModelBounds.MaximumTurnoverQuicknessSwing,
            "Two fives whose quickness shapes differ by a specialist's margin either way reach the cap.");
        Assert.True(100 + (SpecialistShape * 2 * MatchModelBounds.StealWeightPerQuicknessPoint) >= MatchModelBounds.MaximumStealWeight);
        Assert.True(100 - (SpecialistShape * MatchModelBounds.StealWeightPerQuicknessPoint) <= MatchModelBounds.MinimumStealWeight);
    }

    [Fact]
    public void BlocksRelabelAMinorityOfMissesAndTheirCapsAreReachable()
    {
        // A block chance is a share of misses, so even the tallest mismatch blocks a minority of twos,
        // and a three is always rarer to block than a two.
        Assert.True(MatchModelBounds.BlockChanceOnTwoPointMiss - MatchModelBounds.MaximumBlockHeightSwing > 0);
        Assert.True(MatchModelBounds.BlockChanceOnTwoPointMiss + MatchModelBounds.MaximumBlockHeightSwing < MatchModelBounds.ProbabilityScale / 4);
        Assert.True(MatchModelBounds.BlockChanceOnThreePointMiss < MatchModelBounds.BlockChanceOnTwoPointMiss - MatchModelBounds.MaximumBlockHeightSwing);
        Assert.InRange(MatchModelBounds.BlockedOutOfBoundsShare, 1, MatchModelBounds.ProbabilityScale - 1);

        // Two fives whose height shapes differ by a specialist's margin reach the swing cap, and one
        // specialist reaches each weight cap.
        const int SpecialistShape = 20;
        Assert.True(SpecialistShape * MatchModelBounds.BlockPerHeightPoint >= MatchModelBounds.MaximumBlockHeightSwing);
        Assert.True(100 + (SpecialistShape * 2 * MatchModelBounds.BlockWeightPerHeightPoint) >= MatchModelBounds.MaximumBlockWeight);
        Assert.True(100 - (SpecialistShape * MatchModelBounds.BlockWeightPerHeightPoint) <= MatchModelBounds.MinimumBlockWeight);
    }

    [Fact]
    public void PositionalWeightsPutTheRightPlayerOnHandForEachEvent()
    {
        var positions = Enum.GetValues<BallGM.Domain.Players.Position>();
        Assert.All(positions, position =>
        {
            Assert.True(MatchModelBounds.ReboundWeightFor(position) > 0);
            Assert.True(MatchModelBounds.AssistWeightFor(position) > 0);
            Assert.True(MatchModelBounds.BlockWeightFor(position) > 0);
        });

        // The big man is at the rim, the point guard has the ball.
        Assert.Equal(BallGM.Domain.Players.Position.Center, positions.MaxBy(MatchModelBounds.BlockWeightFor));
        Assert.Equal(BallGM.Domain.Players.Position.Center, positions.MaxBy(MatchModelBounds.ReboundWeightFor));
        Assert.Equal(BallGM.Domain.Players.Position.PointGuard, positions.MaxBy(MatchModelBounds.AssistWeightFor));

        // Blocks are more positional than rebounds: the spread between the extremes is wider.
        Assert.True(
            positions.Max(MatchModelBounds.BlockWeightFor) * positions.Min(MatchModelBounds.ReboundWeightFor) >
            positions.Max(MatchModelBounds.ReboundWeightFor) * positions.Min(MatchModelBounds.BlockWeightFor));
    }

    [Fact]
    public void FoulTermsKeepFoulsCommonButFoulOutsRare()
    {
        // A foul away from the shot is a minority of possessions, an offensive foul a minority of lost
        // balls, and foul trouble damps but never removes a defender's fouls.
        Assert.InRange(MatchModelBounds.NonShootingFoulRate, 1, MatchModelBounds.ProbabilityScale / 5);
        Assert.InRange(MatchModelBounds.OffensiveFoulShareOfLostBalls, 1, MatchModelBounds.ProbabilityScale / 2);
        Assert.InRange(MatchModelBounds.FoulTroubleWeightPercent, 1, 99);
        Assert.InRange(MatchModelBounds.FoulTroubleMargin, 1, BallGM.Rules.Configuration.GameRules.Default.PersonalFoulLimit - 1);

        // Bigs foul more than guards; and a specialist's quickness shape reaches both weight caps.
        Assert.True(MatchModelBounds.FoulWeightFor(BallGM.Domain.Players.Position.Center) > MatchModelBounds.FoulWeightFor(BallGM.Domain.Players.Position.PointGuard));
        const int SpecialistShape = 20;
        Assert.True(100 + (SpecialistShape * 2 * MatchModelBounds.FoulWeightPerQuicknessPoint) >= MatchModelBounds.MaximumFoulWeight);
        Assert.True(100 - (SpecialistShape * MatchModelBounds.FoulWeightPerQuicknessPoint) <= MatchModelBounds.MinimumFoulWeight);
    }

    [Fact]
    public void ShotZonesRedistributeTheTwoAndThreeSplitWithoutMovingIt()
    {
        Assert.Equal(MatchModelBounds.ProbabilityScale,
            MatchModelBounds.RimShare + MatchModelBounds.PaintShare + MatchModelBounds.MidRangeShare + MatchModelBounds.Corner3Share + MatchModelBounds.AboveBreak3Share);
        Assert.Equal(MatchModelBounds.ThreePointAttemptShare, MatchModelBounds.Corner3Share + MatchModelBounds.AboveBreak3Share);

        // The offsets are zero-mean over each kind's neutral shares (to within a basis point an attempt).
        var twos = (MatchModelBounds.RimShare * MatchModelBounds.RimPercentageOffset) + (MatchModelBounds.PaintShare * MatchModelBounds.PaintPercentageOffset) +
            (MatchModelBounds.MidRangeShare * MatchModelBounds.MidRangePercentageOffset);
        var threes = (MatchModelBounds.Corner3Share * MatchModelBounds.Corner3PercentageOffset) + (MatchModelBounds.AboveBreak3Share * MatchModelBounds.AboveBreak3PercentageOffset);
        // Strictly: under a tenth of a basis point per attempt.
        var twoShares = MatchModelBounds.RimShare + MatchModelBounds.PaintShare + MatchModelBounds.MidRangeShare;
        Assert.InRange(twos, -twoShares / 10, twoShares / 10);

        // A neutral zone sits well inside the named clamps, which bind only in lopsided matchups.
        foreach (var offset in new[] { MatchModelBounds.RimPercentageOffset, MatchModelBounds.PaintPercentageOffset, MatchModelBounds.MidRangePercentageOffset })
        {
            Assert.InRange(MatchModelBounds.BaseTwoPointPercentage + offset, MatchModelBounds.MinimumFieldGoalPercentage + 1_000, MatchModelBounds.MaximumFieldGoalPercentage - 1_000);
        }

        foreach (var offset in new[] { MatchModelBounds.Corner3PercentageOffset, MatchModelBounds.AboveBreak3PercentageOffset })
        {
            Assert.InRange(MatchModelBounds.BaseThreePointPercentage + offset, MatchModelBounds.MinimumFieldGoalPercentage + 1_000, MatchModelBounds.MaximumFieldGoalPercentage - 1_000);
        }
        Assert.Equal(0, threes);

        // The height shift never empties a zone, and a specialist reaches both caps.
        Assert.True(MatchModelBounds.MaximumZoneMixShift < Math.Min(MatchModelBounds.RimShare, MatchModelBounds.AboveBreak3Share));
        const int SpecialistShape = 20;
        Assert.True(SpecialistShape * MatchModelBounds.ZoneMixPerHeightPoint >= MatchModelBounds.MaximumZoneMixShift);
        Assert.True(SpecialistShape * MatchModelBounds.ZonePercentagePerHeightPoint >= MatchModelBounds.MaximumZonePercentageSwing);
    }
}
