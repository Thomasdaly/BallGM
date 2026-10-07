using System.Globalization;
using BallGM.Application.Seasons;
using BallGM.Client.Avalonia.Converters;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The season from one team's chair: its games laid out on a ribbon and a month grid, its form, and
/// the run of fixtures ahead of it. Built from the schedule and the table the session already hands
/// out; nothing here is a rule, and nothing a rule reads comes from here.
/// <para>
/// Opponent strength is shown only once an opponent has played. Before that the table orders level
/// teams by identifier, so a position or a percentage taken from it would be presented as a reading
/// of the opponent when it is nothing of the kind.
/// </para>
/// </summary>
internal sealed class SeasonOutlook
{
    /// <summary>How many of the next games get a card of their own.</summary>
    public const int RoadAheadGames = 5;

    /// <summary>How far ahead the stretch summary looks, in the team's games.</summary>
    public const int StretchGames = 10;

    /// <summary>How many recent results the form strip shows.</summary>
    public const int FormGames = 5;

    /// <summary>Half the ribbon's height: the tallest a win or loss bar can be.</summary>
    public const double RibbonHalfHeight = 26;

    /// <summary>The margin at which a ribbon bar reaches full height; anything wider is a blowout either way.</summary>
    private const int RibbonFullMargin = 30;

    private const double RibbonMinimumBar = 4;

    private readonly SeasonCalendarSummary _calendar;
    private readonly DateOnly _start;
    private readonly IReadOnlyList<TeamFixture> _games;
    private readonly IReadOnlyDictionary<int, TeamFixture> _gamesByDay;
    private readonly IReadOnlyDictionary<string, StandingsLine> _standings;
    private readonly StandingsLine? _teamLine;

    private SeasonOutlook(
        SeasonCalendarSummary calendar,
        DateOnly start,
        string? teamId,
        IReadOnlyList<TeamFixture> games,
        IReadOnlyDictionary<string, StandingsLine> standings)
    {
        _calendar = calendar;
        _start = start;
        _games = games;
        _standings = standings;
        TeamId = teamId;

        var byDay = new Dictionary<int, TeamFixture>();
        foreach (var game in games)
        {
            byDay.TryAdd(game.Day, game);
        }

        _gamesByDay = byDay;
        _teamLine = teamId is not null && standings.TryGetValue(teamId, out var line) ? line : null;
    }

    public string? TeamId { get; }

    public bool HasTeam => TeamId is not null;

    public string TeamName => _teamLine?.TeamName ?? string.Empty;

    public DateOnly Today => DateOn(_calendar.CurrentDay);

    public DateOnly FirstMonth => FirstOfMonth(_start);

    public DateOnly LastMonth => FirstOfMonth(DateOn(Math.Max(0, _calendar.LengthInDays - 1)));

    /// <summary>The month the calendar opens on: today's, kept inside the season.</summary>
    public DateOnly CurrentMonth => Clamp(FirstOfMonth(Today));

    public static SeasonOutlook Build(SeasonSummary season, IReadOnlyList<ScheduleDayLine> schedule, string? teamId)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(schedule);

        var start = ParseDate(season.Calendar.SeasonStartDate);
        var standings = season.Standings.Rows
            .GroupBy(row => row.TeamId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var known = teamId is not null && standings.ContainsKey(teamId) ? teamId : null;
        var games = known is null
            ? []
            : schedule
                .SelectMany(day => day.Fixtures)
                .Select(fixture => TeamFixture.From(fixture, known))
                .OfType<TeamFixture>()
                .OrderBy(game => game.Day)
                .ThenBy(game => game.GameId, StringComparer.Ordinal)
                .ToList();

        return new SeasonOutlook(season.Calendar, start, known, games, standings);
    }

    public DateOnly Clamp(DateOnly month) =>
        month < FirstMonth ? FirstMonth : month > LastMonth ? LastMonth : FirstOfMonth(month);

