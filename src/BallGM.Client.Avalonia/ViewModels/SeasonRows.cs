using BallGM.Application.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One line of the standings table, already formatted.</summary>
public sealed record StandingsRowDisplay(
    int Position,
    string TeamName,
    string Group,
    string Record,
    string DivisionRecord,
    string ConferenceRecord,
    string PointDifferential,
    string PointsFor)
{
    public static StandingsRowDisplay From(StandingsLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // A league with no divisions has no division record — an em dash rather than "0-0", because
        // a team that has played no division games and a league that has no divisions are different
        // things and only one of them is the team's fault.
        var division = line.DivisionWins is { } divisionWins && line.DivisionLosses is { } divisionLosses
            ? $"{divisionWins}-{divisionLosses}"
            : "—";

        var conference = line.ConferenceWins is { } conferenceWins && line.ConferenceLosses is { } conferenceLosses
            ? $"{conferenceWins}-{conferenceLosses}"
            : "—";

        var group = string.Join(" · ", new[] { line.ConferenceName, line.DivisionName }.Where(name => !string.IsNullOrWhiteSpace(name)));

        return new StandingsRowDisplay(
            line.Position,
            line.TeamName,
            group.Length == 0 ? "—" : group,
            $"{line.Wins}-{line.Losses}",
            division,
            conference,
            line.PointDifferential > 0 ? $"+{line.PointDifferential}" : line.PointDifferential.ToString(System.Globalization.CultureInfo.InvariantCulture),
            line.PointsFor.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>One fixture on the schedule strip, with its score where it has been played.</summary>
public sealed record FixtureRow(string Day, string Matchup, string Score)
{
    public static FixtureRow From(FixtureLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new FixtureRow(
            $"Day {line.Day} · {line.Date}",
            $"{line.AwayTeamName} at {line.HomeTeamName}",
            line.Played ? $"{line.AwayPoints}–{line.HomePoints}" : "—");
    }
}

/// <summary>One phase of the calendar, marked if the league is currently in it.</summary>
public sealed record CalendarPhaseRow(string Phase, string Days, string Dates, bool IsCurrent)
{
    public static CalendarPhaseRow From(CalendarPhaseLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new CalendarPhaseRow(
            line.Phase,
            $"days {line.StartDay}–{line.EndDayExclusive - 1}",
            $"{line.StartDate} to {line.EndDate}",
            line.IsCurrent);
    }
}

/// <summary>One thing the season rules had to say, as the screen shows it.</summary>
public sealed record SeasonFindingRow(string Code, string Explanation, string Scope)
{
    public static SeasonFindingRow From(SeasonFindingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new SeasonFindingRow(line.RuleCode, line.Explanation, line.TeamName ?? "League");
    }
}
