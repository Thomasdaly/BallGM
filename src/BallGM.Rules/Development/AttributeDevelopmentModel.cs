using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Development;

/// <summary>
/// Ages a full scouting sheet by one season, attribute by attribute (attributes A9). Pure and total:
/// the same sheet, age, rules and random state always give the same new sheet.
/// <para>
/// <b>How much.</b> The league's <see cref="DevelopmentRules"/> state how many points of Current Ability
/// a typical player gains (before the peak) or loses (after it) at each age. A young player's gain is
/// moved by his drive — professionalism, work rate and ambition — and capped by his headroom to
/// Potential Ability — and scaled by it, so a low-ceiling prospect plateaus and a high-ceiling one climbs
/// fast enough to become a star by his mid-twenties; an old
/// player's loss is softened by the same drive. The rules' variance then makes two players the same
/// age diverge: some bloom late, some fall off a cliff.
/// </para>
/// <para>
/// <b>Where.</b> The change is spread across the sheet by age, the way a career really moves: before the
/// peak, skills and the mind grow and the body fills out; after it, the legs go first (speed, first
/// step, lift, lateral quickness, stamina) while shooting holds and the game's understanding still
/// improves. The spread is normalised so Current Ability moves by the amount decided above. Hidden
/// attributes stay put except injury proneness, which creeps up after thirty.
/// </para>
/// </summary>
public static class AttributeDevelopmentModel
{
    /// <summary>Per cent of a young player's growth a point of drive (above or below 50) adds or removes.</summary>
    public const int GrowthPercentPerDrivePoint = 2;

    /// <summary>Per cent of an old player's decline a point of drive above 50 removes (or below 50 adds).</summary>
    public const int DeclinePercentPerDrivePoint = 1;

    /// <summary>
    /// Points of headroom to Potential Ability at which a young player grows by the league's stated
    /// curve; more headroom grows him faster (a high-ceiling prospect becomes a star in a few seasons),
    /// less grows him slower, within <see cref="MinimumGrowthPercent"/>–<see cref="MaximumGrowthPercent"/>.
    /// </summary>
    public const int HeadroomForTypicalGrowth = 12;

    /// <inheritdoc cref="HeadroomForTypicalGrowth"/>
    public const int MinimumGrowthPercent = 25;

    /// <inheritdoc cref="HeadroomForTypicalGrowth"/>
    public const int MaximumGrowthPercent = 220;

    /// <summary>The age after which injury proneness rises a point a season.</summary>
    public const int ProneAfterAge = 30;

