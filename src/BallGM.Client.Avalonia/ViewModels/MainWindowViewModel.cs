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
        LeagueName = overview.LeagueName;

        // The ruleset clause only earns its space when a data pack names the ruleset something other
        // than the league; otherwise the header printed the same long title twice.
        LeagueSubtitle = string.Equals(overview.RulesetName, overview.LeagueName, StringComparison.Ordinal)
            ? $"{overview.Teams.Count} teams · {overview.RegularSeasonGameCount}-game regular season"
            : $"{overview.Teams.Count} teams · {overview.RegularSeasonGameCount}-game regular season · ruleset \"{overview.RulesetName}\"";

        _teams = overview.Teams;
        _roster = new RosterViewModel(overview);
        _capSheet = new CapSheetViewModel(overview);
        _pickBoard = new PickBoardViewModel(overview);
        Trade = new TradeProposalViewModel(overview, session, ApplyLeagueChange);
        FreeAgency = new FreeAgencyViewModel(overview, session, ApplyLeagueChange);

        Sections = [_roster.Title, _capSheet.Title, _pickBoard.Title, Trade.Title, FreeAgency.Title];
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

    public TeamSummary? SelectedTeam
    {
        get => _selectedTeam;
        set
        {
            if (!SetProperty(ref _selectedTeam, value))
            {
                return;
            }

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
                _ => _roster,
            };
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
        _roster = new RosterViewModel(overview);
        _capSheet = new CapSheetViewModel(overview);
        _pickBoard = new PickBoardViewModel(overview);

        _selectedTeam = Teams.FirstOrDefault(team => team.TeamId == selectedTeamId) ?? Teams.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedTeam));

        _roster.Team = _selectedTeam;
        _capSheet.Team = _selectedTeam;
        _pickBoard.Team = _selectedTeam;

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
