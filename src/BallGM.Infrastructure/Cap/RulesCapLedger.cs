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
        LeagueConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // The configuration came from a file a modder can edit, so an inconsistent set of
        // thresholds is untrusted input: it fails explainably here rather than throwing.
        var thresholdsResult = CapThresholds.Create(
            configuration.SoftCap,
            configuration.LuxuryTax,
            configuration.FirstApron,
            configuration.SecondApron,
            configuration.HardCap);

        if (thresholdsResult.IsFailure)
        {
            return DomainOperationResult<TeamCapSheet>.Failure(thresholdsResult.Errors.ToArray());
        }

        return _capLedger.Evaluate(teamId, season, charges, thresholdsResult.Value);
    }
}
