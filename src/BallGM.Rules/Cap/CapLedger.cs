using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Cap;

/// <summary>
/// Totals a team's cap charges for a season and reports where that total sits against every
/// configured threshold, as a rule code plus an explanation rather than a bare number — a GM who
/// cannot see <em>why</em> they are capped has not been given a cap sheet.
/// <para>
/// Deliberately narrow. This milestone models exactly two ways money lands on the books — a live
/// contract and guaranteed money owed to a released player — because those two are all the ledger
/// needs to be internally coherent. Explicitly deferred, and not partially implemented here:
/// signing exceptions of any kind, minimum-salary and rookie-scale rules, cap holds for unsigned
/// players, trade exceptions and salary-matching (Milestone 5), the tax bill owed above the tax
/// line, and apron-specific transaction restrictions (Milestone 5). This type reports where a team
/// stands; enforcing what each threshold forbids belongs to the trade and signing engines.
/// </para>
/// </summary>
public sealed class CapLedger
{
    private const string ChargeTeamMismatchCode = "cap_ledger.charge_team_mismatch";
    private const string ChargeSeasonMismatchCode = "cap_ledger.charge_season_mismatch";

    /// <summary>
    /// Builds one team's cap sheet. Charges belonging to another team or another season are a
    /// structured failure rather than a silently ignored row: a payroll that quietly drops a charge
    /// is worse than one that refuses to be computed.
    /// </summary>
    public DomainOperationResult<TeamCapSheet> Evaluate(
        TeamId teamId,
        Season season,
        IReadOnlyCollection<CapCharge> charges,
        CapThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(charges);
        ArgumentNullException.ThrowIfNull(thresholds);

        var errors = new List<DomainError>();

        foreach (var charge in charges)
        {
            if (charge.TeamId != teamId)
            {
                errors.Add(new DomainError(
                    ChargeTeamMismatchCode,
                    $"A cap charge belonging to team '{charge.TeamId.Value}' was supplied while building the cap sheet for team '{teamId.Value}'."));
            }

            if (charge.Season != season)
            {
                errors.Add(new DomainError(
                    ChargeSeasonMismatchCode,
                    $"A cap charge for season {charge.Season.Year} was supplied while building the season {season.Year} cap sheet for team '{teamId.Value}'."));
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<TeamCapSheet>.Failure(errors.ToArray());
        }

        var committed = Money.Sum(charges.Where(charge => !charge.IsDeadMoney).Select(charge => charge.Amount));
        var dead = Money.Sum(charges.Where(charge => charge.IsDeadMoney).Select(charge => charge.Amount));
        var total = committed + dead;

        var standings = new List<ThresholdStanding>
        {
            Compare(total, CapThresholdKind.SoftCap, thresholds.SoftCap),
            Compare(total, CapThresholdKind.LuxuryTax, thresholds.LuxuryTax),
            Compare(total, CapThresholdKind.FirstApron, thresholds.FirstApron),
            Compare(total, CapThresholdKind.SecondApron, thresholds.SecondApron),
            Compare(total, CapThresholdKind.HardCap, thresholds.HardCap),
        };

        return DomainOperationResult<TeamCapSheet>.Success(
            new TeamCapSheet(teamId, season, committed, dead, total, charges.ToList(), standings));
    }

    private static ThresholdStanding Compare(Money payroll, CapThresholdKind kind, Money threshold)
    {
        // Threshold minus payroll: positive is room left, negative is the amount over the line.
        var signedDistance = threshold.SignedDifferenceFrom(payroll);

        var position = signedDistance switch
        {
            > 0 => ThresholdPosition.Under,
            < 0 => ThresholdPosition.Over,
            _ => ThresholdPosition.At,
        };

        return new ThresholdStanding(
            kind,
            threshold,
            signedDistance,
            position,
            RuleCodeFor(kind, position),
            ExplanationFor(kind, position));
    }

    private static string RuleCodeFor(CapThresholdKind kind, ThresholdPosition position)
    {
        var thresholdCode = kind switch
        {
            CapThresholdKind.SoftCap => "soft_cap",
            CapThresholdKind.LuxuryTax => "luxury_tax",
            CapThresholdKind.FirstApron => "first_apron",
            CapThresholdKind.SecondApron => "second_apron",
            CapThresholdKind.HardCap => "hard_cap",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cap threshold."),
        };

        var positionCode = position switch
        {
            ThresholdPosition.Under => "under",
            ThresholdPosition.At => "at",
            ThresholdPosition.Over => "over",
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown threshold position."),
        };

        return $"cap.{positionCode}_{thresholdCode}";
    }

    /// <summary>
    /// The player-facing half of the result. Carries no amounts on purpose: the signed distance
    /// travels as an integer on <see cref="ThresholdStanding"/>, so the client formats money once,
    /// in one place, instead of the rules layer baking a currency format into a sentence.
    /// </summary>
    private static string ExplanationFor(CapThresholdKind kind, ThresholdPosition position) => (kind, position) switch
    {
        (CapThresholdKind.SoftCap, ThresholdPosition.Under) =>
            "Under the soft cap: the team can still sign players using the room below the line.",
        (CapThresholdKind.SoftCap, ThresholdPosition.At) =>
            "Exactly at the soft cap: the room below the line is gone, but no over-the-cap restriction applies yet.",
        (CapThresholdKind.SoftCap, ThresholdPosition.Over) =>
            "Over the soft cap: the team has no room below the line and can only add salary through a signing exception.",

        (CapThresholdKind.LuxuryTax, ThresholdPosition.Under) =>
            "Under the luxury tax line: no tax is owed for this season.",
        (CapThresholdKind.LuxuryTax, ThresholdPosition.At) =>
            "Exactly at the luxury tax line: no tax is owed, and any salary added creates a bill.",
        (CapThresholdKind.LuxuryTax, ThresholdPosition.Over) =>
            "Over the luxury tax line: the team owes tax on the amount above it. The size of that bill is not modelled yet.",

        (CapThresholdKind.FirstApron, ThresholdPosition.Under) =>
            "Under the first apron: no apron restriction applies.",
        (CapThresholdKind.FirstApron, ThresholdPosition.At) =>
            "Exactly at the first apron: one more unit of salary crosses into the restricted band.",
        (CapThresholdKind.FirstApron, ThresholdPosition.Over) =>
            "Over the first apron: the team is restricted in how it may take on salary. The restrictions themselves arrive with the trade engine.",

        (CapThresholdKind.SecondApron, ThresholdPosition.Under) =>
            "Under the second apron.",
        (CapThresholdKind.SecondApron, ThresholdPosition.At) =>
            "Exactly at the second apron: one more unit of salary crosses into the most restricted band.",
        (CapThresholdKind.SecondApron, ThresholdPosition.Over) =>
            "Over the second apron: the strictest transaction restrictions in the ruleset apply to this team.",

        (CapThresholdKind.HardCap, ThresholdPosition.Under) =>
            "Under the hard cap.",
        (CapThresholdKind.HardCap, ThresholdPosition.At) =>
            "Exactly at the hard cap: no further salary may be taken on at all.",
        (CapThresholdKind.HardCap, ThresholdPosition.Over) =>
            "Over the hard cap: this payroll is not legal under the configured ruleset and cannot be reached by any transaction.",

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cap threshold."),
    };
}
