using BallGM.Domain.Common;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;

namespace BallGM.Domain.AI;

/// <summary>
/// A trade a front office might pursue: a legal, one-for-one proposal between two teams, matched
/// because each side's incoming player addresses a stated positional need and the two players read
/// within this model's fairness tolerance of each other, plus the sentences that explain why.
/// <para>
/// Carries the full <see cref="TradeAssessment"/> alongside the rationale rather than just a legality
/// flag, because a GM reading a suggested target is owed the same payroll/roster arithmetic a human
/// proposal's assessment already shows — an AI-sourced trade is not a special, less explainable kind
/// of proposal.
/// </para>
/// </summary>
public sealed record TradeTargetCandidate(
    TradeProposal Proposal,
    TeamId ShoppingTeamId,
    TeamId CounterpartyTeamId,
    IReadOnlyList<RuleFinding> Rationale,
    TradeAssessment Assessment);
