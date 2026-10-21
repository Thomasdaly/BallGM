using System.Globalization;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The player profile: identity, the five attributes as bars, contract, recent form, and career —
/// the screen a management sim opens when a name is clicked. Averages and percentages are display
/// choices made here; everything counted comes from the session's profile query.
/// </summary>
public sealed class PlayerProfileViewModel(PlayerProfileSummary profile, Action back, string backTo = "roster") : ViewModelBase
{
    /// <summary>The back button, naming the screen it returns to.</summary>
    public string BackLabel { get; } = $"‹ Back to {backTo}";

    private StatBasisChoice? _basis;

    public RelayCommand BackCommand { get; } = new(back);

    public string FullName => profile.FullName;

    public string Position => profile.Position;

    public int Overall => profile.Overall;

    public string? PortraitPath => profile.PortraitPath;

    public string TeamLine => profile.TeamName ?? "Free agent";

    public string BioLine => string.Create(
        CultureInfo.InvariantCulture,
        $"Age {profile.Age} · born {profile.BirthDate:d MMM yyyy} · {profile.SeasonsOfService} season{(profile.SeasonsOfService == 1 ? string.Empty : "s")} of service");

    public bool IsInjured => profile.IsInjured;

    public string? InjuryDescription => profile.InjuryDescription;

    /// <summary>The scouting sheet: the attributes in three columns and the report above them.</summary>
    public AttributeSheetView Sheet { get; } = new(profile.Attributes, profile.Overall);

    public IReadOnlyList<ContractRow> Contract { get; } = profile.Contract
        .Select(line => new ContractRow(
            LeagueSeasonLabel(line.Season),
            ViewModels.MoneyDisplay.ToMillions(line.Salary),
            line.IsOption ? "Option" : string.Empty))
        .ToList();

    public bool HasContract => profile.Contract.Count > 0;

    public string ContractSummary => profile.Contract.Count == 0
        ? "Not under contract."
        : $"{profile.Contract.Count} season{(profile.Contract.Count == 1 ? string.Empty : "s")} remaining · {ViewModels.MoneyDisplay.ToMillions(profile.Contract.Sum(line => line.Salary))} total";

    public bool HasSeason => profile.CurrentSeason is not null;

    public StatTile[] SeasonTiles { get; } = profile.CurrentSeason is { } season
        ?
        [
            new("GP", season.GamesPlayed.ToString(CultureInfo.InvariantCulture)),
            new("MPG", PerGame(season.Minutes, season.GamesPlayed)),
            new("PPG", PerGame(season.Points, season.GamesPlayed)),
            new("RPG", PerGame(season.Rebounds, season.GamesPlayed)),
            new("APG", PerGame(season.Assists, season.GamesPlayed)),
        ]
        : [];

    public bool HasRecentGames => GameLog.Count > 0;

    public IReadOnlyList<RecentGameRow> RecentGames => GameLog.Take(RecentFormGames).ToList();

    private const int RecentFormGames = 5;

    /// <summary>Every game this season, newest first, each with its match rating — rated once, here.</summary>
    public IReadOnlyList<RecentGameRow> GameLog { get; } = (profile.GameLog ?? profile.RecentGames).Select(RecentGameRow.From).ToList();

    public bool HasGameLog => GameLog.Count > 0;

    /// <summary>This season's shooting by zone against the league's, or null before a shot is taken.</summary>
    public ShotChartView? ShotChart { get; } = ShotChartView.For(profile.SeasonDetail?.Totals.Zones, profile.LeagueZones);

    public bool HasShotChart => ShotChart is not null;

    /// <summary>Where he stands in the league this regular season, stat by stat; empty until he qualifies.</summary>
    public IReadOnlyList<PercentileRow> Percentiles { get; } = PercentileRow.From(profile.Percentiles);

    public bool HasPercentiles => Percentiles.Count > 0;

