using BallGM.Application.Negotiations;
using BallGM.Application.Trades;

namespace BallGM.Application.AI;

/// <summary>
/// One finding, flattened the way every other assessment in this codebase flattens
/// <c>RuleFinding</c> for the client — <see cref="TradeFindingLine"/>,
/// <see cref="SigningFindingLine"/>, <c>BallGM.Application.Seasons.SeasonFindingLine</c> — so the
/// client never needs a reference to <c>BallGM.Domain.Common.RuleFinding</c> or the <c>TeamId</c> it
/// may carry.
/// </summary>
public sealed record AIFindingLine(string RuleCode, string Explanation, string? TeamName);

/// <summary>One position's read from <c>RosterNeedsCalculator</c>.</summary>
public sealed record PositionalNeedLine(string Position, string Severity, string RuleCode, string Explanation);

/// <summary>A team's classified competitive direction, and the findings that produced it.</summary>
public sealed record OrganisationalDirectionLine(string Direction, IReadOnlyList<AIFindingLine> Factors);

/// <summary>A team's positional needs, plus whatever does not belong to a single position.</summary>
public sealed record RosterNeedsLine(
    IReadOnlyList<PositionalNeedLine> PositionalNeeds,
    IReadOnlyList<AIFindingLine> Notes);

/// <summary>
/// One trade this team's front office might pursue. <see cref="Assessment"/> is the same
/// <see cref="TradeAssessmentSummary"/> a human proposal's own screen shows — an AI-sourced suggestion
/// carries the identical payroll and roster arithmetic, not a second, thinner explanation.
/// </summary>
public sealed record TradeTargetLine(
    string CounterpartyTeamId,
    string CounterpartyTeamName,
    string IncomingPlayerId,
    string IncomingPlayerName,
    string OutgoingPlayerId,
    string OutgoingPlayerName,
    IReadOnlyList<AIFindingLine> Rationale,
    TradeAssessmentSummary Assessment);

/// <summary>
/// One free-agent offer this team's front office might make, at the player's own asking price.
/// <see cref="Assessment"/> is the same <see cref="SigningAssessmentSummary"/> a human offer's own
/// screen shows, for the same reason <see cref="TradeTargetLine.Assessment"/> is.
/// </summary>
public sealed record FreeAgentTargetLine(
    string PlayerId,
    string PlayerName,
    IReadOnlyList<AIFindingLine> Rationale,
    SigningAssessmentSummary Assessment);

/// <summary>
/// Which prospect this team's front office would take, read against a freshly generated preview
/// class — see <see cref="IFrontOfficeAdvisor.PreviewDraftRecommendation"/> for why this is a preview
/// and not a forecast of the actual future draft.
/// </summary>
public sealed record DraftRecommendationLine(
    string ProspectId,
    string ProspectName,
    string Position,
    IReadOnlyList<AIFindingLine> Rationale);

/// <summary>
/// Everything <see cref="IFrontOfficeAdvisor"/> reads for one team, flattened for the diagnostics
/// screen. Read-only: nothing here has executed, and nothing on this record can be submitted back —
/// see <see cref="IFrontOfficeAdvisor"/>'s own remarks for why this slice stops at showing the work.
/// </summary>
public sealed record FrontOfficeAdvisorySummary(
    string TeamId,
    string TeamName,
    OrganisationalDirectionLine Direction,
    RosterNeedsLine Needs,
    IReadOnlyList<TradeTargetLine> TradeTargets,
    IReadOnlyList<FreeAgentTargetLine> FreeAgentTargets,
    DraftRecommendationLine? DraftPreview,
    string? DraftPreviewNote);
