using System.Collections.ObjectModel;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The league's statistical leaders and its full player stats table, for the regular season in
/// progress. Who qualifies is the ruleset's <c>statLeaders</c> section, applied by the Application
/// layer (<see cref="LeagueStatsSummary.Qualifies"/>); every figure is formatted by
/// <see cref="StatColumns"/> and derived by <c>StatFormulas</c>.
/// </summary>
public sealed class LeadersViewModel : ViewModelBase
{
    /// <summary>How many rows of the player table are drawn at once; "show more" adds this many again.</summary>
    public const int PageSize = 50;

    private const int BoardSize = 5;

    private readonly Func<LeagueStatsSummary> _stats;
    private readonly Func<string, DomainOperationResult<PlayerProfileSummary>>? _profile;
    private readonly ObservableCollection<LeagueTableRow> _rows = [];
    private LeagueStatsSummary? _cached;
    private IReadOnlyList<LeaguePlayerLine>? _sorted;
    private IReadOnlyList<LeaderBoard>? _boards;
    private PlayerProfileViewModel? _openProfile;
    private int _sortStat = StatColumns.Season.ToList().IndexOf(StatColumns.Points);
    private bool _sortByRating;
    private bool _sortDescending = true;
    private bool _qualifiedOnly = true;

    public LeadersViewModel(Func<LeagueStatsSummary> stats, Func<string, DomainOperationResult<PlayerProfileSummary>>? profile = null)
    {
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _profile = profile;
        SortByStatCommand = new ParameterCommand<int>(index => SortBy(index, rating: false));
        SortByRatingCommand = new RelayCommand(() => SortBy(_sortStat, rating: true));
        ShowMoreCommand = new RelayCommand(ShowMore);
        OpenProfileCommand = new ParameterCommand<string>(ShowProfile);
        Basis.Changed += () =>
        {
            _boards = null;
            RaisePropertyChanged(nameof(Boards));
            RaisePropertyChanged(nameof(StatHeaders));
            ResetTable();
        };
    }

    public string Title => "Leaders";

    /// <summary>Per game, per 36 or totals, shared by both tabs.</summary>
    public StatBasisChoice Basis { get; } = new();

    public bool HasStats => Stats.Players.Count > 0;

    /// <summary>Who qualifies, in words, from the ruleset's own figures.</summary>
    public string QualifierNote
    {
        get
        {
            var qualifiers = Stats.Qualifiers;
            return $"Per-game and per-36 leaders need {DisplayText.Share(qualifiers.MinimumGamesShareBasisPoints)}% of the season's games, and per-36 leaders {qualifiers.MinimumMinutesPerGame} minutes a game. " +
                $"Shooting leaders also need, per team game, {DisplayText.Made(qualifiers.MinimumFieldGoalsMadePerTeamGame, "field goal")}, " +
                $"{DisplayText.Made(qualifiers.MinimumThreesMadePerTeamGame, "three")} or {DisplayText.Made(qualifiers.MinimumFreeThrowsMadePerTeamGame, "free throw")}. Totals count everyone.";
        }
    }

    /// <summary>The top five in each category on the chosen basis, worked out once per basis and refresh.</summary>
    public IReadOnlyList<LeaderBoard> Boards => _boards ??= HasStats
        ? LeaderCategory.All.Select(category => category.Board(Stats, Basis.Basis, BoardSize)).ToList()
        : [];

    /// <summary>Whether the table lists only players who qualify on games.</summary>
    public bool QualifiedOnly
    {
        get => _qualifiedOnly;
        set
        {
            if (SetProperty(ref _qualifiedOnly, value))
            {
                ResetTable();
            }
        }
    }

    public IReadOnlyList<RosterStatHeader> StatHeaders =>
        StatColumns.Headers(StatColumns.Season, Basis.Basis)
            .Select((header, index) => new RosterStatHeader(
                header,
                index,
                !_sortByRating && _sortStat == index ? TableOrder.Glyph(_sortDescending) : string.Empty))
            .ToList();

