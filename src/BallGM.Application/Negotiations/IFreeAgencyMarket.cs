using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;

namespace BallGM.Application.Negotiations;

/// <summary>
/// The port an Application use case reaches the free-agency market through — the same arrangement as
/// <see cref="ISigningEngine"/>, <see cref="Trades.ITradeEngine"/> and <see cref="Cap.ICapLedger"/>,
/// because Application still does not reference Rules.
/// <para>
/// Two operations, deliberately separate, exactly as the other engines have.
/// <see cref="Assess"/> never touches the league or the negotiation, so the free-agency board can
/// re-ask "who would win right now" whenever anything changes; <see cref="Resolve"/> re-checks
/// everything against the league as it stands and either signs somebody or leaves the negotiation
/// exactly as it was.
/// </para>
/// <para>
/// The negotiation is passed in rather than read out of the snapshot. An in-flight negotiation is
/// market state a session owns for as long as free agency is running, not league state every screen
/// projects — and keeping it out of <see cref="LeagueSnapshot"/> is what stops every read model in
/// the game from having an opinion about it.
/// </para>
/// </summary>
public interface IFreeAgencyMarket
{
    DomainOperationResult<MarketAssessment> Assess(
        Negotiation negotiation,
        LeagueSnapshot snapshot,
        SeasonDay day,
        IRandomSource random);

    DomainOperationResult<MarketExecution> Resolve(
        Negotiation negotiation,
        LeagueSnapshot snapshot,
        SeasonDay day,
        IRandomSource random);

    /// <summary>
    /// What this player is asking per season, or <c>null</c> in a league that configures no salary
    /// range for them to be placed inside. Behind the port for the same reason
    /// <see cref="ISigningEngine.LimitsFor"/> is: a screen that worked an asking price out for itself
    /// would be a screen holding an opinion the rules layer is supposed to own.
    /// </summary>
    Money? AskingPrice(LeagueSnapshot snapshot, PlayerId playerId);
}
