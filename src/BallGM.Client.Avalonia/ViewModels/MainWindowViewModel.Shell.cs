using System.Globalization;
using System.Windows.Input;
using BallGM.Application.Seasons;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.Formatting;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The shell around the screens (spec 04 sections 1-4): the header band (user's team, record and
/// standing, the in-game date, Continue), the nav rail, back/forward history, global search, the
/// ticker, toasts, dialogs and the busy overlay. Presentation only: Continue advances time through the
/// calendar screen's own commands, search reads the loaded league, the ticker shows played results.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private bool _navigatingHistory;
    private bool _isSearchOpen;
    private string _searchText = string.Empty;
    private bool _isBusy;
    private string _busyStep = string.Empty;
    private DialogRequest? _dialog;
    private RelayCommand? _backCommand;
    private RelayCommand? _forwardCommand;

    /// <summary>Display labels for the nav rail, by section key.</summary>
    private static readonly IReadOnlyDictionary<string, string> NavLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Calendar"] = "Schedule",
        ["Trade"] = "Trades",
        ["Free agency board"] = "Negotiations",
        ["Cap sheet"] = "Finances",
        ["Pick board"] = "Draft picks",
    };

    private Settings.UiSettings _settings = new();

    public ToastService Toasts { get; } = new();

    /// <summary>The interface options in force; the window applies them, the shell reads a few.</summary>
    public Settings.UiSettings Settings
    {
        get => _settings;
        set
        {
            if (SetProperty(ref _settings, value ?? new Settings.UiSettings()))
            {
                RaisePropertyChanged(nameof(ShowTicker));
            }
        }
    }

    public bool ShowTicker => HasLeague && _settings.Ticker;

    public string LoadErrorText => string.Join(Environment.NewLine, LoadErrors);

    /// <summary>The nav rail: every section that exists, in rail order, with Ctrl+1..9 on the first nine.</summary>
    public IReadOnlyList<NavEntry> NavEntries => Sections
        .Select((section, index) => new NavEntry(
            section,
            NavLabels.GetValueOrDefault(section, section),
            Converters.SectionIconConverter.IconKey(section),
            0,
            index < 9 ? $"Ctrl+{index + 1}" : null))
        .ToList();

    public NavEntry? SelectedNavEntry
    {
        get => NavEntries.FirstOrDefault(entry => entry.Key == SelectedSection);
        set
        {
            if (value is not null)
            {
                SelectedSection = value.Key;
            }
        }
    }

    public ICommand BackCommand => _backCommand ??= new RelayCommand(GoBack);

    public ICommand ForwardCommand => _forwardCommand ??= new RelayCommand(GoForward);

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>The page title in the page header: the nav label of the section showing, or the page opened from it.</summary>
    public string PageTitle => CurrentScreen switch
    {
        PlayerProfileViewModel => "Player profile",
        SeasonViewModel { Sheet: { } sheet } => sheet.IsFinal ? "Box score" : "Pre-game",
        _ => SectionLabel,
    };

    private string SectionLabel => NavLabels.GetValueOrDefault(SelectedSection, SelectedSection);

    public IReadOnlyList<BreadcrumbItem> Breadcrumb => SelectedSection.Length == 0
        ? []
        : CurrentScreen is PlayerProfileViewModel profile
            ? [new BreadcrumbItem(UserTeamName ?? "League", NavigateCommand, Sections.FirstOrDefault()), new BreadcrumbItem(SectionLabel, BackCommand), new BreadcrumbItem(profile.FullName)]
            : CurrentScreen is SeasonViewModel { Sheet: { } sheet } season
                ? [new BreadcrumbItem(UserTeamName ?? "League", NavigateCommand, Sections.FirstOrDefault()), new BreadcrumbItem(SectionLabel, season.CloseGameCommand), new BreadcrumbItem($"{sheet.Away.Art.Initials} @ {sheet.Home.Art.Initials}")]
                : [new BreadcrumbItem(UserTeamName ?? "League", NavigateCommand, Sections.FirstOrDefault()), new BreadcrumbItem(SectionLabel)];

    /// <summary>Opens a section by key (rail, shortcut, breadcrumb, a link on another screen).</summary>
    public ICommand NavigateCommand => new ParameterCommand<string>(section => SelectedSection = section);

    public string? UserTeamName => _selectedTeam?.TeamName;

    public string? UserTeamId => _selectedTeam?.TeamId;

    /// <summary>The header's second line: record and conference standing, or the roster count before a season.</summary>
    public string HeaderRecordLine
    {
        get
        {
            if (_selectedTeam is null)
            {
                return string.Empty;
            }

            var standings = _session?.Standings();
            if (standings is not { IsSuccess: true } || standings.Value.Rows.Count == 0)
            {
                return Fmt.Count(_selectedTeam.RosterCount, "player").ToUpperInvariant() + " · " + Fmt.Money(_selectedTeam.CapSheet.TotalPayroll) + " PAYROLL";
            }

            var row = standings.Value.Rows.FirstOrDefault(line => line.TeamId == _selectedTeam.TeamId);
            if (row is null)
            {
                return string.Empty;
            }

            var group = standings.Value.Rows.Where(line => line.ConferenceName == row.ConferenceName).ToList();
            var rank = group.IndexOf(row) + 1;
            var conference = row.ConferenceName is { } name ? " " + ConferenceShort(name) : string.Empty;
            return $"{Fmt.Record(row.Wins, row.Losses)} · {Fmt.Ordinal(rank).ToUpperInvariant()}{conference}";
        }
    }

    /// <summary>The in-game date, or the season label before one starts.</summary>
    public string HeaderDate
    {
        get
        {
            var season = _session?.Season();
            if (season is not { IsSuccess: true })
            {
                return "PRESEASON";
            }

            return DateOnly.TryParse(season.Value.Calendar.CurrentDate, CultureInfo.InvariantCulture, out var date)
                ? Fmt.Date(date).ToUpperInvariant()
                : season.Value.Calendar.CurrentDate;
        }
    }

    public string HeaderPhase
    {
        get
        {
            var season = _session?.Season();
            return season is { IsSuccess: true } ? DisplayText.Words(season.Value.Calendar.CurrentPhase).ToUpperInvariant() : "SEASON NOT STARTED";
        }
    }

    public ContinueState ContinueState => _isBusy
        ? ContinueState.Simulating
        : NextUserGame() is { } next && next.Day == CurrentDay ? ContinueState.GameDay : ContinueState.Ready;

    public string ContinueLabel => ContinueState switch
    {
        ContinueState.Simulating => "SIMULATING",
        ContinueState.GameDay => "GO TO GAME",
        _ when Season?.HasNoSeason == true => "START SEASON",
        _ => "CONTINUE",
    };

    /// <summary>The next event: the user's next game, or the season's end.</summary>
    public string ContinueSubtitle
    {
        get
        {
            if (_isBusy)
            {
                return _busyStep;
            }

            if (Season?.HasNoSeason == true)
            {
                return "Build the calendar and fixture list";
            }

            if (NextUserGame() is not { } next)
            {
                return HeaderPhase.Length > 0 ? DisplayText.Words(HeaderPhase.ToLowerInvariant()) : string.Empty;
            }

            var home = next.HomeTeamId == UserTeamId;
            var opponent = home ? next.AwayTeamName : next.HomeTeamName;
            var when = next.Day == CurrentDay ? "today" : DateOnly.TryParse(next.Date, CultureInfo.InvariantCulture, out var date) ? Fmt.Date(date) : next.Date;
            return $"{(home ? "vs" : "@")} {Converters.TeamInitialsConverter.Initials(opponent)} · {when}";
        }
    }

    /// <summary>Continue: start the season, or advance one day (playing today's games) the way the calendar does.</summary>
    public ICommand ContinueCommand => new RelayCommand(() =>
    {
        if (Season is null || _isBusy)
        {
            return;
        }

        if (Season.HasNoSeason)
        {
            Season.StartSeasonCommand.Execute(null);
            RaiseShellChanged();
            return;
        }

        // Game day: go to the game first (its pre-game, where it is simulated); there already, play it.
        if (ContinueState == ContinueState.GameDay && NextUserGame() is { } today
            && !(CurrentScreen is SeasonViewModel { Sheet: { } open } && open.GameId == today.GameId))
        {
            SelectedSection = Season.Title;
            Season.OpenGame(today.GameId);
            RaisePropertyChanged(nameof(PageTitle));
            RaisePropertyChanged(nameof(Breadcrumb));
            return;
        }

        AdvanceBy(1);
    });

    /// <summary>The sim-to options the calendar already supports.</summary>
    public IReadOnlyList<ContinueOption> ContinueOptions => Season is null || Season.HasNoSeason
        ? []
        :
        [
            new("To the next game", new RelayCommand(() =>
            {
                if (NextUserGame() is { } next)
                {
                    AdvanceBy(Math.Max(1, next.Day - CurrentDay + 1));
                }
            })),
            new("One week", new RelayCommand(() => AdvanceBy(7))),
            new("Two weeks", new RelayCommand(() => AdvanceBy(14))),
            new("30 days", new RelayCommand(() => AdvanceBy(30))),
            new("To the end of the season", new RelayCommand(() =>
            {
                Season.AdvanceToEndCommand.Execute(null);
                RaiseShellChanged();
            })),
        ];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string BusyStep
    {
        get => _busyStep;
        private set => SetProperty(ref _busyStep, value);
    }

    public DialogRequest? Dialog
    {
        get => _dialog;
        set => SetProperty(ref _dialog, value);
    }

    /// <summary>Scores for the ticker: the latest results around the league.</summary>
    public IReadOnlyList<TickerItem> TickerItems
    {
        get
        {
            if (_session is null || Season?.HasNoSeason != false)
            {
                return [];
            }

            var from = Math.Max(0, CurrentDay - 3);
            var schedule = _session.Schedule(from, CurrentDay - from + 1);
            if (schedule is not { IsSuccess: true })
            {
                return [];
            }

            return schedule.Value
                .SelectMany(day => day.Fixtures)
                .OrderByDescending(fixture => fixture.Day)
                .Select(fixture => fixture.Played
                    ? new TickerItem($"{Abbr(fixture.AwayTeamName)} {fixture.AwayPoints} – {fixture.HomePoints} {Abbr(fixture.HomeTeamName)}", "FINAL")
                    : new TickerItem($"{Abbr(fixture.AwayTeamName)} @ {Abbr(fixture.HomeTeamName)}", fixture.Day == CurrentDay ? "TODAY" : null))
                .Take(24)
                .ToList();
        }
    }

    // Search (Ctrl+K): players and teams from the loaded league.
    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        set
        {
            if (SetProperty(ref _isSearchOpen, value) && !value)
            {
                SearchText = string.Empty;
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(SearchResults));
                RaisePropertyChanged(nameof(HasSearchResults));
                RaisePropertyChanged(nameof(SearchEmptyMessage));
            }
        }
    }

    public IReadOnlyList<SearchResult> SearchResults => SearchIndex.Find(_teams, _freeAgentNames, _searchText, OpenSearchResultCommand);

    public bool HasSearchResults => SearchResults.Count > 0;

    public string SearchEmptyMessage => _searchText.Trim().Length == 0
        ? "Type a player or team name."
        : $"No player or team matches \"{_searchText.Trim()}\".";

    public ICommand OpenSearchCommand => new RelayCommand(() => IsSearchOpen = true);

    public ICommand CloseSearchCommand => new RelayCommand(() => IsSearchOpen = false);

    public ICommand OpenSearchResultCommand => new ParameterCommand<SearchResult>(OpenSearchResult);

    private IReadOnlyList<(string Id, string Name)> _freeAgentNames = [];

    private int CurrentDay => _session?.Season() is { IsSuccess: true } season ? season.Value.Calendar.CurrentDay : 0;

    private static string ConferenceShort(string conference) =>
        conference.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Replace("ern", string.Empty, StringComparison.Ordinal).ToUpperInvariant() ?? string.Empty;

    private static string Abbr(string teamName) => Converters.TeamInitialsConverter.Initials(teamName);

    private FixtureLine? NextUserGame()
    {
        if (_session is null || UserTeamId is not { } team || Season?.HasNoSeason != false)
        {
            return null;
        }

        var schedule = _session.Schedule(CurrentDay, 40);
        return schedule is { IsSuccess: true }
            ? schedule.Value.SelectMany(day => day.Fixtures).FirstOrDefault(fixture => !fixture.Played && (fixture.HomeTeamId == team || fixture.AwayTeamId == team))
            : null;
    }

    private void AdvanceBy(int days)
    {
        if (Season is null)
        {
            return;
        }

        IsBusy = true;
        BusyStep = $"Advancing {Fmt.Count(days, "day")}";
        RaiseShellChanged();
        Season.AdvanceDays = days;
        Season.AdvanceCommand.Execute(null);
        IsBusy = false;
        RaiseShellChanged();
    }

    private void OpenSearchResult(SearchResult result)
    {
        IsSearchOpen = false;
        if (result.Kind == EntityKind.Team)
        {
            SelectedTeam = Teams.FirstOrDefault(team => team.TeamId == result.Id) ?? SelectedTeam;
            SelectedSection = Sections.FirstOrDefault() ?? SelectedSection;
            return;
        }

        OpenPlayer(result.Id);
    }

    /// <summary>Opens any player's profile as its own page; Back returns to where it was opened from.</summary>
    public void OpenPlayer(string playerId)
    {
        if (_session is null)
        {
            return;
        }

        var profile = _session.PlayerProfile(playerId);
        if (profile.IsFailure)
        {
            Toasts.Show(new ToastMessage(ToastKind.Error, "Profile unavailable", string.Join(" ", profile.Errors.Select(error => error.Message))));
            return;
        }

        PushHistory();
        var team = Teams.FirstOrDefault(candidate => candidate.Roster.Any(spot => spot.PlayerId == playerId));
        _profileScreen = new PlayerProfileViewModel(profile.Value, GoBack)
        {
            TeamId = team?.TeamId,
            TeamColours = team?.Colours,
            IsOwnPlayer = team is not null && team.TeamId == SelectedTeam?.TeamId,
            ExtendCommand = new RelayCommand(() => SelectedSection = "Contracts"),
            TradeCommand = new RelayCommand(() => SelectedSection = "Trade"),
        };
        CurrentScreen = _profileScreen;
        RaisePropertyChanged(nameof(PageTitle));
        RaisePropertyChanged(nameof(Breadcrumb));
    }

    private PlayerProfileViewModel? _profileScreen;

    private void PushHistory()
    {
        if (_navigatingHistory || SelectedSection.Length == 0)
        {
            return;
        }

        if (_back.Count == 0 || _back[^1] != SelectedSection)
        {
            _back.Add(SelectedSection);
        }

        _forward.Clear();
        RaiseHistoryChanged();
    }

    private void GoBack()
    {
        if (_profileScreen is not null && ReferenceEquals(CurrentScreen, _profileScreen))
        {
            _profileScreen = null;
            var section = SelectedSection;
            _selectedSection = string.Empty;
            _navigatingHistory = true;
            SelectedSection = section;
            _navigatingHistory = false;
            if (_back.Count > 0 && _back[^1] == section)
            {
                _back.RemoveAt(_back.Count - 1);
            }

            RaiseHistoryChanged();
            return;
        }

        if (_back.Count == 0)
        {
            return;
        }

        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(SelectedSection);
        _navigatingHistory = true;
        SelectedSection = target;
        _navigatingHistory = false;
        RaiseHistoryChanged();
    }

    private void GoForward()
    {
        if (_forward.Count == 0)
        {
            return;
        }

        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(SelectedSection);
        _navigatingHistory = true;
        SelectedSection = target;
        _navigatingHistory = false;
        RaiseHistoryChanged();
    }

    private void RaiseHistoryChanged()
    {
        RaisePropertyChanged(nameof(CanGoBack));
        RaisePropertyChanged(nameof(CanGoForward));
    }

    /// <summary>Everything in the header and ticker that a day passing can change.</summary>
    internal void RaiseShellChanged()
    {
        foreach (var name in new[]
        {
            nameof(HeaderRecordLine), nameof(HeaderDate), nameof(HeaderPhase), nameof(ContinueState), nameof(ContinueLabel),
            nameof(ContinueSubtitle), nameof(ContinueOptions), nameof(TickerItems), nameof(UserTeamName), nameof(UserTeamId),
            nameof(Breadcrumb), nameof(PageTitle), nameof(SelectedNavEntry),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}

/// <summary>One global search hit.</summary>
public sealed record SearchResult(EntityKind Kind, string Id, string Name, string Detail, ICommand Open)
{
    public bool IsPlayer => Kind == EntityKind.Player;

    public bool IsTeam => Kind == EntityKind.Team;

    public string Group => IsPlayer ? "PLAYERS" : "TEAMS";
}

/// <summary>Matches names in the loaded league: teams first, then players, prefix matches before contains.</summary>
internal static class SearchIndex
{
    public static IReadOnlyList<SearchResult> Find(
        IReadOnlyList<BallGM.Application.Leagues.TeamSummary> teams,
        IReadOnlyList<(string Id, string Name)> freeAgents,
        string query,
        ICommand open)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        static int Score(string name, string text) =>
            name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0
            : name.Split(' ', '-').Any(part => part.StartsWith(text, StringComparison.OrdinalIgnoreCase)) ? 1
            : name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 2
            : -1;

        var teamHits = teams
            .Select(team => (Team: team, Score: Score(team.TeamName, text)))
            .Where(hit => hit.Score >= 0)
            .OrderBy(hit => hit.Score).ThenBy(hit => hit.Team.TeamName, StringComparer.Ordinal)
            .Take(5)
            .Select(hit => new SearchResult(EntityKind.Team, hit.Team.TeamId, hit.Team.TeamName, $"{hit.Team.RosterCount} players", open));

        var players = teams.SelectMany(team => team.Roster.Select(spot => (spot.PlayerId, spot.FullName, Detail: $"{spot.Position} · {team.TeamName} · OVR {spot.Overall}")))
            .Concat(freeAgents.Select(agent => (PlayerId: agent.Id, FullName: agent.Name, Detail: "Free agent")));
        var playerHits = players
            .Select(player => (Player: player, Score: Score(player.FullName, text)))
            .Where(hit => hit.Score >= 0)
            .OrderBy(hit => hit.Score).ThenBy(hit => hit.Player.FullName, StringComparer.Ordinal)
            .Take(8)
            .Select(hit => new SearchResult(EntityKind.Player, hit.Player.PlayerId, hit.Player.FullName, hit.Player.Detail, open));

        return teamHits.Concat(playerHits).ToList();
    }
}