    /// <summary>The team's line in the table, as "12-8".</summary>
    public string Record => _teamLine is null ? "—" : $"{_teamLine.Wins}-{_teamLine.Losses}";

    /// <summary>The current run of results, as "W3" or "L2"; a dash before the first game.</summary>
    public string Streak
    {
        get
        {
            var played = _games.Where(game => game.Played).Reverse().ToList();
            if (played.Count == 0)
            {
                return "—";
            }

            var won = played[0].Won;
            var length = played.TakeWhile(game => game.Won == won).Count();
            return string.Create(CultureInfo.InvariantCulture, $"{(won ? "W" : "L")}{length}");
        }
    }

    public int GamesRemaining => _games.Count(game => !game.Played);

    public IReadOnlyList<FormChip> Form() =>
        _games
            .Where(game => game.Played)
            .TakeLast(FormGames)
            .Select(game => new FormChip(game.Won ? "W" : "L", game.Won, $"{Describe(game)} · {DateLine(game.Day)}"))
            .ToList();

    public IReadOnlyList<RibbonPhase> RibbonPhases() =>
        _calendar.Phases
            .Where(phase => phase.EndDayExclusive > phase.StartDay)
            .Select((phase, index) => new RibbonPhase(
                DisplayText.Words(phase.Phase),
                $"days {phase.StartDay}–{phase.EndDayExclusive - 1}",
                $"{DisplayText.Words(phase.Phase)} · {phase.StartDate} to {phase.EndDate}",
                phase.EndDayExclusive - phase.StartDay,
                phase.IsCurrent,
                index % 2 == 1))
            .ToList();

    public IReadOnlyList<RibbonDay> RibbonDays()
    {
        var days = new List<RibbonDay>(_calendar.LengthInDays);

        for (var day = 0; day < _calendar.LengthInDays; day++)
        {
            var isToday = day == _calendar.CurrentDay && !_calendar.IsComplete;
            var tip = $"Day {day} · {DateLine(day)}";

            if (!_gamesByDay.TryGetValue(day, out var game))
            {
                days.Add(new RibbonDay(day, isToday ? tip + " · today" : tip, isToday, 0, 0, false));
                continue;
            }

            var height = game.Played ? BarHeight(Math.Abs(game.TeamPoints - game.OpponentPoints)) : 0;
            days.Add(new RibbonDay(
                day,
                $"{tip} · {Describe(game)}",
                isToday,
                game.Played && game.Won ? height : 0,
                game.Played && !game.Won ? height : 0,
                !game.Played));
        }

        return days;
    }

