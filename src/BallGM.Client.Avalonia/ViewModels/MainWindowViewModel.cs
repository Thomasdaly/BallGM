using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The navigation shell: which screen is showing, and which team every screen is showing it for.
/// Bare on purpose — dashboards, full navigation, and keyboard support are Milestone 11.
/// <para>
/// It also owns the refresh after a trade. The roster, cap sheet, and pick board are projections of
/// a league that has just changed, so they are rebuilt from the new overview rather than patched;
/// the trade screen keeps itself, because it is the one holding the result a GM is reading.
/// </para>
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly LeagueSession? _session;
    private RosterViewModel? _roster;
    private CapSheetViewModel? _capSheet;
    private PickBoardViewModel? _pickBoard;
    private object? _currentScreen;
    private string _selectedSection = string.Empty;
    private TeamSummary? _selectedTeam;
    private IReadOnlyList<TeamSummary> _teams;

    public MainWindowViewModel(LeagueOverview overview, LeagueSession session)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(session);

        HasLeague = true;
        _session = session;
        LeagueName = overview.LeagueName;

        // The ruleset clause only earns its space when a data pack names the ruleset something other
        // than the league; otherwise the header printed the same long title twice.
        LeagueSubtitle = string.Equals(overview.RulesetName, overview.LeagueName, StringComparison.Ordinal)
            ? $"{overview.Teams.Count} teams · {overview.RegularSeasonGameCount}-game regular season"
            : $"{overview.Teams.Count} teams · {overview.RegularSeasonGameCount}-game regular season · ruleset \"{overview.RulesetName}\"";

        _teams = overview.Teams;
        _roster = new RosterViewModel(overview, session.PlayerSeasonTotals, session.PlayerProfile);
        _capSheet = new CapSheetViewModel(overview);
        _pickBoard = new PickBoardViewModel(overview);
        Trade = new TradeProposalViewModel(overview, session, ApplyLeagueChange);
        FreeAgency = new FreeAgencyViewModel(overview, session, ApplyLeagueChange);
        FreeAgencyBoard = new FreeAgencyBoardViewModel(overview, session, ApplyLeagueChange);
        Season = new SeasonViewModel(session, ApplyLeagueChange);
        FrontOffice = new FrontOfficeViewModel(session, ApplyLeagueChange);
        Contracts = new ContractsViewModel(session, () => ApplyLeagueChange(session.Overview() is { IsSuccess: true } refreshed ? refreshed.Value : overview));

        Sections = [_roster.Title, _capSheet.Title, Contracts.Title, _pickBoard.Title, Trade.Title, FreeAgency.Title, FreeAgencyBoard.Title, Season.Title, FrontOffice.Title];
        SelectedTeam = Teams.FirstOrDefault();
        SelectedSection = Sections[0];
    }

    /// <summary>Load failed. The shell still opens, so the reason is visible instead of a silent crash.</summary>
    public MainWindowViewModel(IReadOnlyList<string> loadErrors)
    {
        ArgumentNullException.ThrowIfNull(loadErrors);

        HasLeague = false;
        LeagueName = "League failed to load";
        LeagueSubtitle = "The client could not build a league from the configured ruleset file.";
        LoadErrors = loadErrors;
        _teams = [];
        Sections = [];
        Trade = null;
        FreeAgency = null;
        FreeAgencyBoard = null;
        Season = null;
        FrontOffice = null;
        Contracts = null;
    }

    public bool HasLeague { get; }

    public string LeagueName { get; }

    public string LeagueSubtitle { get; }

    public IReadOnlyList<string> LoadErrors { get; } = [];

    public IReadOnlyList<TeamSummary> Teams
    {
        get => _teams;
        private set => SetProperty(ref _teams, value);
    }

    public IReadOnlyList<string> Sections { get; }

    public TradeProposalViewModel? Trade { get; }

    public FreeAgencyViewModel? FreeAgency { get; }

    public FreeAgencyBoardViewModel? FreeAgencyBoard { get; }

    /// <summary>
    /// The calendar screen. Held for the run rather than rebuilt on every league change: it owns the
    /// season in progress, and throwing it away would discard the day the league has reached.
    /// </summary>
    public SeasonViewModel? Season { get; }

    /// <summary>
    /// The Milestone 9 diagnostics-and-turn screen. Rebuilt freely like every other read screen — it
    /// holds no in-progress form the way the trade or free-agency screens do, even though its own
    /// "run AI turn" button can now change the league.
    /// </summary>
    public FrontOfficeViewModel? FrontOffice { get; }

    /// <summary>Payroll outlook, upcoming free agents, and re-signing. Held for the run: refusals are session state.</summary>
    public ContractsViewModel? Contracts { get; }

    /// <summary>The banner's alert chip: players who have refused to re-sign with the selected team.</summary>
    public string AlertLine => Contracts is null || _selectedTeam is null
        ? string.Empty
        : Contracts.Alerts.Count switch
        {
            0 => string.Empty,
            1 => "1 player refused to re-sign",
            var count => $"{count} players refused to re-sign",
        };

    public bool HasAlerts => AlertLine.Length > 0;

    /// <summary>
    /// The banner's second line: the roster at a glance, plus the franchise name only when it says
    /// something the team name does not (in many leagues the two are the same).
    /// </summary>
    public string SelectedTeamDetail => _selectedTeam is null
        ? string.Empty
        : string.Join(" · ", new[]
        {
            string.Equals(_selectedTeam.FranchiseName, _selectedTeam.TeamName, StringComparison.Ordinal) ? null : _selectedTeam.FranchiseName,
            $"{_selectedTeam.RosterCount} players",
            $"{MoneyDisplay.ToMillions(_selectedTeam.Roster.Sum(spot => spot.CapCharge))} payroll",
        }.Where(part => part is not null));

    public TeamSummary? SelectedTeam
    {
        get => _selectedTeam;
        set
        {
            // Replacing the team list after a league change makes the combo box push a transient
            // null before it re-reads the selection. Taking that null would leave the team-scoped
            // screens reading "no team selected" for a team that is still selected.
            if (value is null && Teams.Count > 0)
            {
                return;
            }

            if (!SetProperty(ref _selectedTeam, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(SelectedTeamDetail));

            if (_roster is not null)
            {
                _roster.Team = value;
            }

            if (_capSheet is not null)
            {
                _capSheet.Team = value;
            }

            if (_pickBoard is not null)
            {
                _pickBoard.Team = value;
            }

            if (FreeAgencyBoard is not null)
            {
                FreeAgencyBoard.Team = value;
            }

            if (FrontOffice is not null)
            {
                FrontOffice.Team = value;
            }

            if (Contracts is not null)
            {
                Contracts.Team = value;
            }

            RaisePropertyChanged(nameof(AlertLine));
            RaisePropertyChanged(nameof(HasAlerts));
        }
    }

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (!SetProperty(ref _selectedSection, value))
            {
                return;
            }

            CurrentScreen = value switch
            {
                _ when _capSheet is not null && value == _capSheet.Title => _capSheet,
                _ when _pickBoard is not null && value == _pickBoard.Title => _pickBoard,
                _ when Trade is not null && value == Trade.Title => Trade,
                _ when FreeAgency is not null && value == FreeAgency.Title => FreeAgency,
                _ when FreeAgencyBoard is not null && value == FreeAgencyBoard.Title => FreeAgencyBoard,
                _ when Season is not null && value == Season.Title => Season,
                _ when FrontOffice is not null && value == FrontOffice.Title => FrontOffice,
                _ when Contracts is not null && value == Contracts.Title => Contracts,
                _ => _roster,
            };

            // Games may have been played, or contracts signed, since these screens were last drawn.
            if (CurrentScreen is RosterViewModel roster)
            {
                roster.Refresh();
            }

            if (CurrentScreen is ContractsViewModel contracts)
            {
                contracts.Refresh();
            }

            RaisePropertyChanged(nameof(AlertLine));
            RaisePropertyChanged(nameof(HasAlerts));
        }
    }

    public object? CurrentScreen
    {
        get => _currentScreen;
        private set => SetProperty(ref _currentScreen, value);
    }

    /// <summary>
    /// Rebuilds the read-only screens against a league that has just changed, keeping the team the
    /// GM was looking at. Cheap enough to do wholesale — these are projections, not state.
    /// </summary>
    private void ApplyLeagueChange(LeagueOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        var selectedTeamId = _selectedTeam?.TeamId;

        Teams = overview.Teams;
        _roster = new RosterViewModel(overview, _session is null ? null : _session.PlayerSeasonTotals, _session is null ? null : _session.PlayerProfile);
        _capSheet = new CapSheetViewModel(overview);
        _pickBoard = new PickBoardViewModel(overview);

        _selectedTeam = Teams.FirstOrDefault(team => team.TeamId == selectedTeamId) ?? Teams.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedTeam));
        RaisePropertyChanged(nameof(SelectedTeamDetail));

        _roster.Team = _selectedTeam;
        _capSheet.Team = _selectedTeam;
        _pickBoard.Team = _selectedTeam;
        if (FreeAgencyBoard is not null)
        {
            FreeAgencyBoard.Team = _selectedTeam;
        }

        if (FrontOffice is not null)
        {
            FrontOffice.Team = _selectedTeam;
        }

        if (Contracts is not null)
        {
            Contracts.Team = _selectedTeam;
            Contracts.Refresh();
        }

        RaisePropertyChanged(nameof(AlertLine));
        RaisePropertyChanged(nameof(HasAlerts));

        // The board rebuilds itself against the new league rather than being replaced: it is holding
        // a day, a selected free agent, and the standings of a market a GM is in the middle of
        // reading, and none of that survives being thrown away.
        FreeAgencyBoard?.RefreshFrom(overview);

        // Read fresh rather than kept: a diagnostics screen showing stale suggestions after a trade
        // or signing just happened would be actively misleading about what is still legal to do.
        FrontOffice?.Refresh();

        // The trade and free-agency screens stay put: whichever one is showing is showing the result
        // of what just happened, and rebuilding it would throw that away the moment it became worth
        // reading. Both refresh their own bindings from the new overview instead.
        CurrentScreen = _currentScreen switch
        {
            CapSheetViewModel => _capSheet,
            PickBoardViewModel => _pickBoard,
            RosterViewModel => _roster,
            _ => _currentScreen,
        };
    }
}
