using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Signings;

/// <summary>
/// Whether an offer is a contract this league permits anyone to write down, independent of whether
/// the team can afford it. Affordability is the route table's question; this one is about shape.
/// <para>
/// Every check here is skippable by configuration, and skipping is reported rather than silent: a
/// league that sets no term limit has not passed the term check, it has no term check, and a GM
/// reading "no violations" deserves to know which of those they are looking at.
/// </para>
/// </summary>
public static class OfferLegality
{
    public const string TermTooLongCode = "offer.term_exceeds_limit";
    public const string EscalationTooSteepCode = "offer.raise_exceeds_limit";
    public const string DeescalationTooSteepCode = "offer.cut_exceeds_limit";
    public const string AboveCeilingCode = "offer.above_compensation_ceiling";
    public const string BelowFloorCode = "offer.below_compensation_floor";

    public const string NoTermLimitCode = "offer.no_term_limit_configured";
    public const string NoEscalationLimitCode = "offer.no_escalation_limit_configured";
    public const string NoCeilingCode = "offer.no_compensation_ceiling_configured";
    public const string NoFloorCode = "offer.no_compensation_floor_configured";

    /// <summary>
    /// Checks the offer's shape, appending violations and notes. Takes the player's service figure
    /// rather than the player, because both tables key off service and nothing else here does.
    /// </summary>
    public static void Check(
        Offer offer,
        NegotiationRules rules,
        CapThresholds thresholds,
        int seasonsOfService,
        bool isIncumbentTeam,
        List<RuleFinding> violations,
        List<RuleFinding> notes)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(violations);
        ArgumentNullException.ThrowIfNull(notes);

        CheckTerm(offer, rules, isIncumbentTeam, violations, notes);
        CheckEscalation(offer, rules, violations, notes);
        CheckCeiling(offer, rules, thresholds, seasonsOfService, violations, notes);
        CheckFloor(offer, rules, seasonsOfService, violations, notes);
    }

    private static void CheckTerm(
        Offer offer,
        NegotiationRules rules,
        bool isIncumbentTeam,
        List<RuleFinding> violations,
        List<RuleFinding> notes)
    {
        var maximum = rules.MaximumSeasonsFor(isIncumbentTeam);
        if (maximum is null)
        {
            notes.Add(new RuleFinding(
                NoTermLimitCode,
                "This league does not limit contract length, so no term check applies.",
                offer.TeamId));
            return;
        }

        if (offer.SeasonCount > maximum)
        {
            var whose = isIncumbentTeam && rules.MaximumIncumbentContractSeasons is not null
                ? "an incumbent team re-signing its own player"
                : "a team signing a player from outside";

            violations.Add(new RuleFinding(
                TermTooLongCode,
                $"This offer covers {offer.SeasonCount} seasons. The most {whose} may offer in this league is {maximum}.",
                offer.TeamId));
        }
    }

    private static void CheckEscalation(
        Offer offer,
        NegotiationRules rules,
        List<RuleFinding> violations,
        List<RuleFinding> notes)
    {
        if (!rules.HasEscalationLimit)
        {
            notes.Add(new RuleFinding(
                NoEscalationLimitCode,
                "This league does not limit season-over-season changes in salary, so an offer may rise or fall by any amount.",
                offer.TeamId));
            return;
        }

        // Both limits are a share of the first season, not of the previous one. Measuring against the
        // previous season would let a contract compound its way to any figure a long enough term
        // allows, which is the arbitrage the rule exists to close.
        var basis = offer.FirstSeasonCompensation.SmallestUnits;

        for (var index = 1; index < offer.Terms.Count; index++)
        {
            var previous = offer.Terms[index - 1].Compensation.SmallestUnits;
            var current = offer.Terms[index].Compensation.SmallestUnits;
            var change = current - previous;
            var season = offer.Terms[index].Season.Year;

            if (change > 0 && rules.MaximumAnnualEscalationPercent is { } risePercent)
            {
                var allowed = basis * risePercent / 100;
                if (change > allowed)
                {
                    violations.Add(new RuleFinding(
                        EscalationTooSteepCode,
                        $"Season {season} rises by {change} over the season before it. This league allows a rise of at most {risePercent}% of the first season, which is {allowed}.",
                        offer.TeamId));
                }
            }
            else if (change < 0 && rules.MaximumAnnualDeescalationPercent is { } fallPercent)
            {
                var allowed = basis * fallPercent / 100;
                if (-change > allowed)
                {
                    violations.Add(new RuleFinding(
                        DeescalationTooSteepCode,
                        $"Season {season} falls by {-change} from the season before it. This league allows a fall of at most {fallPercent}% of the first season, which is {allowed}.",
                        offer.TeamId));
                }
            }
        }
    }

    private static void CheckCeiling(
        Offer offer,
        NegotiationRules rules,
        CapThresholds thresholds,
        int seasonsOfService,
        List<RuleFinding> violations,
        List<RuleFinding> notes)
    {
        var ceiling = rules.CompensationCeiling.CeilingFor(seasonsOfService, thresholds.SoftCap);
        if (ceiling is null)
        {
            notes.Add(new RuleFinding(
                NoCeilingCode,
                "This league sets no maximum salary, so there is no ceiling for this offer to breach.",
                offer.TeamId));
            return;
        }

        foreach (var term in offer.Terms.Where(term => term.Compensation > ceiling))
        {
            violations.Add(new RuleFinding(
                AboveCeilingCode,
                $"Season {term.Season.Year} pays {term.Compensation.SmallestUnits}. With {seasonsOfService} seasons of service this player may be paid at most {ceiling.SmallestUnits} in any season, which is {rules.CompensationCeiling.PercentFor(seasonsOfService)}% of the soft cap.",
                offer.TeamId));
        }
    }

    private static void CheckFloor(
        Offer offer,
        NegotiationRules rules,
        int seasonsOfService,
        List<RuleFinding> violations,
        List<RuleFinding> notes)
    {
        var floor = rules.CompensationFloor.FloorFor(seasonsOfService);
        if (floor is null)
        {
            notes.Add(new RuleFinding(
                NoFloorCode,
                "This league sets no minimum salary, so an offer may pay any amount.",
                offer.TeamId));
            return;
        }

        // The floor is read at the player's service today and applied to every season. Service
        // accrues, so a later season's true floor would be higher; charging that against an offer
        // signed now would refuse contracts on the strength of seasons nobody has played yet. The
        // season-by-season version arrives with the calendar.
        foreach (var term in offer.Terms.Where(term => term.Compensation < floor))
        {
            violations.Add(new RuleFinding(
                BelowFloorCode,
                $"Season {term.Season.Year} pays {term.Compensation.SmallestUnits}. With {seasonsOfService} seasons of service this player cannot be paid less than {floor.SmallestUnits} in any season.",
                offer.TeamId));
        }
    }
}
