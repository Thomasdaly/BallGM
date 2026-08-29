using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Signings;

/// <summary>
/// Judges an offer without changing anything. Safe to call on every keystroke, which is the whole
/// point: an offer screen is a machine for asking "what if" repeatedly, and a check that mutates is
/// a check nobody can run speculatively.
/// <para>
/// Same division of labour as the trade engine, deliberately. This type assembles the verdict and
/// the arithmetic behind it; <see cref="SigningExecutor"/> re-runs it and applies the result. An
/// assessment handed in from outside is never trusted at execution time — an offer agreed five
/// transactions ago is not an offer anybody agreed to now.
/// </para>
/// </summary>
public sealed class SigningValidator
{
    public const string TeamMismatchCode = "signing.offer_team_mismatch";
    public const string PlayerMismatchCode = "signing.offer_player_mismatch";
    public const string AlreadyUnderContractCode = "signing.player_under_contract";
    public const string WrongStartSeasonCode = "signing.offer_does_not_start_this_season";
    public const string RosterFullCode = "signing.roster_full";
    public const string NoRouteCode = "signing.no_route_permits";
    public const string AboveHardCapCode = "signing.payroll_would_exceed_hard_cap";
    public const string NoHardCapCode = "signing.no_hard_cap_configured";
    public const string BelowFloorAfterCode = "signing.still_below_payroll_floor";
    public const string PostseasonIneligibleCode = "signing.after_playoff_eligibility_cutoff";
    public const string NoEligibilityCutoffCode = "signing.no_playoff_eligibility_cutoff";
    public const string EligibilityUncheckableCode = "signing.playoff_eligibility_cutoff_not_checked";

    private readonly CapLedger _capLedger = new();

    public DomainOperationResult<SigningAssessment> Validate(Offer offer, SigningContext context)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(context);

        // Identity mismatches are caller bugs rather than rule outcomes: an offer built for another
        // team says nothing about this one, so there is no assessment to hand back.
        if (offer.TeamId != context.Team.Id)
        {
            return DomainOperationResult<SigningAssessment>.Failure(new DomainError(
                TeamMismatchCode,
                $"This offer was made by team '{offer.TeamId.Value}' but is being assessed against team '{context.Team.Id.Value}'."));
        }

        if (offer.PlayerId != context.Player.Id)
        {
            return DomainOperationResult<SigningAssessment>.Failure(new DomainError(
                PlayerMismatchCode,
                $"This offer is for player '{offer.PlayerId.Value}' but is being assessed against player '{context.Player.Id.Value}'."));
        }

        var thresholds = context.CapThresholds;
        var rules = context.NegotiationRules;
        var season = context.CurrentSeason;

        var violations = new List<RuleFinding>();
        var warnings = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        var beforeResult = EvaluateCapSheet(context, context.Team.RosterCount, additionalCharge: null);
        if (beforeResult.IsFailure)
        {
            return DomainOperationResult<SigningAssessment>.Failure(beforeResult.Errors.ToArray());
        }

        var before = beforeResult.Value;

        var firstSeasonCharge = CapCharge.ActiveContract(
            context.Team.Id,
            season,
            context.Player.Id,
            // A contract that does not exist yet still has to be charged somewhere, and the charge is
            // what every threshold comparison below is about. The identifier is a stand-in: the real
            // one is minted when the signing is executed, and nothing here persists.
            new Domain.Contracts.ContractId(SortableId.NewId()),
            offer.FirstSeasonCompensation);

        var afterResult = EvaluateCapSheet(context, context.Team.RosterCount + 1, firstSeasonCharge);
        if (afterResult.IsFailure)
        {
            return DomainOperationResult<SigningAssessment>.Failure(afterResult.Errors.ToArray());
        }

        var after = afterResult.Value;

        if (!context.IsFreeAgent)
        {
            violations.Add(new RuleFinding(
                AlreadyUnderContractCode,
                $"{context.Player.FullName} is already under contract and cannot be signed. A player under contract moves by trade, or after being released.",
                offer.TeamId));
        }