    /// <summary>Why there are no percentiles yet, in the league's own qualifier.</summary>
    public string NotRankedNote { get; } = profile.LeaderQualifiers is { } qualifiers
        ? $"Not ranked yet: a player needs {DisplayText.Share(qualifiers.MinimumGamesShareBasisPoints)}% of the season's games to be compared with the league."
        : "Not ranked yet: not enough games to compare with the league.";

    /// <summary>The regular season's average match rating, for the header pill.</summary>
    public RatingView? SeasonRating { get; } = RatingView.ForAverage(profile.SeasonDetail?.AverageRating);

    /// <summary>The last ten games played (rated), oldest first: the sparkline and its words.</summary>
    private IReadOnlyList<RatingView> Trend => GameLog.Select(game => game.Rating).OfType<RatingView>().Take(SparklineGames).Reverse().ToList();

    public IReadOnlyList<double> RatingTrend => Trend.Select(rating => rating.Value).ToList();

    public bool HasRatingTrend => Trend.Count > 1;

    /// <summary>The trend in words, so the line is never the only way to read it.</summary>
    public string RatingTrendText => string.Join(" · ", Trend.Select(rating => rating.Text));

    /// <summary>"Last 10 games played · latest 7.0": the caption under the line, so its end is read in words too.</summary>
    public string RatingTrendCaption => Trend is { Count: > 0 } trend ? $"Last {trend.Count} games played · latest {trend[^1].Text}" : string.Empty;

    private const int SparklineGames = 10;

    public bool HasCareer => profile.Career.Count > 0;

    public IReadOnlyList<CareerRow> Career { get; } = profile.Career.Select(CareerRow.From).ToList();

    public IReadOnlyList<StatHeader> CareerHeaders => CareerRow.Headers;

    public bool HasSimulatedStats => profile.SeasonDetail is not null;

    /// <summary>Per game, per 36 minutes, or totals for the simulated season's panels.</summary>
    public StatBasisChoice Basis => _basis ??= Redraws(new StatBasisChoice());

    public string SimulatedSample => profile.SeasonDetail is { } detail ? PlayerStatsSections.SimulatedSample(detail) : string.Empty;

    public IReadOnlyList<StatGroup> SimulatedGroups => profile.SeasonDetail is { } detail
        ? PlayerStatsSections.SimulatedSeason(detail, Basis.Basis)
        : [];

    /// <summary>The latest stated (not simulated) season that carries metrics, e.g. last real season.</summary>
    private static CareerSeasonLine? LastStated(PlayerProfileSummary profile) =>
        profile.Career.LastOrDefault(season => !season.IsCurrent && !season.IsPostseason && season.Metrics is { Count: > 0 });

    public bool HasStatedStats => LastStated(profile) is not null;

    public string StatedSeasonTitle => LastStated(profile) is { } season ? $"{season.Season} — AS STATED BY THE DATA PACK" : string.Empty;

    public IReadOnlyList<StatGroup> StatedGroups { get; } = LastStated(profile) is { } stated
        ? PlayerStatsSections.StatedSeason(stated)
        : [];

    private StatBasisChoice Redraws(StatBasisChoice choice)
    {
        choice.Changed += () => RaisePropertyChanged(nameof(SimulatedGroups));
        return choice;
    }

    // Contract, in depth.
    public bool HasContractDetail => profile.ContractDetail is { Years.Count: > 0 };

    public IReadOnlyList<StatTile> ContractTiles { get; } = profile.ContractDetail is { Years.Count: > 0 } c
        ?
        [
            new("Total value", ViewModels.MoneyDisplay.ToMillions(c.TotalValue)),
            new("Guaranteed", ViewModels.MoneyDisplay.ToMillions(c.GuaranteedValue)),
            new("Average per season", ViewModels.MoneyDisplay.ToMillions(c.AverageAnnualValue)),
            new("Free agent in", c.FreeAgentYear is { } year ? LeagueSeasonLabel(year) : "–"),
        ]
        : [];

