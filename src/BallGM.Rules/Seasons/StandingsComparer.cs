using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>
/// Orders the table: overall record first, then the league's stated tie-breaks in the order it
/// stated them, then the terminal key.
/// <para>
/// The terminal key exists because a table has to be totally ordered — two rows in an
/// indeterminate order would render differently on different runs, which is the same class of bug
/// as an unstable read model. It is deliberately the least meaningful thing available, a team
/// identifier, so that nobody can mistake it for a rule; and <see cref="SeparatedByARule"/> is what
/// lets the caller report every place it was reached.
/// </para>
/// </summary>
internal sealed class StandingsComparer(
    StandingsRules standingsRules,
    IReadOnlyList<GameResult> results) : IComparer<StandingsRow>
{
    public int Compare(StandingsRow? left, StandingsRow? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var byRule = CompareByRules(left, right);

        return byRule != 0
            ? byRule
            : string.CompareOrdinal(left.TeamId.Value, right.TeamId.Value);
    }

    /// <summary>Whether any stated rule — the record itself included — puts one of these two above the other.</summary>
    public bool SeparatedByARule(StandingsRow left, StandingsRow right) => CompareByRules(left, right) != 0;

    private int CompareByRules(StandingsRow left, StandingsRow right)
    {
        // The record itself is not a tie-break; it is what a tie-break breaks a tie in. Better
        // record first, which is why the comparison is inverted: a higher record sorts earlier.
        var byRecord = right.Overall.CompareTo(left.Overall);
        if (byRecord != 0)
        {
            return byRecord;
        }

        foreach (var step in standingsRules.TieBreaks.Steps)
        {
            var byStep = Apply(step, left, right);
            if (byStep != 0)
            {
                return byStep;
            }
        }

        return 0;
    }

    private int Apply(StandingsTieBreak step, StandingsRow left, StandingsRow right) => step switch
    {
        StandingsTieBreak.HeadToHeadRecord => CompareHeadToHead(left, right),
        StandingsTieBreak.DivisionRecord => CompareOptionalRecord(left.DivisionRecord, right.DivisionRecord),
        StandingsTieBreak.ConferenceRecord => CompareOptionalRecord(left.ConferenceRecord, right.ConferenceRecord),
        StandingsTieBreak.PointDifferential => right.PointDifferential.CompareTo(left.PointDifferential),
        StandingsTieBreak.PointsScored => right.PointsFor.CompareTo(left.PointsFor),
        _ => 0,
    };

    /// <summary>
    /// The record between these two teams and nobody else. Teams that have not met each other yet
    /// are not separated by this step, which is different from being level on it — but both hand
    /// over to the next step, so the distinction changes nothing about the order.
    /// </summary>
    private int CompareHeadToHead(StandingsRow left, StandingsRow right)
    {
        var leftRecord = TeamRecord.None;
        var rightRecord = TeamRecord.None;

        foreach (var result in results.Where(result => Involves(result, left.TeamId, right.TeamId)))
        {
            if (result.WinnerId == left.TeamId)
            {
                leftRecord = leftRecord.Won();
                rightRecord = rightRecord.Lost();
            }
            else
            {
                rightRecord = rightRecord.Won();
                leftRecord = leftRecord.Lost();
            }
        }

        return rightRecord.CompareTo(leftRecord);
    }

    private static bool Involves(GameResult result, TeamId left, TeamId right) =>
        (result.HomeTeamId == left && result.AwayTeamId == right) ||
        (result.HomeTeamId == right && result.AwayTeamId == left);

    /// <summary>
    /// A record the league cannot produce — a division record in a league with no divisions —
    /// separates nobody. It is reported as a note by the caller rather than treated as 0-0, because
    /// "there is no such record" and "both teams are 0-0" are different facts.
    /// </summary>
    private static int CompareOptionalRecord(TeamRecord? left, TeamRecord? right) =>
        left is null || right is null ? 0 : right.Value.CompareTo(left.Value);
}
