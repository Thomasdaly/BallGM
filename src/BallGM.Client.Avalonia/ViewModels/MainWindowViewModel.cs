using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The navigation shell: which screen is showing, and which team every screen is showing it for.
/// Bare on purpose — dashboards, full navigation, and keyboard support are Milestone 11.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly RosterViewModel? _roster;
    private readonly CapSheetViewModel? _capSheet;
    private readonly PickBoardViewModel? _pickBoard;
    private object? _currentScreen;
    private string _selectedSection = string.Empty;
    private TeamSummary? _selectedTeam;

    public MainWindowViewModel(LeagueOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        HasLeague = true;
        LeagueName = overview.LeagueName;

        // The ruleset clause only earns its space when a data pack names the ruleset something other
        // than the league; otherwise the header printed the same long title twice.
        LeagueSubtitle = string.Equals(overview.RulesetName, overview.LeagueName, StringComparison.Ordinal)
            ? $"{overview.Teams.Count} teams · {overview.RegularSeasonGameCount}-game regular season"
            : $"{overview.Teams.Count} teams · {overview.RegularSeasonGameCount}-game regular season · ruleset \"{overview.RulesetName}\"";
        Teams = overview.Teams;

        _roster = new RosterViewModel(overview);
        _capSheet = new CapSheetViewModel(overview);
        _pickBoard = new PickBoardViewModel(overview);
        Trade = new TradeProposalViewModel(overview);

        Sections = [_roster.Title, _capSheet.Title, _pickBoard.Title, Trade.Title];
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
        Teams = [];
        Sections = [];
        Trade = null;
    }

    public bool HasLeague { get; }

    public string LeagueName { get; }

    public string LeagueSubtitle { get; }

    public IReadOnlyList<string> LoadErrors { get; } = [];

    public IReadOnlyList<TeamSummary> Teams { get; }

    public IReadOnlyList<string> Sections { get; }

    public TradeProposalViewModel? Trade { get; }

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
                _ => _roster,
            };
        }
    }

    public object? CurrentScreen
    {
        get => _currentScreen;
        private set => SetProperty(ref _currentScreen, value);
    }
}