    public IReadOnlyList<ContractYearRow> ContractYears { get; } = profile.ContractDetail?.Years
        .Select(ContractYearRow.From).ToList() ?? [];

    /// <summary>Where this season's salary sits between the player's minimum and maximum, 0..1.</summary>
    public double SalaryScalePosition => profile.ContractDetail is { MaximumSalary: { } max, MinimumSalary: { } min, Years.Count: > 0 } c && max > min
        ? Math.Clamp((double)(c.Years[0].Salary - min) / (max - min), 0, 1)
        : 0;

    public double SalaryScaleFill => 360 * SalaryScalePosition;

    public string SalaryScaleLine => profile.ContractDetail switch
    {
        { MaximumSalary: { } max, MinimumSalary: { } min, MaximumPercentOfCap: { } percent, Years.Count: > 0 } c =>
            string.Create(CultureInfo.InvariantCulture, $"Minimum for {profile.SeasonsOfService} seasons of service {ViewModels.MoneyDisplay.ToMillions(min)} · maximum {ViewModels.MoneyDisplay.ToMillions(max)} ({percent}% of the cap) · this season is {100.0 * c.Years[0].Salary / max:0}% of his maximum."),
        _ => "This league states no minimum or maximum salary scale.",
    };

    public string CareerTotalsLine => profile.Career.Count == 0
        ? string.Empty
        : CareerRow.TotalsLine(profile.Career);

    internal static string PerGame(int total, int games) => StatColumns.Fixed(StatFormulas.PerGame(total, games));

    /// <summary>The game log's column headers: the same box-score columns a game sheet shows.</summary>
    public IReadOnlyList<StatHeader> GameLogHeaders { get; } = StatColumns.Headers(StatColumns.BoxScore);

    private int _tabIndex;

    /// <summary>The player's team, so the page can take its colours (spec 01 section 4, page scope).</summary>
    public string? TeamId { get; init; }

    public Application.Leagues.TeamColours? TeamColours { get; init; }

    public string PlayerId => profile.PlayerId;

    public IReadOnlyList<string> Tabs { get; } = ["Overview", "Ratings", "Stats", "Game log", "Contract", "Career"];

    public int TabIndex
    {
        get => _tabIndex;
        set
        {
            if (SetProperty(ref _tabIndex, value))
            {
                RaisePropertyChanged(nameof(IsOverviewTab));
                RaisePropertyChanged(nameof(IsRatingsTab));
                RaisePropertyChanged(nameof(IsStatsTab));
                RaisePropertyChanged(nameof(IsGameLogTab));
                RaisePropertyChanged(nameof(IsContractTab));
                RaisePropertyChanged(nameof(IsCareerTab));
            }
        }
    }

    public IReadOnlyList<string> BasisOptions { get; } = ["Per game", "Per 36", "Totals"];

    /// <summary>0 per game, 1 per 36, 2 totals.</summary>
    public int BasisIndex
    {
        get => Basis.IsPer36 ? 1 : Basis.IsTotals ? 2 : 0;
        set
        {
            if (value == 1)
            {
                Basis.IsPer36 = true;
            }
            else if (value == 2)
            {
                Basis.IsTotals = true;
            }
            else
            {
                Basis.IsPerGame = true;
            }

            RaisePropertyChanged(nameof(BasisIndex));
        }
    }

    public bool IsOverviewTab => _tabIndex == 0;

    public bool IsRatingsTab => _tabIndex == 1;

    public bool IsStatsTab => _tabIndex == 2;

    public bool IsGameLogTab => _tabIndex == 3;

    public bool IsContractTab => _tabIndex == 4;

    public bool IsCareerTab => _tabIndex == 5;