        if (offer.FirstSeason != season)
        {
            violations.Add(new RuleFinding(
                WrongStartSeasonCode,
                $"This offer starts in season {offer.FirstSeason.Year}, but the league is in season {season.Year}. A signing takes effect now or not at all.",
                offer.TeamId));
        }

        if (context.Team.RosterCount >= context.RosterLimits.MaximumPlayers)
        {
            violations.Add(new RuleFinding(
                RosterFullCode,
                $"{context.Team.Name} already carries {context.Team.RosterCount} players, which is this league's maximum. Someone has to leave before anyone else arrives.",
                offer.TeamId));
        }

        OfferLegality.Check(
            offer,
            rules,
            thresholds,
            context.Player.SeasonsOfService,
            context.IsIncumbentTeam,
            violations,
            notes);

        // The hard cap is not a route, it is a ceiling on the result: no route may leave a team above
        // it, and a league without one has no such ceiling at all.
        if (thresholds.HardCap is { } hardCap)
        {
            if (after.TotalPayroll > hardCap)
            {
                violations.Add(new RuleFinding(
                    AboveHardCapCode,
                    $"This signing would take the payroll to {after.TotalPayroll.SmallestUnits}, above the hard cap of {hardCap.SmallestUnits}. No signing route permits a payroll above the hard cap.",
                    offer.TeamId));
            }
        }
        else
        {
            notes.Add(new RuleFinding(
                NoHardCapCode,
                "This league configures no hard cap, so no payroll is out of reach on this signing.",
                offer.TeamId));
        }

        var allowanceUsed = SigningRouteTable.AllowanceUsed(context.Ledger, context.Team.Id, season);

        var routes = SigningRouteTable.Evaluate(
            offer,
            rules,
            thresholds,
            context.Player.SeasonsOfService,
            before.TotalPayroll,
            HoldReleasedBySigning(before, rules),
            allowanceUsed.Committed,
            allowanceUsed.Signings);

        if (!routes.Any(route => route.Permits))
        {
            // Names the routes that were open to this team and came up short, not their reasoning:
            // every route reports its own verdict and figure on the assessment, and repeating all of
            // it here produced a paragraph that said the same thing three times.
            var tried = routes.Where(route => route.Applicable).Select(route => Describe(route.Kind)).ToList();

            violations.Add(new RuleFinding(
                NoRouteCode,
                tried.Count == 0
                    ? "No signing route in this league permits this offer."
                    : $"No signing route permits this offer. {string.Join(", ", tried)} were available to this team and none of them covers it.",
                offer.TeamId));
        }

        // Reporting only, and deliberately a warning rather than a violation: a team below the
        // payroll floor is not barred from signing anyone, it is a team that still has spending to do.
        if (thresholds.PayrollFloor is { } floor && after.TotalPayroll < floor)
        {
            warnings.Add(new RuleFinding(
                BelowFloorAfterCode,
                $"Even after this signing the payroll is {after.TotalPayroll.SmallestUnits}, still {floor.SmallestUnits - after.TotalPayroll.SmallestUnits} below this league's payroll floor.",
                offer.TeamId));
        }

        CheckPlayoffEligibility(context, offer.TeamId, warnings, notes);

        var capRoomBefore = thresholds.SoftCap is { } softCap
            ? new Money(Math.Max(0, softCap.SmallestUnits - before.TotalPayroll.SmallestUnits))
            : null;

