using BallGM.Domain.Common;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Development;

/// <summary>The rules' verdict on whether one player retires this season, with the finding that explains it.</summary>
public sealed record RetirementAssessment(bool Retires, RuleFinding Finding);

/// <summary>
/// Decides whether a player retires this season. Every path — too young to consider, drawn, forced,
/// or never modelled at all — reports a <see cref="RuleFinding"/> rather than a bare bool, so a GM
/// reading a retirement can always be told why, the same explainability contract every other rules
/// verdict in this codebase keeps.
/// </summary>
public static class RetirementModel
{
    private const string NotConfiguredCode = "retirement.not_configured";
    private const string BelowMinimumAgeCode = "retirement.below_minimum_age";
    private const string MandatoryAgeCode = "retirement.mandatory_age";
    private const string VoluntaryDrawnCode = "retirement.voluntary_drawn";
    private const string ContinuesPlayingCode = "retirement.continues_playing";

    /// <summary>
    /// The age-only verdict, moved by what else a real decision weighs: a player still under contract
    /// for next season rarely walks away from the money (<see cref="UnderContractPercent"/> of the odds),
    /// a free agent nobody has signed is likelier to call it a career (<see cref="UnsignedPercent"/>),
    /// and the better he still is against the league (<paramref name="qualityPercentile"/>, 0–100), the
    /// longer he plays — a star at the top of the league keeps half the odds of a fringe player.
    /// </summary>
    public static RetirementAssessment Assess(int age, int qualityPercentile, bool underContract, RetirementRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        var context = (underContract ? UnderContractPercent : UnsignedPercent) * (150 - Math.Clamp(qualityPercentile, 0, 100)) / 100;
        return Assess(age, rules, random, context);
    }

    /// <summary>Per cent of the age odds a player under contract for next season keeps.</summary>
    public const int UnderContractPercent = 25;

    /// <summary>Per cent of the age odds an unsigned free agent carries.</summary>
    public const int UnsignedPercent = 200;

    public static RetirementAssessment Assess(int age, RetirementRules rules, IRandomSource random) => Assess(age, rules, random, 100);

    private const string LeftTheLeagueCode = "retirement.left_the_league";

    /// <summary>
    /// Whether a free agent leaves the league (<c>docs/franchise-realism.md</c> → §21): nobody signed him
    /// all season and he did not play a game (<paramref name="playedThisSeason"/> is false only for a player
    /// with neither a game nor a contract in the season), so he has gone overseas, to the minor leagues or home — the
    /// real league's free-agent pool does not keep every unsigned player forever. A player out injured is
    /// still waiting to show he is healthy, and stays. Only for a league that models retirement.
    /// </summary>
    public static RetirementAssessment LeavesTheLeague(bool playedThisSeason, bool injured, RetirementRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!rules.IsConfigured || playedThisSeason || injured)
        {
            return new RetirementAssessment(false, new RuleFinding(ContinuesPlayingCode, "He played this season, is injured, or this league does not model retirement."));
        }

        return new RetirementAssessment(true, new RuleFinding(LeftTheLeagueCode, "Unsigned all season without playing a game, he leaves the league."));
    }

    private static RetirementAssessment Assess(int age, RetirementRules rules, IRandomSource random, int oddsPercent)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        if (!rules.IsConfigured)
        {
            return new RetirementAssessment(
                false, new RuleFinding(NotConfiguredCode, "This league does not model retirement, so no player ever retires from age."));
        }

        if (rules.HasMandatoryAge && age >= rules.MandatoryRetirementAge)
        {
            return new RetirementAssessment(
                true, new RuleFinding(MandatoryAgeCode, $"At age {age}, this player has reached this league's mandatory retirement age of {rules.MandatoryRetirementAge} and retires."));
        }

        if (age < rules.MinimumVoluntaryAge)
        {
            return new RetirementAssessment(
                false, new RuleFinding(BelowMinimumAgeCode, $"At age {age}, this player is younger than this league's minimum voluntary retirement age of {rules.MinimumVoluntaryAge}."));
        }

        var odds = Math.Min(10_000, (rules.VoluntaryOddsByAge.ValueFor(age) ?? 0) * oddsPercent / 100);
        var roll = random.NextInt32(0, 10_000);
        var retires = roll < odds;

        return retires
            ? new RetirementAssessment(true, new RuleFinding(VoluntaryDrawnCode, $"At age {age}, this player drew retirement ({odds} in 10,000 odds this season)."))
            : new RetirementAssessment(false, new RuleFinding(ContinuesPlayingCode, $"At age {age}, this player was eligible to retire ({odds} in 10,000 odds this season) but continues playing."));
    }
}
