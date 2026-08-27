using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Signings;

/// <summary>
/// How a team is allowed to pay for a signing. Every route answers the same question — may this team
/// commit this much to this player, and why — and a signing is legal if any of them says yes.
/// <para>
/// A table rather than a chain of conditionals, because the mechanism inventory dates six more
/// routes to later milestones and every one of them is a variation on <em>eligibility</em>. Adding
/// the incumbent retention allowance should add a row here, not a branch inside an existing row.
/// </para>
/// <para>
/// Routes are evaluated in a fixed order and the first that permits the signing is the one recorded
/// against it. The order preserves the scarce thing: the minimum-salary route consumes nothing at
/// all, cap room is simply payroll, and the standard allowance is the only genuinely finite pot, so
/// it is tried last. A team that could sign a minimum player either way should not find its
/// allowance quietly spent.
/// </para>
/// </summary>
public static class SigningRouteTable
{
    public const string PermittedUnrestrictedCode = "signing.permitted_unrestricted";
    public const string PermittedCapRoomCode = "signing.permitted_cap_room";
    public const string PermittedMinimumCode = "signing.permitted_minimum_salary";
    public const string PermittedAllowanceCode = "signing.permitted_standard_allowance";

    public const string InsufficientCapRoomCode = "signing.insufficient_cap_room";
    public const string AboveMinimumCode = "signing.above_minimum_salary";
    public const string InsufficientAllowanceCode = "signing.insufficient_allowance";
    public const string AllowanceWithdrawnCode = "signing.allowance_unavailable_above_threshold";
    public const string AllowanceSpentCode = "signing.allowance_already_committed";

    public const string NoSoftCapRouteCode = "signing.route_needs_soft_cap";
    public const string NoFloorRouteCode = "signing.route_needs_compensation_floor";
    public const string NoAllowanceRouteCode = "signing.route_needs_allowance";
    public const string CappedLeagueRouteCode = "signing.route_only_in_uncapped_league";

    /// <summary>
    /// Evaluates every route against one offer. Every route reports, including the ones that do not
    /// apply: a GM who is refused needs to see which doors were shut and which were never there.
    /// </summary>
    /// <param name="payrollBeforeSigning">
    /// The team's payroll as it stands, roster-slot holds included.
    /// </param>
    /// <param name="holdReleasedBySigning">
    /// What the team stops holding for an empty roster spot when this signing fills one. Cap room for
    /// a signing counts it back: the spot the new player occupies was already being reserved for
    /// somebody, and charging the team for both the hold and the player would find room that is not
    /// missing.
    /// </param>
    public static IReadOnlyList<SigningRouteEvaluation> Evaluate(
        Offer offer,
        NegotiationRules rules,
        CapThresholds thresholds,
        int seasonsOfService,
        Money payrollBeforeSigning,
        Money holdReleasedBySigning,
        Money allowanceAlreadyCommitted,
        int allowanceSigningsAlreadyMade)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(payrollBeforeSigning);
        ArgumentNullException.ThrowIfNull(holdReleasedBySigning);
        ArgumentNullException.ThrowIfNull(allowanceAlreadyCommitted);

