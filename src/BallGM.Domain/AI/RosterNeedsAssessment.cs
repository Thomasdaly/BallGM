using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.AI;

/// <summary>How badly a team needs help at one position. Ordered so a caller can compare severities directly.</summary>
public enum NeedSeverity
{
    None = 0,
    Depth = 1,
    Starter = 2,
}

/// <summary>One position's read: how severe the need is, and why.</summary>
public sealed record PositionalNeed(Position Position, NeedSeverity Severity, string RuleCode, string Explanation);

/// <summary>
/// A team's roster needs: one reading per position, plus whatever else does not belong to a single
/// position — a short roster, a payroll floor breach. <see cref="Notes"/> reuses <see cref="RuleFinding"/>,
/// the same "third list" shape <c>TradeAssessment</c>/<c>SigningAssessment</c> already carry.
/// </summary>
public sealed record RosterNeedsAssessment(
    TeamId TeamId,
    IReadOnlyList<PositionalNeed> PositionalNeeds,
    IReadOnlyList<RuleFinding> Notes)
{
    public PositionalNeed? NeedAt(Position position) =>
        PositionalNeeds.FirstOrDefault(need => need.Position == position);
}
