using BallGM.Application.Cap;
using BallGM.Application.Leagues;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;

namespace BallGM.Infrastructure.Cap;

/// <summary>
/// Adapts the Rules cap ledger onto the Application port. Application does not reference Rules, so
/// this is where the loaded <see cref="LeagueConfiguration"/> is turned back into the
/// <see cref="CapThresholds"/> the rules layer works in — the same trust boundary
/// <c>LeagueRulesetSerializer</c> already occupies for the ruleset file.
/// </summary>
public sealed class RulesCapLedger : ICapLedger
{
    private readonly CapLedger _capLedger = new();

    public DomainOperationResult<TeamCapSheet> Evaluate(
        TeamId teamId,
        Season season,
        IReadOnlyCollection<CapCharge> charges,
        int filledRosterSpots,
        LeagueConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(charges);
        ArgumentNullException.ThrowIfNull(configuration);

        // The configuration came from a file a modder can edit, so an inconsistent set of
        // thresholds is untrusted input: it fails explainably here rather than throwing.
        var thresholdsResult = CapThresholds.Create(
            configuration.PayrollFloor,
            configuration.SoftCap,
            configuration.LuxuryTax,
            configuration.FirstApron,
            configuration.SecondApron,
            configuration.HardCap);

        if (thresholdsResult.IsFailure)
        {
            return DomainOperationResult<TeamCapSheet>.Failure(thresholdsResult.Errors.ToArray());
        }

        // Contract charges arrive from the caller; holds are projected here, on the rules side of the
        // port, because their size and count are ruleset content. The two lists are added together
        // into one collection rather than kept apart: a payroll is one sum, and a hold that has to be
        // added in a second place is a hold one caller eventually forgets.
        var holds = RosterSlotHoldProjection.ForTeamSeason(
            teamId,
            season,
            filledRosterSpots,
            configuration.RosterLimits,
            CompensationFloorScale.From(configuration.Negotiation.CompensationFloorScale));

        var allCharges = charges.Concat(holds).ToList();

        return _capLedger.Evaluate(teamId, season, allCharges, thresholdsResult.Value);
    }
}
