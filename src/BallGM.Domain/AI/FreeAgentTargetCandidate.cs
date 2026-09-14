using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;

namespace BallGM.Domain.AI;

/// <summary>
/// A free-agent signing a front office might pursue: a legal offer for a player at a position the
/// team needs, priced at the player's own asking price, plus the sentences that explain why.
/// <para>
/// Carries the full <see cref="SigningAssessment"/> alongside the rationale rather than just a
/// legality flag, for the same reason <see cref="TradeTargetCandidate"/> carries a
/// <see cref="TradeAssessment"/> — a GM reading a suggested target is owed the same route,
/// payroll, and roster arithmetic a human offer's assessment already shows.
/// </para>
/// </summary>
public sealed record FreeAgentTargetCandidate(
    Offer Offer,
    TeamId ShoppingTeamId,
    IReadOnlyList<RuleFinding> Rationale,
    SigningAssessment Assessment);