    /// <summary>The hero's identity line: positions, age, experience, team.</summary>
    public string HeroLine => string.Join(
        " · ",
        new[]
        {
            profile.Position,
            string.Create(CultureInfo.InvariantCulture, $"AGE {profile.Age}"),
            profile.Attributes.Body is { } body ? Formatting.Fmt.Height(body.HeightCm) : null,
            profile.Attributes.Body is { } weight ? Formatting.Fmt.Weight(weight.WeightKg).ToUpperInvariant() : null,
            (profile.TeamName ?? "Free agent").ToUpperInvariant(),
        }.OfType<string>());

    /// <summary>The hero's second line: birth date and experience (age is already in the line above).</summary>
    public string HeroSubLine => string.Create(
        CultureInfo.InvariantCulture,
        $"Born {profile.BirthDate:d MMM yyyy} · {profile.SeasonsOfService} season{(profile.SeasonsOfService == 1 ? string.Empty : "s")} of service");

    /// <summary>True for a player on the user's own team: only then does the hero offer actions.</summary>
    public bool IsOwnPlayer { get; init; }

    public System.Windows.Input.ICommand? ExtendCommand { get; init; }

    public System.Windows.Input.ICommand? TradeCommand { get; init; }

    public bool CanExtend => IsOwnPlayer && ExtendCommand is not null && HasContract;

    public IReadOnlyList<Controls.StatusItem> StatusChips => profile.IsInjured
        ? [new(profile.InjuryDescription ?? "Injured", Controls.StatusKind.Negative)]
        : [new("Healthy", Controls.StatusKind.Positive)];

    /// <summary>Every attribute group with its average, for the Ratings tab and the Overview's top groups.</summary>
    public IReadOnlyList<AttributeGroupDisplay> Groups => Sheet.Columns
        .SelectMany(column => column)
        .Select(group => new AttributeGroupDisplay(group.Title, group.Attributes.Count == 0 ? 0 : (int)Math.Round(group.Attributes.Average(cell => cell.Value)), group.Attributes))
        .ToList();

    public IReadOnlyList<AttributeGroupDisplay> OverviewGroups => Groups.Take(4).ToList();

    public IReadOnlyList<Controls.FormResult> FormResults => GameLog.Take(SparklineGames).Select(game => new Controls.FormResult(game.Won, $"{game.Opponent} · {game.Result}")).Reverse().ToList();

    public IReadOnlyList<Controls.TableColumn> GameLogColumns =>
    [
        new("date", "DATE", 96, IsFrozen: true, CanSort: false),
        new("opp", "OPPONENT", 200, IsFrozen: true, CanSort: false),
        new("result", "RESULT", 104, CanSort: false),
        new("rtg", "RTG", 64, IsNumeric: true, CanSort: false, ToolTip: "Match rating out of ten"),
        .. GameLogHeaders.Select((header, index) => new Controls.TableColumn("s" + index.ToString(CultureInfo.InvariantCulture), header.Header, Math.Max(56, header.Width), IsNumeric: true, CanSort: false, ToolTip: header.Tooltip)),
    ];

    public IReadOnlyList<Controls.TableRow> GameLogRows => GameLog
        .Select((game, index) => new Controls.TableRow(
            index.ToString(CultureInfo.InvariantCulture),
            [
                new Controls.TextCell(game.Date),
                new Controls.TextCell(game.Opponent, IsStrong: true),
                new Controls.DeltaCell(game.Result, game.Won, !game.Won),
                new Controls.TextCell(game.Rating?.Text ?? StatColumns.Missing, IsNumeric: true),
                .. game.Stats.Select(stat => (Controls.TableCell)new Controls.TextCell(stat.Value, IsNumeric: true)),
            ]))
        .ToList();

    public IReadOnlyList<Controls.TableColumn> CareerColumns =>
    [
        new("season", "SEASON", 96, IsFrozen: true, CanSort: false),
        new("team", "TEAM", 220, IsFrozen: true, CanSort: false),
        .. CareerHeaders.Select((header, index) => new Controls.TableColumn("c" + index.ToString(CultureInfo.InvariantCulture), header.Header, Math.Max(56, header.Width), IsNumeric: true, CanSort: false, ToolTip: header.Tooltip)),
    ];

