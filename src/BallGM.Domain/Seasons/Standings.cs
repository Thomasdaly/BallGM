using BallGM.Domain.Common;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One team's line in the table. Every figure is counted from recorded results rather than kept as
/// a running total, so a table can never disagree with the games behind it.
/// <para>
/// <see cref="DivisionRecord"/> and <see cref="ConferenceRecord"/> are null in a league that has no
/// such grouping. Null rather than 0-0 for the same reason a missing soft cap is null rather than
/// zero: a tie-break keyed on division record has to be able to report "this league has no
/// divisions" as a note instead of silently ruling on an empty record.
/// </para>
/// </summary>
public sealed record StandingsRow(
    TeamId TeamId,
    string TeamName,
    string? ConferenceName,
    string? DivisionName,
    TeamRecord Overall,
    TeamRecord? DivisionRecord,
    TeamRecord? ConferenceRecord,
    int PointsFor,
    int PointsAgainst)
{
    public int PointDifferential => PointsFor - PointsAgainst;

    public int GamesPlayed => Overall.Games;
}

/// <summary>
/// A league's table, already in order.
/// <para>
/// <see cref="Notes"/> carries every tie the ruleset's stated sequence did not resolve, and every
/// tie-break the league configured that this league's shape cannot support — a division record in a
/// league with no divisions. Both are the "a rule the ruleset does not configure is reported, never
/// silently skipped" contract the trade and signing assessments already keep, applied to the one
/// place a silent answer is hardest to notice: a table that looks perfectly ordinary while being
/// ordered by something nobody asked for.
/// </para>
/// </summary>
public sealed record Standings(
    IReadOnlyList<StandingsRow> Rows,
    IReadOnlyList<RuleFinding> Notes)
{
    public static Standings Empty { get; } = new([], []);

    public StandingsRow? Row(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return Rows.FirstOrDefault(row => row.TeamId == teamId);
    }

    /// <summary>The rows of one conference, in the same order they hold league-wide.</summary>
    public IReadOnlyList<StandingsRow> InConference(string conferenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conferenceName);

        return Rows
            .Where(row => string.Equals(row.ConferenceName, conferenceName, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>Where a team sits in the table, counted from 1. Zero if it has no row.</summary>
    public int PositionOf(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].TeamId == teamId)
            {
                return index + 1;
            }
        }

        return 0;
    }
}