        return
        [
            Unrestricted(offer, thresholds),
            MinimumSalary(offer, rules, seasonsOfService),
            CapRoom(offer, thresholds, payrollBeforeSigning, holdReleasedBySigning),
            StandardAllowance(offer, rules, thresholds, payrollBeforeSigning, allowanceAlreadyCommitted, allowanceSigningsAlreadyMade),
        ];
    }

    /// <summary>
    /// The degenerate route. A league with no soft cap has no line to sign under, over, or around, so
    /// there is nothing to check and no amount to refuse. Reported as a route rather than as the
    /// absence of one, because "you may pay him anything" is what a GM needs told.
    /// </summary>
    private static SigningRouteEvaluation Unrestricted(Offer offer, CapThresholds thresholds)
    {
        if (thresholds.SoftCap is not null)
        {
            return new SigningRouteEvaluation(
                SigningRouteKind.UnrestrictedSigning,
                Applicable: false,
                Permits: false,
                MaximumFirstSeasonCompensation: null,
                CappedLeagueRouteCode,
                "This league configures a soft cap, so signings are measured against it rather than unrestricted.");
        }

        return new SigningRouteEvaluation(
            SigningRouteKind.UnrestrictedSigning,
            Applicable: true,
            Permits: true,
            MaximumFirstSeasonCompensation: null,
            PermittedUnrestrictedCode,
            "This league configures no soft cap, so there is no limit on what a team may commit to a player. Roster space is the only constraint on this signing.");
    }

    private static SigningRouteEvaluation MinimumSalary(Offer offer, NegotiationRules rules, int seasonsOfService)
    {
        var floor = rules.CompensationFloor.FloorFor(seasonsOfService);
        if (floor is null)
        {
            return new SigningRouteEvaluation(
                SigningRouteKind.MinimumSalary,
                Applicable: false,
                Permits: false,
                MaximumFirstSeasonCompensation: null,
                NoFloorRouteCode,
                "This league configures no compensation floor, so there is no minimum-salary signing to make: the route exists to pay a player the least the rules allow, and the rules allow anything.");
        }

        var permits = offer.FirstSeasonCompensation <= floor;

        return new SigningRouteEvaluation(
            SigningRouteKind.MinimumSalary,
            Applicable: true,
            Permits: permits,
            floor,
            permits ? PermittedMinimumCode : AboveMinimumCode,
            permits
                ? $"A minimum-salary signing is available to every team regardless of payroll. With {seasonsOfService} seasons of service this player's minimum is {floor.SmallestUnits}."
                : $"This offer pays {offer.FirstSeasonCompensation.SmallestUnits} in its first season, above the {floor.SmallestUnits} minimum for a player with {seasonsOfService} seasons of service, so it is not a minimum-salary signing.");
    }

    private static SigningRouteEvaluation CapRoom(
        Offer offer,
        CapThresholds thresholds,
        Money payrollBeforeSigning,
        Money holdReleasedBySigning)
    {
        if (thresholds.SoftCap is not { } softCap)
        {
            return new SigningRouteEvaluation(
                SigningRouteKind.CapRoom,
                Applicable: false,
                Permits: false,
                MaximumFirstSeasonCompensation: null,
                NoSoftCapRouteCode,
                "This league configures no soft cap, so there is no room below it to sign into. Nothing is restricted here.");
        }

        var payrollForRoom = payrollBeforeSigning.SmallestUnits - holdReleasedBySigning.SmallestUnits;
        var room = new Money(Math.Max(0, softCap.SmallestUnits - payrollForRoom));
        var permits = offer.FirstSeasonCompensation <= room;

        return new SigningRouteEvaluation(
            SigningRouteKind.CapRoom,
            Applicable: true,
            Permits: permits,
            room,
            permits ? PermittedCapRoomCode : InsufficientCapRoomCode,
            permits
                ? $"The team has {room.SmallestUnits} of room below the soft cap, which covers this offer's first season of {offer.FirstSeasonCompensation.SmallestUnits}."
                : $"The team has {room.SmallestUnits} of room below the soft cap and this offer's first season is {offer.FirstSeasonCompensation.SmallestUnits}, which is {offer.FirstSeasonCompensation.SmallestUnits - room.SmallestUnits} more than the room covers.");
    }

    private static SigningRouteEvaluation StandardAllowance(
        Offer offer,
        NegotiationRules rules,
        CapThresholds thresholds,
        Money payrollBeforeSigning,
        Money allowanceAlreadyCommitted,
        int allowanceSigningsAlreadyMade)
    {
        if (rules.StandardOverCapAllowance is not { } allowance)
        {
            return new SigningRouteEvaluation(
                SigningRouteKind.StandardOverCapAllowance,
                Applicable: false,
                Permits: false,
                MaximumFirstSeasonCompensation: null,
                NoAllowanceRouteCode,
                "This league configures no standard over-cap allowance, so a team above the cap has no fixed sum to spend.");
        }

        // Withdrawn above a named line. Checked before the arithmetic because a team above the line
        // has no allowance at all, and reporting a remaining balance it cannot touch would be worse
        // than reporting none.
        if (rules.StandardOverCapAllowanceUnavailableAbove is { } limitKind)
        {
            var limit = thresholds.Configured.FirstOrDefault(entry => entry.Kind == limitKind).Amount;
            if (limit is not null && payrollBeforeSigning > limit)
            {
                return new SigningRouteEvaluation(
                    SigningRouteKind.StandardOverCapAllowance,
                    Applicable: true,
                    Permits: false,
                    Money.Zero,
                    AllowanceWithdrawnCode,
                    $"The team's payroll of {payrollBeforeSigning.SmallestUnits} is above the {Describe(limitKind)} of {limit.SmallestUnits}, and this league withdraws the standard allowance above that line. This team has nothing to offer but a pitch.");
            }
        }

        if (!rules.AllowanceMaySplitAcrossPlayers && allowanceSigningsAlreadyMade > 0)
        {
            return new SigningRouteEvaluation(
                SigningRouteKind.StandardOverCapAllowance,
                Applicable: true,
                Permits: false,
                Money.Zero,
                AllowanceSpentCode,
                $"This league's standard allowance may be used on one player per season, and the team has already used it on {allowanceSigningsAlreadyMade}.");
        }

        var remaining = new Money(Math.Max(0, allowance.SmallestUnits - allowanceAlreadyCommitted.SmallestUnits));
        var permits = offer.FirstSeasonCompensation <= remaining && remaining.SmallestUnits > 0;

        return new SigningRouteEvaluation(
            SigningRouteKind.StandardOverCapAllowance,
            Applicable: true,
            Permits: permits,
            remaining,
            permits ? PermittedAllowanceCode : InsufficientAllowanceCode,
            permits
                ? $"The team has {remaining.SmallestUnits} of its standard over-cap allowance left, which covers this offer's first season of {offer.FirstSeasonCompensation.SmallestUnits}."
                : $"The team has {remaining.SmallestUnits} of its standard over-cap allowance left, and this offer's first season is {offer.FirstSeasonCompensation.SmallestUnits}.");
    }

    /// <summary>
    /// How much of the standard allowance a team has already committed this season, read back from
    /// the ledger rather than kept as a running total. A stored balance is a second account of the
    /// same events, and a rolled-back signing would leave it wrong with nothing to notice.
    /// </summary>
    public static (Money Committed, int Signings) AllowanceUsed(
        TransactionLedger ledger,
        TeamId teamId,
        Season season)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);

        var entries = ledger.EntriesForTeam(teamId)
            .Where(entry => entry.SigningRoute == SigningRouteKind.StandardOverCapAllowance)
            .Where(entry => entry.Season == season)
            .ToList();

        return (Money.Sum(entries.Select(entry => entry.Amount ?? Money.Zero)), entries.Count);
    }

    private static string Describe(CapThresholdKind kind) => kind switch
    {
        CapThresholdKind.PayrollFloor => "payroll floor",
        CapThresholdKind.SoftCap => "soft cap",
        CapThresholdKind.LuxuryTax => "luxury tax line",
        CapThresholdKind.FirstApron => "first apron",
        CapThresholdKind.SecondApron => "second apron",
        CapThresholdKind.HardCap => "hard cap",
        _ => kind.ToString(),
    };
}
