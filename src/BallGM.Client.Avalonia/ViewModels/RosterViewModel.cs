using System.Globalization;
using System.Windows.Input;
using BallGM.Application.Leagues;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The squad screen: every player with rating, contract, and — once a season is under way — their
/// season line, sortable by any column the way a management sim's squad view is. The stat columns
/// are the shared <see cref="StatColumns.Season"/> set, on whichever <see cref="StatBasis"/> is chosen;
/// every figure is derived by <c>StatFormulas</c>, never here.
/// </summary>
public sealed partial class RosterViewModel : ViewModelBase
{
    private readonly LeagueOverview _overview;
    private readonly Func<IReadOnlyDictionary<string, PlayerSeasonTotals>> _seasonTotals;
    private readonly Func<string, DomainOperationResult<PlayerProfileSummary>>? _profile;
    private PlayerProfileViewModel? _openProfile;
    private string? _profileError;
    private TeamSummary? _team;
    private RosterSortColumn _sortColumn = RosterSortColumn.Overall;
    private bool _sortDescending = true;
    private int _sortStat;
    private IReadOnlyDictionary<string, PlayerSeasonTotals>? _cachedTotals;
    private readonly Func<DateOnly>? _ageDate;

    public RosterViewModel(
        LeagueOverview overview,
        Func<IReadOnlyDictionary<string, PlayerSeasonTotals>>? seasonTotals = null,
        Func<string, DomainOperationResult<PlayerProfileSummary>>? profile = null,
        Func<DateOnly>? ageDate = null)
    {
        _ageDate = ageDate;
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _seasonTotals = seasonTotals ?? (() => new Dictionary<string, PlayerSeasonTotals>());
        _profile = profile;
        OpenProfileCommand = new ParameterCommand<string>(ShowProfile);

        SortByOverallCommand = SortCommand(RosterSortColumn.Overall);
        SortByNameCommand = SortCommand(RosterSortColumn.Name);
        SortByPositionCommand = SortCommand(RosterSortColumn.Position);
        SortBySalaryCommand = SortCommand(RosterSortColumn.Salary);
        SortByYearsCommand = SortCommand(RosterSortColumn.Years);
        SortByRatingCommand = SortCommand(RosterSortColumn.Rating);
        SortByPotentialCommand = SortCommand(RosterSortColumn.Potential);
        SortByStatCommand = new ParameterCommand<int>(index => SortBy(RosterSortColumn.Stat, index));
        Basis.Changed += () =>
        {
            RaisePropertyChanged(nameof(StatHeaders));
            RaisePropertyChanged(nameof(Rows));
            RaiseTable();
        };
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
        _cachedTotals = null;
        foreach (var name in new[]
        {
            nameof(TeamName), nameof(FranchiseName), nameof(RosterCountLabel), nameof(Roster), nameof(Rows),
            nameof(AverageOverall), nameof(TopPlayerName), nameof(TopPlayerOverall), nameof(TopPlayerPortrait),
            nameof(InjuredCount), nameof(Payroll), nameof(TopPlayerId), nameof(HasSeasonStats), nameof(SortLabel),
        })
        {
            RaisePropertyChanged(name);
        }

        RaiseTable();
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

    public bool HasSeasonStats => Roster.Any(spot => SeasonTotals.ContainsKey(spot.PlayerId));

    /// <summary>
    /// The season's totals, read once per <see cref="Refresh"/> rather than on every sort or basis
    /// click: re-sorting or re-formatting the same season must not re-sum every box score in it.
    /// </summary>
    private IReadOnlyDictionary<string, PlayerSeasonTotals> SeasonTotals => _cachedTotals ??= _seasonTotals();

    public string SortLabel =>
        $"Sorted by {_sortColumn switch { RosterSortColumn.Stat => StatColumns.Season[_sortStat].Header, RosterSortColumn.Rating => "match rating", _ => _sortColumn.ToString().ToLowerInvariant() }}, {(_sortDescending ? "highest" : "lowest")} first · click a column to sort";

    /// <summary>The season stat columns, one header per column, labelled for the chosen basis.</summary>
    public IReadOnlyList<RosterStatHeader> StatHeaders =>
        StatColumns.Headers(StatColumns.Season, Basis.Basis)
            .Select((header, index) => new RosterStatHeader(
                header,
                index,
                _sortColumn == RosterSortColumn.Stat && _sortStat == index ? TableOrder.Glyph(_sortDescending) : string.Empty))
            .ToList();

    /// <summary>Per game, per 36 minutes, or totals — the shared choice every stat table offers.</summary>
    public StatBasisChoice Basis { get; } = new();

    /// <summary>The roster as table rows in the chosen sort order (best first by default).</summary>
    public IReadOnlyList<RosterRow> Rows
    {
        get
        {
            var totals = SeasonTotals;
            var asOf = _ageDate?.Invoke();
            var rows = Roster.Select(spot => totals.GetValueOrDefault(spot.PlayerId) is { } season
                    ? RosterRow.From(spot, season.Stats, Basis.Basis, season.AverageRating)
                    : RosterRow.From(spot, basis: Basis.Basis))
                .Select(row => asOf is { } date && row.BirthDate != default ? row with { Age = AgeOn(row.BirthDate, date) } : row)
                .ToList();
            IOrderedEnumerable<RosterRow> ordered = _sortColumn switch
            {
                RosterSortColumn.Name => Order(rows, row => row.FullName),
                RosterSortColumn.Position => Order(rows, row => row.Position),
                RosterSortColumn.Salary => Order(rows, row => row.SalarySortKey),
                RosterSortColumn.Years => Order(rows, row => row.YearsSortKey),
                RosterSortColumn.Stat => RosterRow.OrderByStat(rows, _sortStat, _sortDescending),
                RosterSortColumn.Rating => TableOrder.NullsLast(rows, row => row.RatingSortKey, _sortDescending),
                RosterSortColumn.Potential => Order(rows, row => row.PotentialSortKey),
                RosterSortColumn.Age => Order(rows, row => row.Age),
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

    /// <summary>Sorts by average match rating; players with no games come last either way.</summary>
    public RelayCommand SortByRatingCommand { get; }

    public RelayCommand SortByPotentialCommand { get; }

    /// <summary>Sorts by the season stat column at the index passed as the command parameter.</summary>
    public ParameterCommand<int> SortByStatCommand { get; }

    // Header tiles. Display summaries of the roster, not rules: nothing downstream reads them.
    public string AverageOverall => Roster.Count == 0 ? "–" : Math.Round(Roster.Average(spot => spot.Overall)).ToString("0", CultureInfo.InvariantCulture);

    public string TopPlayerName => Roster.Count == 0 ? "–" : Roster.MaxBy(spot => spot.Overall)!.FullName;

    public int TopPlayerOverall => Roster.Count == 0 ? 0 : Roster.Max(spot => spot.Overall);

    public string? TopPlayerPortrait => Roster.Count == 0 ? null : Roster.MaxBy(spot => spot.Overall)!.PortraitPath;

    public string? TopPlayerId => Roster.Count == 0 ? null : Roster.MaxBy(spot => spot.Overall)!.PlayerId;

    public ICommand OpenTopPlayerCommand => new ParameterCommand<string>(ShowProfile);

    public int InjuredCount => Roster.Count(spot => spot.IsInjured);

    public string Payroll => MoneyDisplay.ToMillions(Roster.Sum(spot => spot.CapCharge));

    private void ShowProfile(string playerId)
    {
        if (_openPlayer is not null)
        {
            _openPlayer(playerId);
            return;
        }

        if (_profile is null)
        {
            return;
        }

        var result = _profile(playerId);
        ProfileError = result.IsFailure ? string.Join(" ", result.Errors.Select(error => error.Message)) : null;
        Profile = result.IsSuccess ? new PlayerProfileViewModel(result.Value, () => Profile = null) : null;
    }

    private static int AgeOn(DateOnly birthDate, DateOnly asOf)
    {
        var age = asOf.Year - birthDate.Year;
        return asOf < birthDate.AddYears(age) ? age - 1 : age;
    }

    private RelayCommand SortCommand(RosterSortColumn column) => new(() => SortBy(column, 0));

    private void SortBy(RosterSortColumn column, int stat)
    {
        // Same column again flips the direction; a new column starts from its natural order
        // (names A-Z, numbers highest first).
        var same = column == _sortColumn && (column != RosterSortColumn.Stat || stat == _sortStat);
        _sortDescending = same ? !_sortDescending : column is not (RosterSortColumn.Name or RosterSortColumn.Position);
        _sortColumn = column;
        _sortStat = stat;
        RaisePropertyChanged(nameof(Rows));
        RaisePropertyChanged(nameof(SortLabel));
        RaisePropertyChanged(nameof(StatHeaders));
    }

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

    /// <summary>Average match rating this season.</summary>
    Rating,

    /// <summary>Potential Ability.</summary>
    Potential,

    Age,

    /// <summary>One of <see cref="StatColumns.Season"/>, by index.</summary>
    Stat,
}

/// <summary>
/// A sortable stat header on the roster: the shared header, its column index, and an arrow when the
/// roster is sorted by it — the sort is shown where it applies, not only in the caption below.
/// </summary>
public sealed record RosterStatHeader(StatHeader Header, int Index, string SortGlyph)
{
    public string Label => Header.Header + SortGlyph;
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
    IReadOnlyList<StatValue> Stats,
    IReadOnlyList<double?> StatSortKeys)
{
    public long SalarySortKey { get; init; }

    public int YearsSortKey { get; init; }

    /// <summary>His average match rating this season, or null before he has played.</summary>
    public RatingView? Rating { get; init; }

    /// <summary>Potential Ability as a coach's stars, as the profile shows it — never the exact figure.</summary>
    public string Potential { get; init; } = string.Empty;

    public int PotentialSortKey { get; init; }

    public int Age { get; init; }

    public DateOnly BirthDate { get; init; }

    /// <summary>The unrounded average, in hundredths: 7.04 ranks above 6.96 though both print 7.0.</summary>
    public int? RatingSortKey { get; init; }

    /// <summary>
    /// Rows ordered by one stat column. A player with nothing to rank (no games, no attempts) comes
    /// after every real figure whichever way the column runs — "no figure" is neither high nor low.
    /// </summary>
    public static IOrderedEnumerable<RosterRow> OrderByStat(IEnumerable<RosterRow> rows, int column, bool descending) =>
        TableOrder.NullsLast(rows, row => row.StatSortKeys[column], descending);

    public static RosterRow From(RosterSpot spot, CountingStats? season = null, StatBasis basis = StatBasis.PerGame, RatingAverage? rating = null)
    {
        var played = season is { Games: > 0 } ? season : null;

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
            StatColumns.Cells(played, StatColumns.Season, basis),
            StatColumns.Season.Select(column => played is null ? null : column.SortKey(played, basis)).ToList())
        {
            SalarySortKey = spot.CapCharge,
            YearsSortKey = spot.ContractSeasonsRemaining,
            Rating = RatingView.ForAverage(rating),
            Potential = AttributeSheetView.Stars(AbilityStars.HalfStars(spot.Potential)),
            PotentialSortKey = spot.Potential,
            RatingSortKey = rating?.Hundredths,
            Age = spot.Age,
            BirthDate = spot.BirthDate,
        };
    }
}
