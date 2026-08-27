using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Signings;

/// <summary>
/// Everything the signing rules read: the league as it stands, the two parties, and the ruleset. The
/// counterpart to <c>TradeContext</c>, and deliberately the same shape — a context handed in whole,
/// so that assessing an offer never has to go and find anything, and so the thing being assessed is
/// visibly the league at one moment rather than whatever the caller had lying around.
/// </summary>
/// <param name="Contracts">
/// Every contract in the league, not just this team's. Whether the player is already under contract
/// somewhere is the first question a signing has to answer, and answering it from one team's
/// contracts would answer a different question.
/// </param>
public sealed record SigningContext(
    Season CurrentSeason,
    Team Team,
    Player Player,
    IReadOnlyCollection<Contract> Contracts,
    TransactionLedger Ledger,
    RosterSizeLimits RosterLimits,
    CapThresholds CapThresholds,
    NegotiationRules NegotiationRules)
{
    /// <summary>
    /// Whether this is the player's current team. It changes the term limit where a league lets an
    /// incumbent offer more years, and it is the hook every deferred retention route hangs off.
    /// </summary>
    public bool IsIncumbentTeam => Team.PlayerIds.Contains(Player.Id);

    /// <summary>The live contract the player is already on, if any.</summary>
    public Contract? ExistingContract =>
        Contracts.FirstOrDefault(contract => contract.PlayerId == Player.Id && !contract.IsTerminated);

    public bool IsFreeAgent => ExistingContract is null;
}
