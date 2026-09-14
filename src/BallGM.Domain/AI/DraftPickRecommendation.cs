using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Teams;

namespace BallGM.Domain.AI;

/// <summary>
/// Which prospect a front office would take with one selection, and why. Unlike
/// <see cref="TradeTargetCandidate"/> and <see cref="FreeAgentTargetCandidate"/>, there is no
/// legality dimension to re-validate against — a draft selection has no route table or hard cap to
/// clear — so this carries only the rationale, no assessment.
/// </summary>
public sealed record DraftPickRecommendation(
    TeamId TeamId,
    ProspectId ProspectId,
    IReadOnlyList<RuleFinding> Rationale);
