using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// What a screen needs to draw a team's badge: its pack logo if it has one, its stated colours if it
/// has them, and the crest initials either way. Presentation only.
/// </summary>
public sealed record TeamArt(string Initials, string? LogoPath, TeamColours? Colours)
{
    public bool HasLogo => !string.IsNullOrEmpty(LogoPath);

    public static TeamArt For(string teamName, string? logoPath = null, TeamColours? colours = null) =>
        new(Converters.TeamInitialsConverter.Initials(teamName), logoPath, colours);
}

/// <summary>
/// One day of the season ribbon. A played game for the viewed team is a bar above the centre line
/// (a win) or below it (a loss), its height scaled by the margin; a game still to play is a tick on
/// the line; a day with no game is empty.
/// </summary>
public sealed record RibbonDay(
    int Day,
    string Tip,
    bool IsToday,
    double WinHeight,
    double LossHeight,
    bool IsUpcomingGame);

/// <summary>One phase of the season under the ribbon, as wide as its share of the season's days.</summary>
public sealed record RibbonPhase(string Name, string Days, string Dates, int Span, bool IsCurrent, bool IsAlternate);

/// <summary>One square of the month grid, from the viewed team's point of view.</summary>
public sealed record CalendarDayCell(
    string DayOfMonth,
    bool IsInSeason,
    bool IsToday,
    bool HasGame,
    bool IsPlayed,
    bool IsUpcoming,
    bool IsWin,
    bool IsLoss,
    bool IsHome,
    string Matchup,
    TeamArt? Opponent,
    string Result,
    string Detail,
    string Marker,
    string? GameId,
    string Tip)
{
    /// <summary>The opponent's team identifier, for its logo through the asset resolver.</summary>
    public string? OpponentId { get; init; }

    public string? OpponentName { get; init; }

    /// <summary>The second game of a back-to-back: the team also played the day before.</summary>
    public bool IsBackToBack { get; init; }
}

/// <summary>One of the viewed team's next games, with what a GM would want to know before it.</summary>
public sealed record MatchupCard(
    string GameId,
    string When,
    string DayLine,
    string Venue,
    bool IsHome,
    string OpponentName,
    TeamArt Opponent,
    string OpponentRecord,
    string OpponentDetail,
    bool HasOpponentForm,
    double OpponentWinPercent,
    string RestLine,
    bool IsBackToBack,
    string SeriesLine)
{
    public string? OpponentId { get; init; }

    public Controls.StatusKind RestKind => IsBackToBack ? Controls.StatusKind.Warning : Controls.StatusKind.Neutral;

    public string Eyebrow => $"{When} · {Venue}".ToUpperInvariant();

    public string DetailLine => $"{OpponentDetail} · {SeriesLine}";
}

/// <summary>One result in the viewed team's recent form.</summary>
public sealed record FormChip(string Letter, bool IsWin, string Tip);
