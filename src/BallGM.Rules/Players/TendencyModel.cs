using BallGM.Domain.Players;

namespace BallGM.Rules.Players;

/// <summary>
/// The tendencies a player's game implies, when no one has stated them: a pure function of his sheet,
/// so a pack or save that leaves them out always reads the same way. Each starts at the league's middle
/// and moves with how far the skill behind it stands out from his own level — a shooter better at threes
/// than at everything else shoots threes; a big better inside posts up — and with his body. Style, not
/// stature: how good he is overall enters usage separately, so a star's touches are not counted twice.
/// </summary>
public static class TendencyModel
{
    public const int Minimum = 2;
    public const int Maximum = 98;

    public static int[] Derive(PlayerRating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        var level = rating.Overall;
        var size = rating.Body?.HeightShape ?? 0;
        int Above(PlayerAttribute attribute) => rating[attribute] - level;

        var tendencies = new int[PlayerAttributes.TendencyCount];
        void Set(PlayerTendency tendency, int tenths) =>
            tendencies[(int)tendency] = Math.Clamp(PlayerRating.NeutralTendency + (tenths / 10), Minimum, Maximum);

        Set(PlayerTendency.Touches, (Above(PlayerAttribute.ShotCreation) * 12) + (Above(PlayerAttribute.BallHandling) * 8));
        Set(PlayerTendency.Shot, (((Above(PlayerAttribute.Finishing) + Above(PlayerAttribute.MidRange) + Above(PlayerAttribute.ThreePoint) + Above(PlayerAttribute.ShotCreation)) / 4) * 15) - (Above(PlayerAttribute.Passing) * 8));
        Set(PlayerTendency.Drive, (Above(PlayerAttribute.FirstStep) * 12) + (Above(PlayerAttribute.Finishing) * 8) - (size * 2));
        Set(PlayerTendency.PostUp, (Above(PlayerAttribute.PostScoring) * 15) + (Above(PlayerAttribute.Strength) * 5) + (size * 3) - 150);
        Set(PlayerTendency.ShotClose, (Above(PlayerAttribute.Finishing) * 10) + (Above(PlayerAttribute.Dunking) * 5) + (size * 4));
        Set(PlayerTendency.ShotMid, Above(PlayerAttribute.MidRange) * 15);
        Set(PlayerTendency.ShotThree, (Above(PlayerAttribute.ThreePoint) * 15) - (size * 4));
        Set(PlayerTendency.PullUp, Above(PlayerAttribute.ShotCreation) * 15);
        Set(PlayerTendency.Pass, (Above(PlayerAttribute.Vision) * 10) + (Above(PlayerAttribute.Passing) * 10));
        Set(PlayerTendency.CrashGlass, (Above(PlayerAttribute.OffensiveRebounding) * 12) + (Above(PlayerAttribute.Hustle) * 5));
        Set(PlayerTendency.Gamble, (Above(PlayerAttribute.Stealing) * 12) - (Above(PlayerAttribute.DefensiveDiscipline) * 8));
        Set(PlayerTendency.BlockShot, Above(PlayerAttribute.Blocking) * 15);
        Set(PlayerTendency.Foul, (Above(PlayerAttribute.Aggression) * 8) - (Above(PlayerAttribute.DefensiveDiscipline) * 12));
        return tendencies;
    }
}
