using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// When a player's career ends. Below <see cref="MinimumVoluntaryAge"/> a player never retires; from
/// there to <see cref="MandatoryRetirementAge"/> — exclusive, since the mandatory age is itself forced
/// rather than drawn — <see cref="VoluntaryOddsByAge"/> gives that season's chance out of 10,000,
/// keyed by age; at or above <see cref="MandatoryRetirementAge"/> retirement is certain rather than
/// drawn, because a league that means to end every career by a stated age should not have that age
/// occasionally miss on an unlucky-for-the-league roll.
/// </summary>
public sealed record RetirementRules
{
    private const string InvalidAgeRangeCode = "ruleset.invalid_retirement_age_range";

    private RetirementRules(int minimumVoluntaryAge, int mandatoryRetirementAge, BandedScale voluntaryOddsByAge)
    {
        MinimumVoluntaryAge = minimumVoluntaryAge;
        MandatoryRetirementAge = mandatoryRetirementAge;
        VoluntaryOddsByAge = voluntaryOddsByAge;
    }

    /// <summary>A league that models no retirement at all: players never leave the game through age.</summary>
    public static RetirementRules None { get; } = new(0, 0, BandedScale.None);

    public bool IsConfigured => MinimumVoluntaryAge > 0;

    /// <summary>The first age a voluntary retirement may be drawn for.</summary>
    public int MinimumVoluntaryAge { get; }

    /// <summary>The age retirement stops being drawn and starts being certain. Zero means no such age is set.</summary>
    public int MandatoryRetirementAge { get; }

    /// <summary>Chance of voluntary retirement that season, out of 10,000, keyed by age.</summary>
    public BandedScale VoluntaryOddsByAge { get; }

    public bool HasMandatoryAge => MandatoryRetirementAge > 0;

    public static DomainOperationResult<RetirementRules> Create(
        int minimumVoluntaryAge,
        int mandatoryRetirementAge,
        BandedScale? voluntaryOddsByAge)
    {
        if (minimumVoluntaryAge <= 0 || (mandatoryRetirementAge != 0 && mandatoryRetirementAge < minimumVoluntaryAge))
        {
            return DomainOperationResult<RetirementRules>.Failure(new DomainError(
                InvalidAgeRangeCode,
                $"The minimum voluntary retirement age must be positive, and the mandatory age (0 for none) must not fall below it, but was {minimumVoluntaryAge}/{mandatoryRetirementAge}."));
        }

        return DomainOperationResult<RetirementRules>.Success(
            new RetirementRules(minimumVoluntaryAge, mandatoryRetirementAge, voluntaryOddsByAge ?? BandedScale.None));
    }
}
