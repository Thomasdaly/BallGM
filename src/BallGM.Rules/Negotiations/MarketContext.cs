using BallGM.Domain.Cap;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Negotiations;

/// <summary>
/// Everything the free-agency market reads: the league as it stands, the player whose market it is,
/// the day it is, and the seeded source the model is allowed to draw from where it is genuinely
/// indifferent. The counterpart to <see cref="SigningContext"/> and deliberately the same shape,
/// except that a market has many teams rather than one — which is the entire difference between 6a
/// and 6b.
/// </summary>
/// <param name="Players">
/// Every player in the league, not just free agents. Team fit is a question about who is already on
/// a roster at this player's position, and it cannot be answered from the free agents alone.
/// </param>
/// <param name="Random">
/// Seeded, and consumed only where <see cref="PreferenceRanking"/> has declared two offers
/// inseparable. Every other part of a resolution is arithmetic on the league, so the same league on
/// the same day resolves the same way whether or not anything is ever drawn from this.
/// </param>
public sealed record MarketContext(
    Season CurrentSeason,
    SeasonDay Day,
    Player Player,
    IReadOnlyCollection<Team> Teams,
    IReadOnlyCollection<Player> Players,
    IReadOnlyCollection<Contract> Contracts,
    TransactionLedger Ledger,
    RosterSizeLimits RosterLimits,
    CapThresholds CapThresholds,
    NegotiationRules NegotiationRules,
    IRandomSource Random,
    PostseasonRules? PostseasonRules = null)
{
    public Team? TeamFor(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return Teams.FirstOrDefault(team => team.Id == teamId);
    }

    /// <summary>
    /// This market's view of one team, in the shape the signing rules already read. Built rather
    /// than stored so that every offer in a resolving market is judged by the same validator the
    /// offer screen used — a market that legalised its own signings would be a second rulebook.
    /// </summary>
    public SigningContext SigningContextFor(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        return new SigningContext(
            CurrentSeason,
            team,
            Player,
            Contracts,
            Ledger,
            RosterLimits,
            CapThresholds,
            NegotiationRules,
            PostseasonRules,
            Day);
    }

    /// <summary>The players a team currently rosters, resolved from identifiers.</summary>
    public IReadOnlyList<Player> RosterOf(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var rostered = team.PlayerIds.ToHashSet();
        return Players.Where(player => rostered.Contains(player.Id)).ToList();
    }
}