    public static PlayerRating Develop(PlayerRating rating, int age, DevelopmentRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        if (!rules.IsConfigured)
        {
            return rating;
        }

        var phase = age < rules.PeakAgeStart ? Phase.Growth : age > rules.PeakAgeEnd ? Phase.Decline : Phase.Peak;
        var drive = ((rating.Hidden(HiddenAttribute.Professionalism) + rating[PlayerAttribute.WorkRate] + rating.Hidden(HiddenAttribute.Ambition)) / 3)
            - PlayerRating.NeutralHidden;

        // The season's change in Current Ability, in tenths of a point.
        var tenths = phase switch
        {
            Phase.Growth => (int)(rules.GrowthCurve.ValueFor(age) ?? 0) * 10 * Math.Clamp(100 + (drive * GrowthPercentPerDrivePoint), 40, 160) / 100
                * Math.Clamp((rating.PotentialAbility - rating.Overall) * 100 / HeadroomForTypicalGrowth, MinimumGrowthPercent, MaximumGrowthPercent) / 100,
            Phase.Decline => -(int)(rules.DeclineCurve.ValueFor(age) ?? 0) * 10 * Math.Clamp(100 - (drive * DeclinePercentPerDrivePoint), 60, 140) / 100,
            _ => 0,
        };

        tenths += rules.VarianceRange > 0 ? random.NextInt32(-rules.VarianceRange * 10, (rules.VarianceRange * 10) + 1) : 0;

        // Growth is bounded by the ceiling: a player at his potential only moves by luck, and never far.
        var headroom = (rating.PotentialAbility - rating.Overall) * 10;
        if (tenths > 0)
        {
            tenths = Math.Min(tenths, Math.Max(0, headroom) + 10);
        }

        var weights = PlayerAttributes.All.Select(attribute => Spread(attribute, phase, tenths >= 0)).ToArray();
        var abilityPerUnit = PlayerAttributes.All.Sum(attribute => PlayerAttributes.AbilityWeight(attribute) * weights[(int)attribute]);
        var visible = rating.Visible.ToArray();
        if (abilityPerUnit > 0 && tenths != 0)
        {
            // Each attribute moves by the change times its weight, scaled so the weighted sum (Current
            // Ability) moves by the change; the fraction of a point is settled by a draw, so small
            // changes still land somewhere rather than rounding to nothing everywhere.
            foreach (var attribute in PlayerAttributes.All)
            {
                var index = (int)attribute;
                var hundredths = (long)tenths * weights[index] * 100 * 10 / abilityPerUnit;
                var whole = (int)(hundredths / 100);
                var remainder = (int)Math.Abs(hundredths % 100);
                if (random.NextInt32(0, 100) < remainder)
                {
                    whole += Math.Sign(hundredths);
                }

                visible[index] = Math.Clamp(visible[index] + whole, PlayerRating.MinimumOverall + 1, PlayerRating.MaximumOverall);
            }
        }

        var hidden = rating.HiddenValues.ToArray();
        if (age >= ProneAfterAge)
        {
            var prone = (int)HiddenAttribute.InjuryProneness;
            hidden[prone] = Math.Min(PlayerRating.MaximumOverall, hidden[prone] + 1);
        }

        var developed = new PlayerRating(visible, hidden, rating.PotentialAbility, rating.Body, rating.Positions, rating.Traits, rating.Tendencies);

        // Potential is a ceiling, never below where he stands; past the peak there is nothing left to
        // reach, so it follows him down.
        var potential = phase == Phase.Decline ? developed.Overall : Math.Max(rating.PotentialAbility, developed.Overall);
        return potential == developed.PotentialAbility
            ? developed
            : new PlayerRating(visible, hidden, potential, rating.Body, rating.Positions, rating.Traits, rating.Tendencies);
    }

    private enum Phase
    {
        Growth,
        Peak,
        Decline,
    }

    /// <summary>
    /// How much of a season's change an attribute takes, in tenths of an even share, by career phase
    /// and direction. A young player's legs and frame grow less than his skills and his reading of the
    /// game; an old player's legs go first and his jump shot last, and his understanding still grows.
    /// </summary>
    private static int Spread(PlayerAttribute attribute, Phase phase, bool gaining)
    {
        if (phase == Phase.Decline || !gaining)
        {
            return attribute switch
            {
                PlayerAttribute.Speed or PlayerAttribute.FirstStep or PlayerAttribute.Vertical or PlayerAttribute.LateralQuickness => 24,
                PlayerAttribute.Stamina or PlayerAttribute.NaturalFitness or PlayerAttribute.Dunking => 16,
                PlayerAttribute.Finishing or PlayerAttribute.PerimeterDefence or PlayerAttribute.Stealing or PlayerAttribute.Blocking => 13,
                PlayerAttribute.Strength => 4,
                PlayerAttribute.ThreePoint or PlayerAttribute.MidRange or PlayerAttribute.FreeThrow or PlayerAttribute.Passing => 3,
                PlayerAttribute.BasketballIq or PlayerAttribute.Anticipation or PlayerAttribute.Composure or PlayerAttribute.Leadership => phase == Phase.Decline ? -3 : 3,
                _ => 8,
            };
        }

        return attribute switch
        {
            PlayerAttribute.Speed or PlayerAttribute.FirstStep or PlayerAttribute.Vertical or PlayerAttribute.LateralQuickness => 5,
            PlayerAttribute.Strength or PlayerAttribute.Stamina => 12,
            PlayerAttribute.BasketballIq or PlayerAttribute.Anticipation or PlayerAttribute.Composure or PlayerAttribute.Concentration => 13,
            PlayerAttribute.ThreePoint or PlayerAttribute.MidRange or PlayerAttribute.FreeThrow or PlayerAttribute.ShotCreation => 12,
            _ => 10,
        };
    }
}
