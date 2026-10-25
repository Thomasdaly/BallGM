using BallGM.Domain.Players;

namespace BallGM.Rules.AI;

/// <summary>
/// What a front office knows about who is hurt and who used to be a star when it makes a deal
/// (<c>docs/franchise-realism.md</c> → F9): the days each injured player is still out, the days left in
/// the regular season (a contender's gamble is a player back for the postseason), and the players whose
/// careers once had an All-Star season.
/// </summary>
public sealed record InjuryReport(
    IReadOnlyDictionary<PlayerId, int> DaysOut,
    int DaysLeftInRegularSeason,
    IReadOnlySet<PlayerId> FormerStars)
{
    public static InjuryReport None { get; } = new(new Dictionary<PlayerId, int>(), 0, new HashSet<PlayerId>());

    /// <summary>The days out from which an injury moves a price at all: a knock of a few days does not.</summary>
    public const int PricedInjuryDays = 20;

    /// <summary>Out long enough that a deal is priced around it.</summary>
    public bool IsPricedIn(PlayerId playerId) => DaysOutFor(playerId) >= PricedInjuryDays;

    /// <summary>The days out at which an injury is a long one: a deal is priced around it.</summary>
    public const int LongInjuryDays = 45;

    /// <summary>The age from which a long injury makes a big contract dead weight rather than a setback.</summary>
    public const int VeteranAge = 30;

    public int DaysOutFor(PlayerId playerId) => DaysOut.GetValueOrDefault(playerId);

    /// <summary>A veteran out long: what his contract is worth to its team is mostly what it costs.</summary>
    public bool IsCareerThreatened(Player player, DateOnly asOf) =>
        DaysOutFor(player.Id) >= LongInjuryDays && player.AgeOn(asOf) >= VeteranAge;

    /// <summary>Back before the season's end: a contender can gamble on him for the postseason.</summary>
    public bool ReturnsThisSeason(PlayerId playerId) => DaysOutFor(playerId) < DaysLeftInRegularSeason;
}
