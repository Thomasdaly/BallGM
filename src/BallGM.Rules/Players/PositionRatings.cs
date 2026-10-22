using BallGM.Domain.Players;

namespace BallGM.Rules.Players;

/// <summary>
/// How good a player is <em>at a position</em>: his attributes weighted the way that position uses them
/// (a point guard's handle and vision, a centre's rim protection and rebounding), less a penalty for how
/// little at home he is there. What a rotation is picked by — Current Ability is position-blind.
/// A flat sheet with no familiarity assessed rates its Overall everywhere.
/// </summary>
public static class PositionRatings
{
    /// <summary>The points a familiarity costs, from natural (none) to awkward.</summary>
    public static int FamiliarityPenalty(PositionFamiliarity familiarity) => familiarity switch
    {
        PositionFamiliarity.Natural => 0,
        PositionFamiliarity.Accomplished => 2,
        PositionFamiliarity.Competent => 5,
        PositionFamiliarity.Unconvincing => 10,
        _ => 18,
    };

    /// <summary>Each position's weight on each attribute, worked out once: Current Ability's plus the position's emphasis.</summary>
    private static readonly int[][] Weights = Enum.GetValues<Position>()
        .OrderBy(position => (int)position)
        .Select(position => PlayerAttributes.All.Select(attribute => PlayerAttributes.AbilityWeight(attribute) + Emphasis(attribute, position)).ToArray())
        .ToArray();

    private static readonly int[] WeightTotals = Weights.Select(weights => weights.Sum()).ToArray();

    public static int For(PlayerRating rating, Position position)
    {
        ArgumentNullException.ThrowIfNull(rating);
        var row = (int)position - (int)Position.PointGuard;
        var weights = Weights[row];
        var visible = rating.Visible;
        var total = 0;
        for (var index = 0; index < weights.Length; index++)
        {
            total += weights[index] * visible[index];
        }

        var weightTotal = WeightTotals[row];

        var penalty = rating.Positions.TryGetValue(position, out var familiarity) ? FamiliarityPenalty(familiarity) : 0;
        return Math.Max(PlayerRating.MinimumOverall, (total / weightTotal) - penalty);
    }

    /// <summary>Extra weight a position puts on an attribute, on top of its Current Ability weight.</summary>
    private static int Emphasis(PlayerAttribute attribute, Position position) => (position, attribute) switch
    {
        (Position.PointGuard, PlayerAttribute.BallHandling or PlayerAttribute.Passing or PlayerAttribute.Vision) => 4,
        (Position.PointGuard, PlayerAttribute.ShotCreation or PlayerAttribute.ThreePoint or PlayerAttribute.PerimeterDefence) => 2,
        (Position.ShootingGuard, PlayerAttribute.ThreePoint or PlayerAttribute.ShotCreation or PlayerAttribute.MidRange) => 3,
        (Position.ShootingGuard, PlayerAttribute.PerimeterDefence or PlayerAttribute.OffBallMovement) => 2,
        (Position.SmallForward, PlayerAttribute.PerimeterDefence or PlayerAttribute.Finishing or PlayerAttribute.ThreePoint) => 2,
        (Position.SmallForward, PlayerAttribute.HelpDefence or PlayerAttribute.Vertical) => 1,
        (Position.PowerForward, PlayerAttribute.DefensiveRebounding or PlayerAttribute.InteriorDefence or PlayerAttribute.Strength) => 3,
        (Position.PowerForward, PlayerAttribute.Finishing or PlayerAttribute.ScreenSetting) => 2,
        (Position.Center, PlayerAttribute.InteriorDefence or PlayerAttribute.Blocking or PlayerAttribute.DefensiveRebounding) => 4,
        (Position.Center, PlayerAttribute.OffensiveRebounding or PlayerAttribute.Strength or PlayerAttribute.Finishing or PlayerAttribute.ScreenSetting) => 2,
        _ => 0,
    };
}