        return DomainOperationResult<SigningAssessment>.Success(new SigningAssessment(
            offer,
            violations,
            warnings,
            notes,
            routes,
            before.TotalPayroll,
            after.TotalPayroll,
            context.Team.RosterCount,
            context.Team.RosterCount + 1,
            capRoomBefore));
    }

    /// <summary>
    /// Applies this league's playoff eligibility cutoff to the day the signing is being made on.
    /// <para>
    /// A warning, not a violation. A league with a cutoff does not forbid signing anybody after it —
    /// it decides who may appear in the postseason, and a team signing cover for the last fortnight
    /// of a regular season is doing something legal and deliberate. What it must never be is silent:
    /// a GM who signs a player on the wrong side of the cutoff has bought someone who cannot play in
    /// the games the signing was probably for.
    /// </para>
    /// <para>
    /// The three ways the check cannot fire — no postseason, no stated cutoff, no season under way —
    /// are each reported as their own note rather than collapsed into one. A check that never ran is
    /// otherwise indistinguishable from a check that ran and approved, which is the contract every
    /// other assessment in this codebase keeps.
    /// </para>
    /// </summary>
    private static void CheckPlayoffEligibility(
        SigningContext context,
        TeamId teamId,
        List<RuleFinding> warnings,
        List<RuleFinding> notes)
    {
        if (context.PostseasonRules is not { } postseason || !postseason.IsConfigured)
        {
            notes.Add(new RuleFinding(
                NoEligibilityCutoffCode,
                "This league holds no postseason, so no signing date can make a player ineligible for one.",
                teamId));
            return;
        }

        if (!postseason.HasEligibilityCutoff)
        {
            notes.Add(new RuleFinding(
                NoEligibilityCutoffCode,
                "This league states no playoff eligibility cutoff, so a player signed on the last day of the regular season is as eligible as one signed on the first.",
                teamId));
            return;
        }

        if (context.SigningDay is not { } day)
        {
            notes.Add(new RuleFinding(
                EligibilityUncheckableCode,
                $"This league's playoff eligibility cutoff falls on day {postseason.PlayoffEligibilityCutoffDay}, but no season is under way in this session, so there is no day to measure this signing against.",
                teamId));
            return;
        }

        if (context.IsPostseasonEligible == false)
        {
            warnings.Add(new RuleFinding(
                PostseasonIneligibleCode,
                $"{context.Player.FullName} would be signed on {day}, after this league's playoff eligibility cutoff on day {postseason.PlayoffEligibilityCutoffDay}. The signing is permitted and the player may play the rest of the regular season, but not the postseason.",
                teamId));
        }
    }

    private static string Describe(SigningRouteKind kind) => kind switch
    {
        SigningRouteKind.UnrestrictedSigning => "Unrestricted signing",
        SigningRouteKind.CapRoom => "Cap room",
        SigningRouteKind.MinimumSalary => "Minimum salary",
        SigningRouteKind.StandardOverCapAllowance => "The standard over-cap allowance",
        _ => kind.ToString(),
    };

    /// <summary>
    /// What the team stops reserving for an empty roster spot because this signing fills one. Zero
    /// when the team is already at or above the roster minimum, because then it was holding nothing.
    /// </summary>
    private static Money HoldReleasedBySigning(TeamCapSheet before, NegotiationRules rules)
    {
        if (before.RosterHolds.SmallestUnits == 0)
        {
            return Money.Zero;
        }

        // A hold is always priced at the floor for no service at all, whoever ends up filling it.
        return rules.CompensationFloor.FloorFor(0) ?? Money.Zero;
    }

    /// <summary>
    /// The team's cap sheet with the roster count it would have, optionally including a charge that
    /// does not exist yet. Both figures a GM sees — before and after — come through the one ledger,
    /// so the screen and the rules cannot disagree about a payroll.
    /// </summary>
    private DomainOperationResult<TeamCapSheet> EvaluateCapSheet(
        SigningContext context,
        int rosterCount,
        CapCharge? additionalCharge)
    {
        var charges = CapChargeProjection
            .ForTeamSeason(context.Contracts, context.Team.Id, context.CurrentSeason)
            .ToList();

        if (additionalCharge is not null)
        {
            charges.Add(additionalCharge);
        }

        charges.AddRange(RosterSlotHoldProjection.ForTeamSeason(
            context.Team.Id,
            context.CurrentSeason,
            rosterCount,
            context.RosterLimits,
            context.NegotiationRules.CompensationFloor));

        return _capLedger.Evaluate(context.Team.Id, context.CurrentSeason, charges, context.CapThresholds);
    }
}