    /// <summary>The month grid, Monday first, padded to whole weeks.</summary>
    public IReadOnlyList<CalendarDayCell> Month(DateOnly month)
    {
        var first = FirstOfMonth(month);
        var leading = ((int)first.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var cellCount = (int)Math.Ceiling((leading + daysInMonth) / 7d) * 7;
        var phaseStarts = _calendar.Phases
            .Where(phase => phase.EndDayExclusive > phase.StartDay)
            .GroupBy(phase => phase.StartDay)
            .ToDictionary(group => group.Key, group => DisplayText.Words(group.First().Phase));

        var cells = new List<CalendarDayCell>(cellCount);

        for (var index = 0; index < cellCount; index++)
        {
            var offset = index - leading;
            if (offset < 0 || offset >= daysInMonth)
            {
                cells.Add(Blank);
                continue;
            }

            var date = first.AddDays(offset);
            var day = date.DayNumber - _start.DayNumber;
            var dayOfMonth = date.Day.ToString(CultureInfo.InvariantCulture);
            var inSeason = day >= 0 && day < _calendar.LengthInDays;
            var isToday = inSeason && day == _calendar.CurrentDay && !_calendar.IsComplete;
            var marker = inSeason && phaseStarts.TryGetValue(day, out var phase) ? phase : string.Empty;

            if (!inSeason || !_gamesByDay.TryGetValue(day, out var game))
            {
                var tip = inSeason ? $"Day {day} · {DateLine(day)} · no game" : "Outside this season";
                cells.Add(new CalendarDayCell(dayOfMonth, inSeason, isToday, false, false, false, false, false, false, string.Empty, string.Empty, string.Empty, marker, null, tip));
                continue;
            }

            var detail = game.Played
                ? $"{game.TeamPoints}–{game.OpponentPoints}"
                : OpponentRecord(game.OpponentId);

            cells.Add(new CalendarDayCell(
                dayOfMonth,
                true,
                isToday,
                true,
                game.Played,
                !game.Played,
                game.Played && game.Won,
                game.Played && !game.Won,
                game.IsHome,
                $"{(game.IsHome ? "vs" : "@")} {TeamInitialsConverter.Initials(game.OpponentName)}",
                game.Played ? (game.Won ? "W" : "L") : string.Empty,
                detail,
                marker,
                game.GameId,
                $"Day {day} · {DateLine(day)} · {Describe(game)}"));
        }

        return cells;
    }

    /// <summary>"8-4 · 3 to play" for the team's games in a month.</summary>
    public string MonthSummary(DateOnly month)
    {
        var first = FirstOfMonth(month);
        var inMonth = _games.Where(game => FirstOfMonth(DateOn(game.Day)) == first).ToList();
        if (inMonth.Count == 0)
        {
            return "No games this month";
        }

        var played = inMonth.Where(game => game.Played).ToList();
        var toPlay = inMonth.Count - played.Count;
        var parts = new List<string>();

        if (played.Count > 0)
        {
            parts.Add($"{played.Count(game => game.Won)}-{played.Count(game => !game.Won)}");
        }

        if (toPlay > 0)
        {
            parts.Add($"{toPlay} to play");
        }

        return string.Join(" · ", parts);
    }

    public IReadOnlyList<MatchupCard> RoadAhead()
    {
        var cards = new List<MatchupCard>();
        foreach (var (game, rest) in Upcoming().Take(RoadAheadGames))
        {
            var opponent = _standings.GetValueOrDefault(game.OpponentId);
            var hasForm = opponent is { GamesPlayed: > 0 };
            var opponentDetail = hasForm
                ? $"{Ordinal(opponent!.Position)} in the table · {PerGame(opponent.PointDifferential, opponent.GamesPlayed)} per game"
                : "No games played yet";

            cards.Add(new MatchupCard(
                game.GameId,
                When(game.Day),
                $"Day {game.Day} · {DateLine(game.Day)}",
                game.IsHome ? "vs" : "@",
                game.IsHome,
                game.OpponentName,
                OpponentRecord(game.OpponentId),
                opponentDetail,
                hasForm,
                hasForm ? 100d * opponent!.Wins / opponent.GamesPlayed : 0,
                rest switch
                {
                    null => "Season opener",
                    0 => "Back-to-back",
                    _ => $"{DisplayText.Count(rest.Value, "day")} rest",
                },
                rest == 0,
                Series(game)));
        }

        return cards;
    }

    /// <summary>"6 home · 4 away" over the next <see cref="StretchGames"/> games.</summary>
    public string StretchVenues
    {
        get
        {
            var stretch = Upcoming().Take(StretchGames).Select(item => item.Game).ToList();
            return stretch.Count == 0 ? "—" : $"{stretch.Count(game => game.IsHome)} home · {stretch.Count(game => !game.IsHome)} away";
        }
    }

    public int StretchBackToBacks => Upcoming().Take(StretchGames).Count(item => item.RestDays == 0);

    /// <summary>
    /// The combined winning percentage of the next <see cref="StretchGames"/> opponents, each counted
    /// once per meeting; a dash while none of them has played.
    /// </summary>
    public string StretchOpponents
    {
        get
        {
            var opponents = Upcoming()
                .Take(StretchGames)
                .Select(item => _standings.GetValueOrDefault(item.Game.OpponentId))
                .OfType<StandingsLine>()
                .ToList();
            var played = opponents.Sum(line => line.GamesPlayed);

            return played == 0 ? "—" : WinPercent(opponents.Sum(line => line.Wins), played);
        }
    }

    private static CalendarDayCell Blank { get; } =
        new(string.Empty, false, false, false, false, false, false, false, false, string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty);

    /// <summary>The unplayed games in order, each with the days of rest before it (null for the team's first game).</summary>
    private IEnumerable<(TeamFixture Game, int? RestDays)> Upcoming()
    {
        for (var index = 0; index < _games.Count; index++)
        {
            var game = _games[index];
            if (game.Played)
            {
                continue;
            }

            int? rest = index == 0 ? null : game.Day - _games[index - 1].Day - 1;
            yield return (game, rest);
        }
    }

    private string Series(TeamFixture upcoming)
    {
        var meetings = _games.Where(game => game.Played && game.OpponentId == upcoming.OpponentId).ToList();
        return meetings.Count == 0
            ? "First meeting"
            : $"Season series {meetings.Count(game => game.Won)}-{meetings.Count(game => !game.Won)}";
    }

    private string OpponentRecord(string opponentId) =>
        _standings.TryGetValue(opponentId, out var line) ? $"{line.Wins}-{line.Losses}" : "—";

    private string When(int day) => (day - _calendar.CurrentDay) switch
    {
        <= 0 => "Today",
        1 => "Tomorrow",
        var days => $"In {days} days",
    };

    private static string Describe(TeamFixture game) => game.Played
        ? $"{(game.Won ? "W" : "L")} {game.TeamPoints}–{game.OpponentPoints} {(game.IsHome ? "vs" : "@")} {game.OpponentName}"
        : $"{(game.IsHome ? "vs" : "@")} {game.OpponentName}";

    private static double BarHeight(int margin) =>
        RibbonMinimumBar + ((RibbonHalfHeight - RibbonMinimumBar) * Math.Min(margin, RibbonFullMargin) / RibbonFullMargin);

    private DateOnly DateOn(int day) => _start.AddDays(day);

    private string DateLine(int day) => DateOn(day).ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly ParseDate(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.MinValue;

    private static string PerGame(int total, int games) =>
        ((double)total / games).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    internal static string WinPercent(int wins, int games) =>
        ((double)wins / games).ToString(".000", CultureInfo.InvariantCulture);

    internal static string Ordinal(int position)
    {
        var suffix = (position % 100) is 11 or 12 or 13
            ? "th"
            : (position % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

        return string.Create(CultureInfo.InvariantCulture, $"{position}{suffix}");
    }

    /// <summary>One fixture seen from one side of it.</summary>
    private sealed record TeamFixture(
        string GameId,
        int Day,
        bool IsHome,
        string OpponentId,
        string OpponentName,
        bool Played,
        int TeamPoints,
        int OpponentPoints)
    {
        public bool Won => Played && TeamPoints > OpponentPoints;

        public static TeamFixture? From(FixtureLine fixture, string teamId)
        {
            var isHome = string.Equals(fixture.HomeTeamId, teamId, StringComparison.Ordinal);
            if (!isHome && !string.Equals(fixture.AwayTeamId, teamId, StringComparison.Ordinal))
            {
                return null;
            }

            var played = fixture.Played && fixture.HomePoints is not null && fixture.AwayPoints is not null;
            return new TeamFixture(
                fixture.GameId,
                fixture.Day,
                isHome,
                isHome ? fixture.AwayTeamId : fixture.HomeTeamId,
                isHome ? fixture.AwayTeamName : fixture.HomeTeamName,
                played,
                played ? (isHome ? fixture.HomePoints!.Value : fixture.AwayPoints!.Value) : 0,
                played ? (isHome ? fixture.AwayPoints!.Value : fixture.HomePoints!.Value) : 0);
        }
    }
}
