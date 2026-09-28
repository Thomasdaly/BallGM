using System.Globalization;
using BallGM.Application.Leagues;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The squad screen: every player with rating, contract, and — once a season is under way — their
/// season line, sortable by any column the way a management sim's squad view is. Per-game averages
/// are computed here because they are a display choice; the season totals come from the session.
/// </summary>
public sealed class RosterViewModel : ViewModelBase
{
    private readonly LeagueOverview _overview;
    private readonly Func<IReadOnlyDictionary<string, PlayerSeasonTotals>> _seasonTotals;
    private readonly Func<string, DomainOperationResult<PlayerProfileSummary>>? _profile;
    private PlayerProfileViewModel? _openProfile;
    private string? _profileError;
    private TeamSummary? _team;
    private RosterSortColumn _sortColumn = RosterSortColumn.Overall;
    private bool _sortDescending = true;

    public RosterViewModel(
        LeagueOverview overview,
        Func<IReadOnlyDictionary<string, PlayerSeasonTotals>>? seasonTotals = null,
        Func<string, DomainOperationResult<PlayerProfileSummary>>? profile = null)
    {
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _seasonTotals = seasonTotals ?? (() => new Dictionary<string, PlayerSeasonTotals>());
        _profile = profile;
        OpenProfileCommand = new ParameterCommand<string>(ShowProfile);

        SortByOverallCommand = SortCommand(RosterSortColumn.Overall);
        SortByNameCommand = SortCommand(RosterSortColumn.Name);
        SortByPositionCommand = SortCommand(RosterSortColumn.Position);
        SortBySalaryCommand = SortCommand(RosterSortColumn.Salary);
        SortByYearsCommand = SortCommand(RosterSortColumn.Years);
        SortByGamesCommand = SortCommand(RosterSortColumn.Games);
        SortByMinutesCommand = SortCommand(RosterSortColumn.Minutes);
        SortByPointsCommand = SortCommand(RosterSortColumn.Points);
        SortByReboundsCommand = SortCommand(RosterSortColumn.Rebounds);
        SortByAssistsCommand = SortCommand(RosterSortColumn.Assists);
    }

