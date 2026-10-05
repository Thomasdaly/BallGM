using System.Windows.Input;
using BallGM.Application.Leagues;
using BallGM.Application.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The calendar screen: where the season is, what happens if it advances, and what the table says.
/// <para>
/// It shows the day index beside the date on purpose. Every rule in this game is expressed in season
/// days — an offer expiry, the in-season signing window, a playoff eligibility cutoff — so a GM who
/// can only see a date cannot check any of them, and one who can only see an index has no idea what
/// time of year it is.
/// </para>
/// <para>
/// The notes panel is not decoration either. It carries the rules this league does not configure and
/// the standings ties its stated sequence did not resolve, which is the one place in the game where
/// a silently invented answer would look completely ordinary.
/// </para>
/// </summary>
public sealed class SeasonViewModel : ViewModelBase
{
    private string? _viewedTeamName;
    private static readonly int[] AdvanceChoices = [1, 7, 14, 30];

    /// <summary>How many days behind the current day the results strip looks for played games.</summary>
    private const int RecentResultsDaysShown = 7;

    private readonly LeagueSession _session;
    private readonly Action<LeagueOverview> _onLeagueChanged;

    private SeasonSummary? _season;
    private SeasonAdvanceSummary? _lastAdvance;
    private BoxScoreSummary? _boxScore;
    private FixtureRow? _selectedFixture;
    private int _advanceDays = 1;
    private string _message = string.Empty;
    private bool _hasError;

    public SeasonViewModel(LeagueSession session, Action<LeagueOverview> onLeagueChanged)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _onLeagueChanged = onLeagueChanged ?? throw new ArgumentNullException(nameof(onLeagueChanged));

        StartSeasonCommand = new RelayCommand(StartSeason);
        AdvanceCommand = new RelayCommand(Advance);
        AdvanceToEndCommand = new RelayCommand(AdvanceToEnd);

