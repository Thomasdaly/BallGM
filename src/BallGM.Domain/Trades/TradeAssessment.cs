using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Trades;

/// <summary>
/// Whether a league lets injured players be traded. Configuration rather than code, because leagues
/// differ and a data pack must be able to say so without a new build.
/// </summary>
public enum InjuredPlayerTradeEligibility
{
    /// <summary>Injured players move like anyone else.</summary>
    Allowed = 0,

    /// <summary>Injured players move, but the proposal says so out loud before anyone signs it.</summary>
    AllowedWithWarning = 1,

    /// <summary>An injured player cannot be traded at all.</summary>
    Blocked = 2,
}

/// <summary>
/// What the trade would do to one team. Present whether or not the trade is legal, because the
/// numbers are how a GM works out what to change — a rejection with no figures behind it cannot be
/// negotiated against.
/// </summary>
public sealed record TradeTeamOutcome(
    TeamId TeamId,
    Money IncomingSalary,
    Money OutgoingSalary,
    Money PayrollBefore,
    Money PayrollAfter,
    int RosterCountBefore,
    int RosterCountAfter,
    int PicksBefore,
    int PicksAfter,
    IReadOnlyList<ThresholdStanding> ThresholdsAfter)
{
    /// <summary>Payroll after minus payroll before: positive means the team took salary on.</summary>
    public long PayrollChangeSmallestUnits =>
        PayrollAfter.SignedDifferenceFrom(PayrollBefore);
}

/// <summary>
/// The verdict on a proposal, with the arithmetic that produced it. Assembling this never touches
/// league state — validation that mutates is validation nobody can run speculatively, and a trade
/// machine is nothing but speculative runs.
/// <para>
/// Three lists, not two. <paramref name="Notes"/> carries rules the league does not configure and
/// the validator therefore skipped: a check that silently passes because a value was absent is
/// indistinguishable from a check that ran and approved. Notes are not warnings — nothing is wrong
/// with a league that has no salary matching, and styling it as a caution would say otherwise.
/// </para>
/// </summary>
public sealed record TradeAssessment(
    TradeId TradeId,
    IReadOnlyList<RuleFinding> Violations,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    IReadOnlyList<TradeTeamOutcome> Outcomes)
{
    public bool IsLegal => Violations.Count == 0;
}