    public string Title => "Roster";

    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (SetProperty(ref _team, value))
            {
                Profile = null;
                Refresh();
            }
        }
    }

    /// <summary>Re-reads everything derived from the team and the season — after games are played, say.</summary>
    public void Refresh()
    {
        foreach (var name in new[]
        {
            nameof(TeamName), nameof(FranchiseName), nameof(RosterCountLabel), nameof(Roster), nameof(Rows),
            nameof(AverageOverall), nameof(TopPlayerName), nameof(TopPlayerOverall), nameof(TopPlayerPortrait),
            nameof(InjuredCount), nameof(Payroll), nameof(HasSeasonStats), nameof(SortLabel),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    /// <summary>The profile open over the squad list, if any. Null shows the list.</summary>
    public PlayerProfileViewModel? Profile
    {
        get => _openProfile;
        private set
        {
            if (SetProperty(ref _openProfile, value))
            {
                RaisePropertyChanged(nameof(IsShowingList));
            }
        }
    }

    public bool IsShowingList => _openProfile is null;

    public string? ProfileError
    {
        get => _profileError;
        private set => SetProperty(ref _profileError, value);
    }

    public ParameterCommand<string> OpenProfileCommand { get; }

    public string TeamName => _team?.TeamName ?? "No team selected";

    public string FranchiseName => _team is null ? string.Empty : $"Franchise: {_team.FranchiseName}";

    public string RosterCountLabel =>
        _team is null
            ? string.Empty
            : $"{_team.RosterCount} under contract — ruleset allows {_overview.MinimumRosterPlayers}–{_overview.MaximumRosterPlayers}";

    public IReadOnlyList<RosterSpot> Roster => _team?.Roster ?? [];

    public bool HasSeasonStats => Roster.Any(spot => _seasonTotals().ContainsKey(spot.PlayerId));

    public string SortLabel => $"Sorted by {_sortColumn.ToString().ToLowerInvariant()}, {(_sortDescending ? "highest" : "lowest")} first · click a column to sort";

    /// <summary>The roster as table rows in the chosen sort order (best first by default).</summary>
    public IReadOnlyList<RosterRow> Rows
    {
        get
        {
            var totals = _seasonTotals();
            var rows = Roster.Select(spot => RosterRow.From(spot, totals.GetValueOrDefault(spot.PlayerId))).ToList();
            IOrderedEnumerable<RosterRow> ordered = _sortColumn switch
            {
                RosterSortColumn.Name => Order(rows, row => row.FullName),
                RosterSortColumn.Position => Order(rows, row => row.Position),
                RosterSortColumn.Salary => Order(rows, row => row.SalarySortKey),
                RosterSortColumn.Years => Order(rows, row => row.YearsSortKey),
                RosterSortColumn.Games => Order(rows, row => row.GamesSortKey),
                RosterSortColumn.Minutes => Order(rows, row => row.MinutesSortKey),
                RosterSortColumn.Points => Order(rows, row => row.PointsSortKey),
                RosterSortColumn.Rebounds => Order(rows, row => row.ReboundsSortKey),
                RosterSortColumn.Assists => Order(rows, row => row.AssistsSortKey),
                _ => Order(rows, row => row.Overall),
            };
            return ordered.ThenBy(row => row.FullName, StringComparer.Ordinal).ToList();
        }
    }

    public RelayCommand SortByOverallCommand { get; }

    public RelayCommand SortByNameCommand { get; }

    public RelayCommand SortByPositionCommand { get; }

    public RelayCommand SortBySalaryCommand { get; }

    public RelayCommand SortByYearsCommand { get; }

    public RelayCommand SortByGamesCommand { get; }

    public RelayCommand SortByMinutesCommand { get; }

    public RelayCommand SortByPointsCommand { get; }

    public RelayCommand SortByReboundsCommand { get; }

    public RelayCommand SortByAssistsCommand { get; }

    // Header tiles. Display summaries of the roster, not rules: nothing downstream reads them.
    public string AverageOverall => Roster.Count == 0 ? "–" : Math.Round(Roster.Average(spot => spot.Overall)).ToString("0", CultureInfo.InvariantCulture);

    public string TopPlayerName => Roster.Count == 0 ? "–" : Roster.MaxBy(spot => spot.Overall)!.FullName;

    public int TopPlayerOverall => Roster.Count == 0 ? 0 : Roster.Max(spot => spot.Overall);

    public string? TopPlayerPortrait => Roster.Count == 0 ? null : Roster.MaxBy(spot => spot.Overall)!.PortraitPath;

    public int InjuredCount => Roster.Count(spot => spot.IsInjured);

    public string Payroll => MoneyDisplay.ToMillions(Roster.Sum(spot => spot.CapCharge));

    private void ShowProfile(string playerId)
    {
        if (_profile is null)
        {
            return;
        }

        var result = _profile(playerId);
        ProfileError = result.IsFailure ? string.Join(" ", result.Errors.Select(error => error.Message)) : null;
        Profile = result.IsSuccess ? new PlayerProfileViewModel(result.Value, () => Profile = null) : null;
    }

    private RelayCommand SortCommand(RosterSortColumn column) => new(() =>
    {
        // Same column again flips the direction; a new column starts from its natural order
        // (names A-Z, numbers highest first).
        _sortDescending = column == _sortColumn ? !_sortDescending : column is not (RosterSortColumn.Name or RosterSortColumn.Position);
        _sortColumn = column;
        RaisePropertyChanged(nameof(Rows));
        RaisePropertyChanged(nameof(SortLabel));
    });

    private IOrderedEnumerable<RosterRow> Order<TKey>(IEnumerable<RosterRow> rows, Func<RosterRow, TKey> key) =>
        _sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key);
}

public enum RosterSortColumn
{
    Overall,
    Name,
    Position,
    Salary,
    Years,
    Games,
    Minutes,
    Points,
    Rebounds,
    Assists,
}

public sealed record RosterRow(
    string PlayerId,
    string FullName,
    string Position,
    int Overall,
    string Salary,
    string Years,
    bool IsInjured,
    string? InjuryDescription,
    string? PortraitPath,
    string Games,
    string MinutesPerGame,
    string PointsPerGame,
    string ReboundsPerGame,
    string AssistsPerGame)
{
    public long SalarySortKey { get; init; }

    public int YearsSortKey { get; init; }

    public int GamesSortKey { get; init; }

    public double MinutesSortKey { get; init; }

    public double PointsSortKey { get; init; }

    public double ReboundsSortKey { get; init; }

    public double AssistsSortKey { get; init; }

    public static RosterRow From(RosterSpot spot, PlayerSeasonTotals? totals = null)
    {
        var games = totals?.GamesPlayed ?? 0;
        double PerGame(int total) => games == 0 ? 0 : (double)total / games;
        string Show(int total) => games == 0 ? "–" : PerGame(total).ToString("0.0", CultureInfo.InvariantCulture);

        return new RosterRow(
            spot.PlayerId,
            spot.FullName,
            spot.Position,
            spot.Overall,
            MoneyDisplay.ToMillions(spot.CapCharge),
            spot.ContractSeasonsRemaining switch
            {
                <= 0 => "–",
                1 => "1 yr",
                var years => $"{years} yrs",
            },
            spot.IsInjured,
            spot.InjuryDescription,
            spot.PortraitPath,
            games == 0 ? "–" : games.ToString(CultureInfo.InvariantCulture),
            Show(totals?.Minutes ?? 0),
            Show(totals?.Points ?? 0),
            Show(totals?.Rebounds ?? 0),
            Show(totals?.Assists ?? 0))
        {
            SalarySortKey = spot.CapCharge,
            YearsSortKey = spot.ContractSeasonsRemaining,
            GamesSortKey = games,
            MinutesSortKey = PerGame(totals?.Minutes ?? 0),
            PointsSortKey = PerGame(totals?.Points ?? 0),
            ReboundsSortKey = PerGame(totals?.Rebounds ?? 0),
            AssistsSortKey = PerGame(totals?.Assists ?? 0),
        };
    }
}