    public IReadOnlyList<Controls.TableRow> CareerRows => Career
        .Select((row, index) => new Controls.TableRow(
            index.ToString(CultureInfo.InvariantCulture),
            [new Controls.TextCell(row.Season, IsStrong: row.IsCurrent), new Controls.TextCell(row.Team), .. row.Cells.Select(cell => (Controls.TableCell)new Controls.TextCell(cell.Value, IsNumeric: true))],
            IsHighlighted: row.IsCurrent))
        .ToList();

    public IReadOnlyList<Controls.TableColumn> ContractColumns { get; } =
    [
        new("season", "SEASON", 104, IsFrozen: true, CanSort: false),
        new("salary", "SALARY", 120, IsNumeric: true, CanSort: false),
        new("guaranteed", "GUARANTEED", 140, IsNumeric: true, CanSort: false),
        new("option", "OPTION", 160, CanSort: false),
        new("share", "% OF CAP", 104, IsNumeric: true, CanSort: false),
    ];

    public IReadOnlyList<Controls.TableRow> ContractRows => ContractYears
        .Select(year => new Controls.TableRow(
            year.Season,
            [new Controls.TextCell(year.Season, IsStrong: true), new Controls.TextCell(year.Salary, IsNumeric: true), new Controls.TextCell(year.Guaranteed, IsNumeric: true), new Controls.StatusCell(year.Option == "–" ? string.Empty : year.Option, Controls.StatusKind.Info), new Controls.TextCell(year.ShareOfCap, IsNumeric: true)]))
        .ToList();

    private static string LeagueSeasonLabel(int startYear) =>
        string.Create(CultureInfo.InvariantCulture, $"{startYear}-{(startYear + 1) % 100:00}");
}


public sealed record ContractRow(string Season, string Salary, string Note);

/// <summary>An attribute group as the profile draws it: title, average chip, the bars.</summary>
public sealed record AttributeGroupDisplay(string Title, int Average, IReadOnlyList<AttributeCell> Attributes);

public sealed record StatTile(string Label, string Value);

/// <summary>One game in the log: when, against whom, the result, and the full box-score line.</summary>
public sealed record RecentGameRow(string Date, string Opponent, string Result, bool Won, IReadOnlyList<StatValue> Stats, RatingView? Rating)
{
    public static RecentGameRow From(RecentGameLine game) => new(
        game.Date,
        (game.IsHome ? "vs " : "@ ") + game.Opponent,
        string.Create(CultureInfo.InvariantCulture, $"{(game.Won ? "W" : "L")} {game.TeamPoints}–{game.OpponentPoints}"),
        game.Won,
        StatColumns.Cells(game.Stats, StatColumns.BoxScore),
        RatingView.ForGame(game.Stats));
}

public sealed record ContractYearRow(string Season, string Salary, string Guaranteed, string Option, string ShareOfCap)
{
    public static ContractYearRow From(ContractYearLine year) => new(
        string.Create(CultureInfo.InvariantCulture, $"{year.Season}-{(year.Season + 1) % 100:00}"),
        ViewModels.MoneyDisplay.ToMillions(year.Salary),
        year.Guaranteed == year.Salary ? "Fully" : ViewModels.MoneyDisplay.ToMillions(year.Guaranteed),
        year.Option is null ? "–" : $"{year.Option} option",
        year.ShareOfCap is { } share ? (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "–");
}

/// <summary>
/// One season of a career, as the career table draws it: the same per-game columns the roster shows,
/// then the stated rates a data pack may carry. Counting figures a pack does not state show a dash.
/// </summary>
public sealed record CareerRow(string Season, string Team, bool IsCurrent, IReadOnlyList<StatValue> Cells)
{
    private static readonly StatColumn[] PerGameColumns =
    [
        StatColumns.Games, StatColumns.Minutes, StatColumns.Points, StatColumns.Rebounds, StatColumns.Assists,
        StatColumns.Steals, StatColumns.Blocks, StatColumns.Turnovers, StatColumns.PersonalFouls, StatColumns.PlusMinus,
    ];

