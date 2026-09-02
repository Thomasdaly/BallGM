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

    public static RetirementAssessment Assess(int age, RetirementRules rules, IRandomSource random)
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

        var odds = rules.VoluntaryOddsByAge.ValueFor(age) ?? 0;
        var roll = random.NextInt32(0, 10_000);
        var retires = roll < odds;

        return retires
            ? new RetirementAssessment(true, new RuleFinding(VoluntaryDrawnCode, $"At age {age}, this player drew retirement ({odds} in 10,000 odds this season)."))
            : new RetirementAssessment(false, new RuleFinding(ContinuesPlayingCode, $"At age {age}, this player was eligible to retire ({odds} in 10,000 odds this season) but continues playing."));
    }
}
