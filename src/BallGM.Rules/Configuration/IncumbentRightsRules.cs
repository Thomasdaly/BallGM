using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// What a team's own free agent is worth to it (<c>docs/franchise-realism.md</c> → F3): the generic form
/// of the real agreement's veteran re-signing exception and restricted free agency.
/// <list type="bullet">
/// <item><b>Incumbent rights.</b> A team that had a player for at least <see cref="MinimumSeasonsWithTeam"/>
/// seasons when his contract ran out may sign him again over the cap, up to the compensation ceiling —
/// which is what lets a good team keep its core instead of losing it to cap arithmetic.</item>
/// <item><b>Restricted free agency.</b> A player whose expiring contract started within his first
/// <see cref="RestrictedServiceSeasons"/> seasons of service (a rookie deal) is restricted: his team may
/// tender a qualifying offer of <see cref="QualifyingOfferPercent"/> per cent of his last salary, match
/// any offer he accepts from another team, and sign him to the qualifying offer if nobody bids.</item>
/// </list>
/// Absent, a league has neither: an expiring player is simply a free agent like any other.
/// </summary>
public sealed record IncumbentRightsRules
{
    private const string InvalidCode = "ruleset.invalid_incumbent_rights";

    private IncumbentRightsRules(int minimumSeasonsWithTeam, int restrictedServiceSeasons, int qualifyingOfferPercent)
    {
        MinimumSeasonsWithTeam = minimumSeasonsWithTeam;
        RestrictedServiceSeasons = restrictedServiceSeasons;
        QualifyingOfferPercent = qualifyingOfferPercent;
    }

    /// <summary>A league with no incumbent rights and no restricted free agency.</summary>
    public static IncumbentRightsRules None { get; } = new(0, 0, 0);

    public bool IsConfigured => MinimumSeasonsWithTeam > 0;

    /// <summary>Seasons with the team, counted back from the one just ended, that earn incumbent rights.</summary>
    public int MinimumSeasonsWithTeam { get; }

    /// <summary>A contract that started within this many seasons of service makes its player restricted (0 for none).</summary>
    public int RestrictedServiceSeasons { get; }

    /// <summary>The qualifying offer, as a percentage of the player's last salary.</summary>
    public int QualifyingOfferPercent { get; }

    public bool HasRestrictedFreeAgency => IsConfigured && RestrictedServiceSeasons > 0;

    public static DomainOperationResult<IncumbentRightsRules> Create(int minimumSeasonsWithTeam, int restrictedServiceSeasons, int qualifyingOfferPercent)
    {
        if (minimumSeasonsWithTeam is < 1 or > 10 || restrictedServiceSeasons is < 0 or > 10 || (restrictedServiceSeasons > 0 && qualifyingOfferPercent is < 100 or > 300))
        {
            return DomainOperationResult<IncumbentRightsRules>.Failure(new DomainError(InvalidCode,
                $"Incumbent rights after {minimumSeasonsWithTeam} seasons, restriction within {restrictedServiceSeasons} seasons of service and a {qualifyingOfferPercent}% qualifying offer is not a rule: 1 to 10 seasons, 0 to 10 for restriction, and a qualifying offer of 100% to 300% of the last salary."));
        }

        return DomainOperationResult<IncumbentRightsRules>.Success(new IncumbentRightsRules(minimumSeasonsWithTeam, restrictedServiceSeasons, qualifyingOfferPercent));
    }
}