    /// <summary>The career table's headers: per-game counting columns, then shooting and the stated rates.</summary>
    public static IReadOnlyList<StatHeader> Headers { get; } =
    [
        .. StatColumns.Headers(PerGameColumns, StatBasis.PerGame),
        StatColumns.FieldGoalPercent.HeaderFor(StatBasis.PerGame),
        StatColumns.ThreePointPercent.HeaderFor(StatBasis.PerGame),
        StatColumns.TrueShooting.HeaderFor(StatBasis.PerGame),
        StatColumns.RateHeader("USG%", "Usage: the share of team plays a player used while on the floor."),
        StatColumns.RateHeader("NETRTG", "Net rating: points scored minus points allowed per 100 possessions while on the floor."),
        StatColumns.RateHeader("PIE", "Player Impact Estimate: a player's share of all game events while on the floor."),
    ];

    public static CareerRow From(CareerSeasonLine line)
    {
        var m = line.Metrics ?? new Dictionary<string, double>();
        double? Metric(string code) => m.TryGetValue(code, out var value) ? value : null;
        double? Ratio(string made, string attempted) => Metric(attempted) is > 0 and var denominator ? Metric(made) / denominator : null;

        // Every metric code carries a season total; a simulated season always states these, a pack may.
        double? PerGame(double? total) => total is { } value ? StatFormulas.PerGame(value, line.GamesPlayed) : null;

        string[] values =
        [
            line.GamesPlayed.ToString(CultureInfo.InvariantCulture),
            StatColumns.Fixed(PerGame(line.Minutes)),
            StatColumns.Fixed(PerGame(line.Points)),
            StatColumns.Fixed(PerGame(line.Rebounds)),
            StatColumns.Fixed(PerGame(line.Assists)),
            StatColumns.Fixed(PerGame(Metric("STL"))),
            StatColumns.Fixed(PerGame(Metric("BLK"))),
            StatColumns.Fixed(PerGame(Metric("TOV"))),
            StatColumns.Fixed(PerGame(Metric("PF"))),
            StatColumns.Signed(PerGame(Metric("PLUS_MINUS"))),
            StatColumns.Percent(Ratio("FGM", "FGA")),
            StatColumns.Percent(Ratio("FG3M", "FG3A")),
            StatColumns.Percent(Metric("TS_PCT")),
            StatColumns.Percent(Metric("USG_PCT")),
            StatColumns.Signed(Metric("NET_RATING")),
            StatColumns.Percent(Metric("PIE")),
        ];

        return new CareerRow(
            line.IsPostseason ? $"{line.Season} playoffs" : line.Season,
            line.TeamName ?? "–",
            line.IsCurrent,
            Headers.Zip(values, (header, value) => header.Cell(value)).ToList());
    }

    /// <summary>Career totals over regular seasons only; playoff rows are listed but never summed in.</summary>
    public static string TotalsLine(IReadOnlyList<CareerSeasonLine> allRows)
    {
        var career = allRows.Where(line => !line.IsPostseason).ToList();
        var games = career.Sum(line => line.GamesPlayed);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Career: {career.Count} season{(career.Count == 1 ? string.Empty : "s")} · {games} games · {PlayerProfileViewModel.PerGame(career.Sum(line => line.Points), games)} PPG · {PlayerProfileViewModel.PerGame(career.Sum(line => line.Rebounds), games)} RPG · {PlayerProfileViewModel.PerGame(career.Sum(line => line.Assists), games)} APG");
    }
}
