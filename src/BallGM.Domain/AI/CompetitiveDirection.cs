using BallGM.Domain.Common;
using BallGM.Domain.Teams;

namespace BallGM.Domain.AI;

/// <summary>
/// Which of three competitive postures a front office reads its own team as being in. Named the way
/// <c>docs/product-scope.md</c> names it — "classify their competitive direction" — rather than after
/// any one franchise-mode's vocabulary for the same idea.
/// </summary>
public enum CompetitiveDirection
{
    Rebuilding = 1,
    Retooling = 2,
    Contending = 3,
}

/// <summary>
/// A team's classified direction, and the findings that produced it. Mirrors the shape every other
/// assessment in this codebase reports through — <see cref="RuleFinding"/> per factor, never a hidden
/// score — because a direction a GM cannot see the reasoning behind is a label, not an explainable
/// decision.
/// </summary>
public sealed record OrganisationalDirectionAssessment(
    TeamId TeamId,
    CompetitiveDirection Direction,
    IReadOnlyList<RuleFinding> Factors);
