using BallGM.Domain.Teams;

namespace BallGM.Domain.Common;

/// <summary>
/// One thing the rules have to say about a proposed transaction — blocking or not. The code is what
/// the UI keys behaviour off; the explanation is what the GM reads. Both, always: "illegal" on its
/// own is not a rules engine, it is a shrug.
/// <para>
/// Shared by the trade engine and the signing engine rather than duplicated per engine. Both need
/// the same three lists — what blocks this, what is worth saying about it, and what the league does
/// not configure so was never checked — and two identical types would be two places for the third
/// list to be forgotten.
/// </para>
/// </summary>
public sealed record RuleFinding(string RuleCode, string Explanation, TeamId? TeamId = null);
