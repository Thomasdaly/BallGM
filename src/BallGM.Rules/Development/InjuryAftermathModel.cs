using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;

namespace BallGM.Rules.Development;

/// <summary>
/// What a long injury leaves behind (<c>docs/franchise-realism.md</c> → F9), applied at the season boundary
/// to a player who lost <see cref="MinimumDays"/> or more to one injury that season. A young player bounces
/// back — a little more injury-prone, nothing lost; a player in his prime loses a little of his
/// athleticism; a veteran loses a real step, for good: speed, first step, lift, lateral quickness and
/// stamina, more the longer he was out and the older he is. That is the career-defining injury — the
/// one that turns a star's contract into a salary dump, or a former All-Star into a contender's
/// last-chance gamble. Deterministic in its random source.
/// </summary>
public static class InjuryAftermathModel
{
    private const string AftermathCode = "development.injury_aftermath";

    /// <summary>The days out, from one injury, that leave a mark.</summary>
    public const int MinimumDays = 45;

    /// <summary>The days out at which the damage is at its fullest (a year-long recovery).</summary>
    public const int FullSeverityDays = 330;

    /// <summary>Through this age a player bounces back with nothing lost.</summary>
    public const int BounceBackAge = 25;

    /// <summary>From this age the damage is a veteran's: a real, lasting step lost.</summary>
    public const int VeteranAge = 30;

    /// <summary>The most a veteran's athletic attribute falls, at full severity, before age adds to it.</summary>
    public const int MaximumVeteranLoss = 9;

    /// <summary>What a prime-age player can lose, at full severity.</summary>
    public const int MaximumPrimeLoss = 3;

    /// <summary>The extra loss per year past <see cref="VeteranAge"/>, at full severity.</summary>
    public const int LossPerYearPastVeteran = 1;

    /// <summary>Injury proneness added by a long injury: young, prime, veteran.</summary>
    public const int YoungPronenessGain = 3;

    /// <inheritdoc cref="YoungPronenessGain"/>
    public const int PrimePronenessGain = 5;

    /// <inheritdoc cref="YoungPronenessGain"/>
    public const int VeteranPronenessGain = 8;

    private static readonly PlayerAttribute[] Athleticism =
    [
        PlayerAttribute.Speed,
        PlayerAttribute.FirstStep,
        PlayerAttribute.Vertical,
        PlayerAttribute.LateralQuickness,
        PlayerAttribute.Stamina,
    ];

    /// <summary>The sheet after a long injury of <paramref name="daysOut"/> days, and what happened in a sentence.</summary>
    public static (PlayerRating Rating, RuleFinding? Finding) Apply(PlayerRating rating, int age, int daysOut, string playerName, string injury, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(random);
        if (daysOut < MinimumDays)
        {
            return (rating, null);
        }

        // Severity in hundredths: from the shortest long injury (a little) to a year out (all of it).
        var severity = Math.Clamp(((daysOut - MinimumDays) * 100 / (FullSeverityDays - MinimumDays)) + 20, 20, 100);
        var maximumLoss = age switch
        {
            <= BounceBackAge => 0,
            < VeteranAge => MaximumPrimeLoss,
            _ => MaximumVeteranLoss + ((age - VeteranAge) * LossPerYearPastVeteran),
        };
        var pronenessGain = age <= BounceBackAge ? YoungPronenessGain : age < VeteranAge ? PrimePronenessGain : VeteranPronenessGain;

        var after = rating.With(HiddenAttribute.InjuryProneness, Math.Min(PlayerRating.MaximumOverall, rating.Hidden(HiddenAttribute.InjuryProneness) + pronenessGain));
        var lost = 0;
        foreach (var attribute in Athleticism)
        {
            // Each attribute drawn between half and all of the loss this severity allows.
            var cap = maximumLoss * severity / 100;
            var loss = cap == 0 ? 0 : random.NextInt32((cap + 1) / 2, cap + 1);
            after = after.With(attribute, Math.Max(PlayerRating.MinimumOverall, after[attribute] - loss));
            lost += loss;
        }

        // Past his peak there is nothing left to reach above where he now stands.
        if (age >= VeteranAge && after.PotentialAbility > after.Overall)
        {
            after = new PlayerRating(after.Visible, after.HiddenValues, after.Overall, after.Body, after.Positions, after.Traits, after.Tendencies);
        }

        var text = lost == 0
            ? $"{playerName} ({age}) is back from {injury} ({daysOut} days out) with nothing lost, if a little more injury-prone."
            : $"{playerName} ({age}) has lost a step after {injury} ({daysOut} days out): about {Math.Max(1, lost / Athleticism.Length)} points off his speed, first step, lift, lateral quickness and stamina for good, and more injury-prone.";
        return (after, new RuleFinding(AftermathCode, text));
    }
}
