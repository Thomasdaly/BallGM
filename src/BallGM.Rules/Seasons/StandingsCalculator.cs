using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>
/// Builds the table from the games that have been played, and orders it by the sequence the league
/// stated.
/// <para>
/// Every figure is counted from results rather than accumulated, so a table can never drift from
/// the games behind it, and re-asking the question after a game is replayed gives the answer that
/// game implies rather than the answer plus whatever was there before.
/// </para>
/// <para>
/// Unlike the free-agency preference comparison — which is deliberately <em>not</em> a sort
/// comparator, because a materiality band cannot be transitive — a tie-break sequence is a chain of
/// total preorders and composes into a total order. So this one really is a sort, and the terminal
/// key underneath it (team identifier, ordinal ascending) is what makes it total. That terminal key
/// is never allowed to decide anything silently: every tie it settles is reported.
/// </para>
/// </summary>
public sealed class StandingsCalculator
{
    private const string UnresolvedTieCode = "standings.tie_unresolved_by_ruleset";
    private const string NoTieBreaksCode = "standings.no_tie_break_sequence_configured";
    private const string TieBreakNeedsDivisionsCode = "standings.tie_break_needs_divisions";
    private const string TieBreakNeedsConferencesCode = "standings.tie_break_needs_conferences";

    public Standings Calculate(
        League league,
        IReadOnlyDictionary<TeamId, string> teamNames,
        IEnumerable<GameResult> results,
        StandingsRules standingsRules)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(teamNames);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(standingsRules);

        var counted = results
            .Where(result => result.Phase == SeasonPhase.RegularSeason)
            .ToArray();

        var alignment = league.Alignment;
        var notes = new List<RuleFinding>();

        var rows = league.TeamIds
            .OrderBy(teamId => teamId.Value, StringComparer.Ordinal)
            .Select(teamId => BuildRow(teamId, teamNames, alignment, counted))
            .ToList();

        ReportInapplicableTieBreaks(standingsRules, alignment, notes);

        var ordered = rows
            .OrderBy(row => row, new StandingsComparer(standingsRules, counted))
            .ToList();

        ReportUnresolvedTies(ordered, standingsRules, counted, notes);

        return new Standings(ordered, notes);
    }

    private static StandingsRow BuildRow(
        TeamId teamId,
        IReadOnlyDictionary<TeamId, string> teamNames,
        LeagueAlignment alignment,
        IReadOnlyList<GameResult> results)
    {
        var played = results.Where(result => result.HomeTeamId == teamId || result.AwayTeamId == teamId).ToArray();

        var overall = TeamRecord.None;
        var pointsFor = 0;
        var pointsAgainst = 0;
        var divisionRecord = TeamRecord.None;
        var conferenceRecord = TeamRecord.None;

        var conferenceName = alignment.ConferenceOf(teamId);
        var divisionName = alignment.DivisionOf(teamId);

        foreach (var result in played)
        {
            var won = result.WinnerId == teamId;
            overall = won ? overall.Won() : overall.Lost();
            pointsFor += result.PointsFor(teamId);
            pointsAgainst += result.PointsAgainst(teamId);

            var opponent = result.HomeTeamId == teamId ? result.AwayTeamId : result.HomeTeamId;

            if (divisionName is not null && alignment.AreInSameDivision(teamId, opponent))
            {
                divisionRecord = won ? divisionRecord.Won() : divisionRecord.Lost();
            }

            if (conferenceName is not null && alignment.AreInSameConference(teamId, opponent))
            {
                conferenceRecord = won ? conferenceRecord.Won() : conferenceRecord.Lost();
            }
        }

        return new StandingsRow(
            teamId,
            teamNames.GetValueOrDefault(teamId, teamId.Value),
            conferenceName,
            divisionName,
            overall,
            divisionName is null ? null : divisionRecord,
            conferenceName is null ? null : conferenceRecord,
            pointsFor,
            pointsAgainst);
    }

    /// <summary>
    /// A tie-break this league's shape cannot support is a note, not a silent skip. A league with no
    /// divisions that states a division-record tie-break has stated a rule that can never fire, and
    /// a GM reading the table deserves to be told that rather than left to wonder why it never did.
    /// </summary>
    private static void ReportInapplicableTieBreaks(
        StandingsRules standingsRules,
        LeagueAlignment alignment,
        List<RuleFinding> notes)
    {
        if (!standingsRules.HasTieBreaks)
        {
            notes.Add(new RuleFinding(
                NoTieBreaksCode,
                "This league states no standings tie-break sequence. Teams on the same record are ordered by team identifier so that the table has a fixed order, and every tie that falls to it is reported below."));
            return;
        }

        foreach (var step in standingsRules.TieBreaks.Steps)
        {
            switch (step)
            {
                case StandingsTieBreak.DivisionRecord when !alignment.HasDivisions:
                    notes.Add(new RuleFinding(
                        TieBreakNeedsDivisionsCode,
                        "This league's tie-break sequence includes division record, but the league has no divisions, so that step can never separate two teams."));
                    break;

                case StandingsTieBreak.ConferenceRecord when alignment.IsFlat:
                    notes.Add(new RuleFinding(
                        TieBreakNeedsConferencesCode,
                        "This league's tie-break sequence includes conference record, but the league has no conferences, so that step can never separate two teams."));
                    break;
            }
        }
    }

    private static void ReportUnresolvedTies(
        IReadOnlyList<StandingsRow> ordered,
        StandingsRules standingsRules,
        IReadOnlyList<GameResult> results,
        List<RuleFinding> notes)
    {
        var comparer = new StandingsComparer(standingsRules, results);

        for (var index = 1; index < ordered.Count; index++)
        {
            var above = ordered[index - 1];
            var below = ordered[index];

            if (!comparer.SeparatedByARule(above, below))
            {
                notes.Add(new RuleFinding(
                    UnresolvedTieCode,
                    $"{above.TeamName} is placed above {below.TeamName} on the same record ({above.Overall}), and no tie-break this league states separates them. The order between them is the terminal key — team identifier — not a ruling.",
                    above.TeamId));
            }
        }
    }
}
