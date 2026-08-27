using BallGM.Application.Leagues;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Teams;

namespace BallGM.Application.Cap;

/// <summary>
/// The port an Application query reaches the cap rules through. The implementation is
/// <c>BallGM.Rules.Cap.CapLedger</c>, adapted in <c>BallGM.Infrastructure</c>, because Application
/// does not reference Rules — the same arrangement that already keeps the ruleset <em>file format</em>
/// out of Application via <see cref="ILeagueDataSource"/>.
/// <para>
/// Thresholds arrive per call, taken from the loaded <see cref="LeagueConfiguration"/>, so there is
/// one load path for the ruleset and no second copy of the configured amounts to drift.
/// </para>
/// </summary>
public interface ICapLedger
{
    /// <param name="filledRosterSpots">
    /// How many players the team already has. The port needs it because the roster-slot holds that
    /// keep a cap sheet honest are projected behind this boundary, from the league's roster minimum
    /// and its compensation floor — both ruleset content, so both on the rules side of the port. The
    /// alternative was for the caller to project holds and pass them in with the contract charges,
    /// which would put a rule in the Application layer that the rules layer is supposed to own.
    /// </param>
    DomainOperationResult<TeamCapSheet> Evaluate(
        TeamId teamId,
        Season season,
        IReadOnlyCollection<CapCharge> charges,
        int filledRosterSpots,
        LeagueConfiguration configuration);
}