        Refresh();
    }

    public string Title => "Calendar";

    public ICommand StartSeasonCommand { get; }

    public ICommand AdvanceCommand { get; }

    public ICommand AdvanceToEndCommand { get; }

    public IReadOnlyList<int> AdvanceOptions => AdvanceChoices;

    public int AdvanceDays
    {
        get => _advanceDays;
        set
        {
            if (SetProperty(ref _advanceDays, value))
            {
                PreviewAdvance();
            }
        }
    }

    public bool HasSeason => _season is not null;

    public bool HasNoSeason => _season is null;

    public string Headline => _season is null
        ? "No season is under way. Start one to build this league's calendar and fixture list."
        : $"Season {_season.Calendar.SeasonYear} · day {_season.Calendar.CurrentDay} of {_season.Calendar.LengthInDays} · {_season.Calendar.CurrentDate}";

    public string PhaseLine => _season is null
        ? string.Empty
        : $"{DisplayText.Words(_season.Calendar.CurrentPhase)} · {_season.Calendar.PlayedGames} of {_season.Calendar.ScheduledGames} scheduled games played";

    public string Message => _message;

    public bool HasMessage => _message.Length > 0;

    public bool HasError => _hasError;

    /// <summary>What advancing by the selected number of days would do, before it is done.</summary>
    public string AdvancePreview
    {
        get
        {
            if (_season is null)
            {
                return string.Empty;
            }

            if (_lastAdvance is null)
            {
                return $"Advance {DisplayText.Count(_advanceDays, "day")}.";
            }

            var permitted = _lastAdvance.IsPermitted ? string.Empty : " — which this season will not permit";

            return $"Day {_lastAdvance.FromDay} ({_lastAdvance.FromDate}) to day {_lastAdvance.ToDay} ({_lastAdvance.ToDate}), " +
                   $"{DisplayText.Words(_lastAdvance.ToPhase).ToLowerInvariant()}, {DisplayText.Count(_lastAdvance.GamesInRange, "fixture")} in range{permitted}.";
        }
    }

    public IReadOnlyList<CalendarPhaseRow> Phases =>
        _season is null ? [] : _season.Calendar.Phases.Select(CalendarPhaseRow.From).ToList();

    public IReadOnlyList<StandingsRowDisplay> Standings =>
        _season is null ? [] : _season.Standings.Rows.Select(row => StandingsRowDisplay.From(row, ViewedTeamName)).ToList();

    /// <summary>The team the shell is viewing, picked out in the standings the way a GM looks for their own line first.</summary>
    public string? ViewedTeamName
    {
        get => _viewedTeamName;
        set
        {
            if (SetProperty(ref _viewedTeamName, value))
            {
                RaisePropertyChanged(nameof(Standings));
            }
        }
    }

    /// <summary>
    /// The tie-break sequence this league states, in words. A GM reading a table has to be able to
    /// see what would separate two teams on the same record — and, where the answer is "nothing",
    /// see that too.
    /// </summary>
    public string TieBreakLine
    {
        get
        {
            if (_season is null)
            {
                return string.Empty;
            }

            return _season.Standings.HasStatedTieBreaks
                ? "Ties broken by " + string.Join(", then ", _season.Standings.TieBreakSequence.Select(step => DisplayText.Words(step).ToLowerInvariant()))
                : "This league states no tie-break. Teams level on record are ordered by identifier, and every tie that decides is listed below.";
        }
    }

    public IReadOnlyList<FixtureRow> UpcomingFixtures =>
        _season is null
            ? []
            : _season.UpcomingDays.SelectMany(day => day.Fixtures).Select(FixtureRow.From).ToList();

    /// <summary>The last few days of played games, most recent first — where a box score is picked from.</summary>
    public IReadOnlyList<FixtureRow> RecentResults
    {
        get
        {
            if (_season is null)
            {
                return [];
            }

            var currentDay = _season.Calendar.CurrentDay;
            var fromDay = Math.Max(0, currentDay - RecentResultsDaysShown);
            var dayCount = currentDay - fromDay;

            if (dayCount <= 0)
            {
                return [];
            }

            var schedule = _session.Schedule(fromDay, dayCount);

            return schedule.IsFailure
                ? []
                : schedule.Value
                    .SelectMany(day => day.Fixtures)
                    .Where(fixture => fixture.Played)
                    .Reverse()
                    .Select(FixtureRow.From)
                    .ToList();
        }
    }

    /// <summary>The fixture a box score is being read for, if one is selected.</summary>
    public FixtureRow? SelectedFixture
    {
        get => _selectedFixture;
        set
        {
            if (SetProperty(ref _selectedFixture, value))
            {
                LoadBoxScore();
            }
        }
    }

    public bool HasSelectedFixture => _selectedFixture is not null;

    public string BoxScoreHeadline => _boxScore is null
        ? string.Empty
        : $"{_boxScore.AwayTeamName} {_boxScore.AwayPoints} at {_boxScore.HomeTeamName} {_boxScore.HomePoints} · day {_boxScore.Day} · {_boxScore.Date}";

    public bool HasBoxScoreLines => _boxScore is { HasBoxScore: true };

    /// <summary>Why there is nothing to show, when a fixture is selected but no lines came back.</summary>
    public string BoxScoreUnavailableMessage
    {
        get
        {
            if (_selectedFixture is null || HasBoxScoreLines)
            {
                return string.Empty;
            }

            if (!_selectedFixture.Played)
            {
                return "This game has not been played yet.";
            }

            return _boxScore is null
                ? "Could not load this game's box score."
                : "This result was recorded without player lines.";
        }
    }

    public string HomeTeamName => _boxScore?.HomeTeamName ?? string.Empty;

    public string AwayTeamName => _boxScore?.AwayTeamName ?? string.Empty;

    public IReadOnlyList<BoxScoreLine> HomeBoxScoreLines => _boxScore?.HomeLines ?? [];

    public IReadOnlyList<BoxScoreLine> AwayBoxScoreLines => _boxScore?.AwayLines ?? [];

    public IReadOnlyList<SeasonFindingRow> Notes
    {
        get
        {
            if (_season is null)
            {
                return [];
            }

            return _season.Notes
                .Concat(_season.Standings.Notes)
                .Concat(_lastAdvance?.Notes ?? [])
                .Select(SeasonFindingRow.From)
                .ToList();
        }
    }

    public IReadOnlyList<SeasonFindingRow> Warnings
    {
        get
        {
            if (_season is null)
            {
                return [];
            }

            return _season.Warnings
                .Concat(_lastAdvance?.Warnings ?? [])
                .Concat(_lastAdvance?.Violations ?? [])
                .Select(SeasonFindingRow.From)
                .ToList();
        }
    }

    public bool HasNotes => Notes.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    private void StartSeason()
    {
        var result = _session.StartSeason();

        if (result.IsFailure)
        {
            Report(result.Errors.Select(error => error.Message), isError: true);
            return;
        }

        _season = result.Value;
        _lastAdvance = null;
        _selectedFixture = null;
        _boxScore = null;
        Report(["Season started."], isError: false);
        RaiseAll();
        PreviewAdvance();
    }

    private void Advance() => AdvanceBy(_advanceDays);

    private void AdvanceToEnd()
    {
        if (_season is null)
        {
            return;
        }

        AdvanceBy(_season.Calendar.LengthInDays - _season.Calendar.CurrentDay);
    }

    private void AdvanceBy(int days)
    {
        var result = _session.AdvanceDays(days);

        if (result.IsFailure)
        {
            Report(result.Errors.Select(error => error.Message), isError: true);
            return;
        }

        _lastAdvance = result.Value;
        Refresh();

        Report(
            [$"Advanced to day {result.Value.ToDay} ({result.Value.ToDate}). {DisplayText.Count(result.Value.GamesPlayed, "game")} played."],
            isError: false);

        // The league itself has not changed — a day passing moves no money and no players — but the
        // overview is what every other screen is projected from, so it is refreshed rather than left
        // to go stale behind whichever screen the GM opens next.
        var overview = _session.Overview();
        if (overview.IsSuccess)
        {
            _onLeagueChanged(overview.Value);
        }
    }

    /// <summary>Re-asks what an advance would do. Changes nothing, so it is safe on every selection.</summary>
    private void PreviewAdvance()
    {
        if (_season is null)
        {
            return;
        }

        var assessment = _session.AssessAdvance(_advanceDays);

        if (assessment.IsSuccess)
        {
            _lastAdvance = assessment.Value;
        }

        RaisePropertyChanged(nameof(AdvancePreview));
        RaisePropertyChanged(nameof(Notes));
        RaisePropertyChanged(nameof(Warnings));
        RaisePropertyChanged(nameof(HasNotes));
        RaisePropertyChanged(nameof(HasWarnings));
    }

    private void LoadBoxScore()
    {
        _boxScore = _selectedFixture is { Played: true } fixture && _session.BoxScore(fixture.GameId) is { IsSuccess: true } result
            ? result.Value
            : null;

        RaisePropertyChanged(nameof(HasSelectedFixture));
        RaisePropertyChanged(nameof(BoxScoreHeadline));
        RaisePropertyChanged(nameof(HasBoxScoreLines));
        RaisePropertyChanged(nameof(BoxScoreUnavailableMessage));
        RaisePropertyChanged(nameof(HomeTeamName));
        RaisePropertyChanged(nameof(AwayTeamName));
        RaisePropertyChanged(nameof(HomeBoxScoreLines));
        RaisePropertyChanged(nameof(AwayBoxScoreLines));
    }

    private void Refresh()
    {
        var season = _session.Season();
        _season = season.IsSuccess ? season.Value : null;
        _selectedFixture = null;
        _boxScore = null;
        RaiseAll();
    }

    private void Report(IEnumerable<string> messages, bool isError)
    {
        _message = string.Join(" ", messages);
        _hasError = isError;
        RaisePropertyChanged(nameof(Message));
        RaisePropertyChanged(nameof(HasMessage));
        RaisePropertyChanged(nameof(HasError));
    }

    private void RaiseAll()
    {
        RaisePropertyChanged(nameof(HasSeason));
        RaisePropertyChanged(nameof(HasNoSeason));
        RaisePropertyChanged(nameof(Headline));
        RaisePropertyChanged(nameof(PhaseLine));
        RaisePropertyChanged(nameof(Phases));
        RaisePropertyChanged(nameof(Standings));
        RaisePropertyChanged(nameof(TieBreakLine));
        RaisePropertyChanged(nameof(UpcomingFixtures));
        RaisePropertyChanged(nameof(RecentResults));
        RaisePropertyChanged(nameof(HasSelectedFixture));
        RaisePropertyChanged(nameof(BoxScoreHeadline));
        RaisePropertyChanged(nameof(HasBoxScoreLines));
        RaisePropertyChanged(nameof(BoxScoreUnavailableMessage));
        RaisePropertyChanged(nameof(HomeTeamName));
        RaisePropertyChanged(nameof(AwayTeamName));
        RaisePropertyChanged(nameof(HomeBoxScoreLines));
        RaisePropertyChanged(nameof(AwayBoxScoreLines));
        RaisePropertyChanged(nameof(Notes));
        RaisePropertyChanged(nameof(Warnings));
        RaisePropertyChanged(nameof(HasNotes));
        RaisePropertyChanged(nameof(HasWarnings));
        RaisePropertyChanged(nameof(AdvancePreview));
    }
}