    public string RatingHeader => "RTG" + (_sortByRating ? TableOrder.Glyph(_sortDescending) : string.Empty);

    public string ShowMoreLabel => $"Show {PageSize} more";

    /// <summary>
    /// The rows drawn so far: the first page of <see cref="Sorted"/>, and another page per "show more".
    /// Appended to rather than rebuilt, so paging never redraws the rows already on screen, and only
    /// drawn rows are formatted.
    /// </summary>
    public ObservableCollection<LeagueTableRow> Rows
    {
        get
        {
            if (_sorted is null)
            {
                FillFirstPage();
            }

            return _rows;
        }
    }

    public bool CanShowMore => Sorted.Count > Rows.Count;

    public string TableCaption
    {
        get
        {
            var sortedBy = _sortByRating ? "match rating" : StatColumns.Season[_sortStat].Header;
            return $"Showing {Rows.Count} of {DisplayText.Count(Sorted.Count, "player")} · sorted by {sortedBy}, {(_sortDescending ? "highest" : "lowest")} first · click a column to sort, a player for his profile";
        }
    }

    public ParameterCommand<int> SortByStatCommand { get; }

    public RelayCommand SortByRatingCommand { get; }

    public RelayCommand ShowMoreCommand { get; }

    public ParameterCommand<string> OpenProfileCommand { get; }

    /// <summary>The profile open over the screen, if any.</summary>
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

    /// <summary>
    /// Re-reads the league's stats — after games are played, say. The session hands back the same
    /// summary while nothing has changed, and then nothing here is rebuilt either.
    /// </summary>
    public void Refresh()
    {
        Profile = null;
        var fresh = _stats();
        if (ReferenceEquals(fresh, _cached))
        {
            return;
        }

        _cached = fresh;
        _boards = null;
        RaisePropertyChanged(nameof(HasStats));
        RaisePropertyChanged(nameof(QualifierNote));
        RaisePropertyChanged(nameof(Boards));
        ResetTable();
    }

    private LeagueStatsSummary Stats => _cached ??= _stats();

    /// <summary>Every player the table could list, in the chosen order — sorted on the stat lines, so nothing is formatted to be sorted.</summary>
    private IReadOnlyList<LeaguePlayerLine> Sorted => _sorted ?? FillFirstPage();

    private IReadOnlyList<LeaguePlayerLine> FillFirstPage()
    {
        var stats = Stats;
        var basis = Basis.Basis;
        var column = StatColumns.Season[_sortStat];
        var listed = stats.Players.Where(line => !_qualifiedOnly || stats.QualifiesOnGames(line));
        _sorted = TableOrder.NullsLast(listed, line => _sortByRating ? line.Rating?.Hundredths : column.SortKey(line.Stats, basis), _sortDescending)
            .ThenBy(line => line.FullName, StringComparer.Ordinal)
            .ToList();

        _rows.Clear();
        AppendPage();
        return _sorted;
    }

    private void AppendPage()
    {
        foreach (var line in _sorted!.Skip(_rows.Count).Take(PageSize))
        {
            _rows.Add(LeagueTableRow.From(line, Basis.Basis));
        }
    }

    private void ShowMore()
    {
        _ = Rows;
        AppendPage();
        RaisePropertyChanged(nameof(CanShowMore));
        RaisePropertyChanged(nameof(TableCaption));
    }

    private void SortBy(int stat, bool rating)
    {
        var same = rating ? _sortByRating : !_sortByRating && stat == _sortStat;
        _sortDescending = !same || !_sortDescending;
        _sortByRating = rating;
        _sortStat = stat;
        RaisePropertyChanged(nameof(StatHeaders));
        ResetTable();
    }

    /// <summary>Back to the first page, in the current order: after a sort, basis, filter or refresh.</summary>
    private void ResetTable()
    {
        _sorted = null;
        _ = Rows;
        RaisePropertyChanged(nameof(CanShowMore));
        RaisePropertyChanged(nameof(TableCaption));
        RaisePropertyChanged(nameof(RatingHeader));
    }

    private void ShowProfile(string playerId)
    {
        if (_profile?.Invoke(playerId) is { IsSuccess: true } result)
        {
            Profile = new PlayerProfileViewModel(result.Value, () => Profile = null, "leaders");
        }
    }
}

/// <summary>One row of the league's player table.</summary>
public sealed record LeagueTableRow(
    string PlayerId,
    string Name,
    string Team,
    string Position,
    RatingView? Rating,
    IReadOnlyList<StatValue> Cells)
{
    public static LeagueTableRow From(LeaguePlayerLine line, StatBasis basis) => new(
        line.PlayerId,
        line.FullName,
        line.TeamName ?? "Free agent",
        line.Position,
        RatingView.ForAverage(line.Rating),
        StatColumns.Cells(line.Stats, StatColumns.Season, basis));
}

/// <summary>One leaderboard: a category's top players, ranked, with the figure that ranks them.</summary>
public sealed record LeaderBoard(string Title, string Header, string Tooltip, IReadOnlyList<LeaderRow> Leaders)
{
    public bool IsEmpty => Leaders.Count == 0;
}

/// <summary>One place on a leaderboard. Rank is printed, so order is never carried by position alone.</summary>
public sealed record LeaderRow(int Rank, string PlayerId, string Name, string Team, string Value, string Header)
{
    public string AccessibleName => $"{Rank}. {Name}, {Team}, {Value} {Header}";
}

/// <summary>
/// A leaderboard category: what ranks it and how it is printed. Who may appear on it is the Application
/// layer's rule, asked by the category's column code (<see cref="LeagueStatsSummary.Qualifies"/>).
/// </summary>
public sealed record LeaderCategory(
    string Title,
    string Header,
    string Tooltip,
    Func<LeaguePlayerLine, StatBasis, double?> Key,
    Func<LeaguePlayerLine, StatBasis, string> Format)
{
    public static IReadOnlyList<LeaderCategory> All { get; } =
    [
        FromColumn("Points", StatColumns.Points),
        FromColumn("Rebounds", StatColumns.Rebounds),
        FromColumn("Assists", StatColumns.Assists),
        FromColumn("Steals", StatColumns.Steals),
        FromColumn("Blocks", StatColumns.Blocks),
        FromColumn("Plus-minus", StatColumns.PlusMinus),
        FromColumn("Field-goal percentage", StatColumns.FieldGoalPercent),
        FromColumn("Three-point percentage", StatColumns.ThreePointPercent),
        FromColumn("Free-throw percentage", StatColumns.FreeThrowPercent),
        FromColumn("True shooting", StatColumns.TrueShooting),
        new(
            "Match rating",
            "RTG",
            StatColumns.RatingTooltip + ". Needs the games share whatever the basis.",
            (line, _) => line.Rating?.Hundredths,
            (line, _) => line.Rating is { } rating ? RatingView.TextOf(rating.Tenths) : StatColumns.Missing),
    ];

    /// <summary>The top players among those who qualify, each ranked on a key worked out once.</summary>
    public LeaderBoard Board(LeagueStatsSummary stats, StatBasis basis, int size)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var leaders = stats.Players
            .Where(line => stats.Qualifies(Header, line, basis))
            .Select(line => (Line: line, Key: Key(line, basis)))
            .Where(entry => entry.Key is not null)
            .OrderByDescending(entry => entry.Key)
            .ThenBy(entry => entry.Line.FullName, StringComparer.Ordinal)
            .Take(size)
            .Select((entry, index) => new LeaderRow(index + 1, entry.Line.PlayerId, entry.Line.FullName, entry.Line.TeamName ?? "Free agent", Format(entry.Line, basis), Header))
            .ToList();
        return new LeaderBoard(Title, Header, Tooltip, leaders);
    }

    private static LeaderCategory FromColumn(string title, StatColumn column) => new(
        title,
        column.Header,
        column.Tooltip,
        (line, basis) => column.SortKey(line.Stats, basis),
        (line, basis) => column.Format(line.Stats, basis));
}
