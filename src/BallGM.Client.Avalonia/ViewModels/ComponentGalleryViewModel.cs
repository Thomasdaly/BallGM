using System.Windows.Input;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.Imaging;
using BallGM.Client.Avalonia.Theming;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>A team the gallery can re-tint to (spec 03 section 2: at least HCM, NGG, SMS, NCV, IRV).</summary>
public sealed record GalleryTeam(string Abbreviation, string Name, string Primary, string Secondary)
{
    public string LogoId => "gallery-team-" + Abbreviation.ToLowerInvariant();
}

/// <summary>
/// The dev-only Component Gallery (S33): every library component in every variant and state, on
/// Surface1 and on a team-gradient hero, with a team switcher that re-tints live. Reached with the
/// <c>--gallery</c> launch argument, never from release navigation. Sample data is fictional.
/// </summary>
public sealed class ComponentGalleryViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> SectionNames = ["Foundations", "Actions", "Navigation", "Inputs", "Data", "Tables", "Media", "Sports", "Feedback"];

    private int _sectionIndex;
    private GalleryTeam _team;

    public ComponentGalleryViewModel()
    {
        Teams =
        [
            new("HCM", "Harbor City Mariners", "#0B1D3A", "#19A7A0"),
            new("NGG", "Northgate Glaciers", "#FFFFFF", "#7FC8F8"),
            new("SMS", "Sun Mesa Scorch", "#FFD100", "#2B2B2B"),
            new("NCV", "Neon Coast Voltage", "#B6FF00", "#000000"),
            new("IRV", "Ironvale Forge", "#000000", "#FF6A13"),
        ];
        _team = Teams[0];
        SelectTeamCommand = new ParameterCommand<GalleryTeam>(team => Team = team);
        UiServices.Assets = new AssetResolver(() => new AssetCatalog(Teams.Select(team => new AssetEntry(new AssetKey(AssetEntityKind.Team, "gallery-team-" + team.Abbreviation.ToLowerInvariant()), team.Abbreviation, null, null, null, team.Primary, team.Secondary))
            .Concat([
                new AssetEntry(new AssetKey(AssetEntityKind.Team, "gallery-team-psa"), "PSA", null, null, null, "#CB1131", "#FFFFFF"),
                new AssetEntry(new AssetKey(AssetEntityKind.Team, "gallery-team-cap"), "CAP", null, null, null, "#5A1022", "#D4AF37"),
                new AssetEntry(new AssetKey(AssetEntityKind.Team, "gallery-team-rvb"), "RVB", null, null, null, "#1F4FD8", "#F68B1F"),
                new AssetEntry(new AssetKey(AssetEntityKind.Team, "gallery-team-mdm"), "MDM", null, null, null, "#4B2991", "#F2C14E"),
                new AssetEntry(new AssetKey(AssetEntityKind.Team, "gallery-team-bay"), "BAY", null, null, null, "#F4F4F4", "#D7263D"),
                new AssetEntry(new AssetKey(AssetEntityKind.Player, "gallery-player-1"), "MO", null, null, null, "#0B1D3A", "#19A7A0"),
                new AssetEntry(new AssetKey(AssetEntityKind.Player, "gallery-player-2"), "DO", null, null, null, "#000000", "#FF6A13", "3"),
            ])));
        ApplyTeam();
    }

    public IReadOnlyList<string> Sections => SectionNames;

    public IReadOnlyList<GalleryTeam> Teams { get; }

    public ICommand SelectTeamCommand { get; }

    public ICommand NoOpCommand { get; } = new RelayCommand(() => { });

    public int SectionIndex
    {
        get => _sectionIndex;
        set
        {
            if (SetProperty(ref _sectionIndex, value))
            {
                RaisePropertyChanged(nameof(Section));
            }
        }
    }

    public string Section => SectionNames[Math.Clamp(_sectionIndex, 0, SectionNames.Count - 1)];

    public GalleryTeam Team
    {
        get => _team;
        set
        {
            if (SetProperty(ref _team, value))
            {
                ApplyTeam();
                RaisePropertyChanged(nameof(Opponent));
                RaisePropertyChanged(nameof(OpponentAccent));
            }
        }
    }

    /// <summary>The other side in head-to-head samples: the first gallery team that is not the one showing.</summary>
    public GalleryTeam Opponent => Teams.First(team => team != _team);

    public global::Avalonia.Media.IBrush OpponentAccent =>
        new global::Avalonia.Media.SolidColorBrush(Theming.TeamTheme.From(Opponent.Primary, Opponent.Secondary).Accent);

    public IReadOnlyList<ContinueOption> ContinueOptions => [new("To next game", NoOpCommand), new("One week", NoOpCommand), new("To the trade deadline", NoOpCommand)];

    public IReadOnlyList<NavEntry> NavEntries =>
    [
        new("home", "Home", "Icon.Home"),
        new("roster", "Roster", "Icon.Roster", 0, "Ctrl+2"),
        new("schedule", "Schedule", "Icon.Calendar", 3),
        new("trades", "Trades", "Icon.Trade"),
    ];

    public IReadOnlyList<BreadcrumbItem> Crumbs => [new("Home", NoOpCommand), new("Roster", NoOpCommand), new("Contracts")];

    public IReadOnlyList<StepItem> Steps => [new(1, "League", StepState.Done), new(2, "Team", StepState.Current), new(3, "Summary", StepState.Upcoming)];

    public IReadOnlyList<string> BasisOptions { get; } = ["Per game", "Per 36", "Totals"];

    public IReadOnlyList<string> TabNames => ["OVERVIEW", "RATINGS", "CONTRACTS", "STATS"];

    public IReadOnlyList<BreadcrumbItem> OverviewCrumbs => [new("Home", NoOpCommand), new("Roster", NoOpCommand), new("Overview")];

    public IReadOnlyList<NavEntry> NavStateEntries =>
    [
        new("home", "Selected", "Icon.Home"),
        new("roster", "Hover", "Icon.Roster"),
        new("schedule", "Pressed", "Icon.Calendar"),
        new("trades", "Focus", "Icon.Trade"),
        new("finances", "Disabled", "Icon.Finances"),
    ];

    public IReadOnlyList<string> SegmentStates => ["Selected", "Hover", "Pressed", "Focus", "Disabled"];

    public IReadOnlyList<StepItem> InvalidSteps => [new(1, "League", StepState.Done), new(2, "Team", StepState.Invalid), new(3, "Summary", StepState.Upcoming)];

    public IReadOnlyList<string> Segments => ["Per game", "Totals", "Per 36"];

    public IReadOnlyList<string> Positions => ["PG", "SG", "SF", "PF", "C"];

    public IReadOnlyList<KeyValueItem> BioFacts =>
    [
        new("Height", "6'8\""), new("Weight", "220 lb"), new("Age", "27"), new("Experience", "6th season"),
        new("Born", "5 Apr 2013"), new("Contract", "$31.2M x 3"),
    ];

    public PlayerCardModel StarCard => new(
        "gallery-player-1", "Maximilian Okonkwo-Vanderbilt", "gallery-team-hcm", "Harbor City Mariners", "SF/PF", 27, 93, 95,
        "$31.2M · 3 YRS · PLAYER OPTION", [new("Healthy", StatusKind.Positive), new("Extension eligible", StatusKind.Info)], Number: "7");

    public PlayerCardModel InjuredCard => new(
        "gallery-player-2", "D'Shawn O'Malley-Fitzgerald", "gallery-team-irv", "Ironvale Forge", "PG", 24, 82, 90,
        "$9.8M · 2 YRS · ROOKIE SCALE", [new("Out · 2–3 weeks", StatusKind.Negative), new("Rookie scale", StatusKind.Neutral)], IsInjured: true, Number: "3");

    public TeamCardModel TeamCardSample => new("gallery-team-hcm", "Harbor City Mariners", "24–11", "2nd East", "$179.7M payroll");

    public TeamCardModel LongTeamCard => new("gallery-team-psa", "Port Saint Augustine Navigators", "19–16", "6th West", "$141.0M payroll");

    public IReadOnlyList<double> FormValues => [102, 96, 111, 99, 118, 104, 92, 121, 108, 115];

    public IReadOnlyList<FormResult> FormResults => [new(true), new(true), new(false), new(true), new(true), new(false), new(true), new(true), new(true), new(false)];

    public IReadOnlyList<CapSegment> CapSegments => [new("Guaranteed", 128_400_000), new("Options", 9_200_000), new("Holds", 3_600_000)];

    public IReadOnlyList<CapMarker> CapMarkers => [Marker("CAP", 141_000_000), Marker("TAX", 172_000_000), Marker("APRON", 179_000_000)];

    private static CapMarker Marker(string name, long amount) => new($"{name} {Formatting.Fmt.Money(amount)}", amount);

    public IReadOnlyList<CapSegment> CapOverSegments => [new("Guaranteed", 168_000_000), new("Options", 11_700_000)];

    public IReadOnlyList<string> PeriodLabels => ["1", "2", "3", "4"];

    public IReadOnlyList<LinescoreRow> LinescoreRows =>
    [
        new("gallery-team-hcm", "HCM", [31, 22, 19, 26], 98),
        new("gallery-team-irv", "IRV", [24, 25, 23, 29], 101, IsWinner: true),
    ];

    public ScoreBugSide BugAway => new("gallery-team-hcm", "HCM", "Harbor City Mariners", 98);

    public ScoreBugSide BugHome => new("gallery-team-irv", "IRV", "Ironvale Forge", 101, IsWinner: true);

    public IReadOnlyList<TickerItem> TickerItems =>
    [
        new("HCM 98 – 101 IRV", "FINAL"), new("NCV 77 – 80 SMS", "FINAL"), new("Glaciers win ninth straight", "NEWS"),
        new("BAY @ PKF 7:30 PM", "TONIGHT"), new("Forge guard out 2-3 weeks (ankle)", "NEWS"),
    ];

    public IReadOnlyList<BracketRound> BracketRounds =>
    [
        new("First round", [new(1, "gallery-team-ngg", "Northgate Glaciers", 4, 8, "gallery-team-cap", "Capital Senators", 1), new(4, "gallery-team-rvb", "Riverbend Otters", 3, 5, "gallery-team-mdm", "Midland Monarchs", 4), new(2, "gallery-team-hcm", "Harbor City Mariners", 4, 7, "gallery-team-irv", "Ironvale Forge", 2, IsUserSeries: true)]),
        new("Semifinals", [new(1, "gallery-team-ngg", "Northgate Glaciers", 2, 5, "gallery-team-mdm", "Midland Monarchs", 1), new(2, "gallery-team-hcm", "Harbor City Mariners", 3, 3, "gallery-team-bay", "Bayline Gulls", 3, IsUserSeries: true)]),
    ];

    public IReadOnlyList<CalendarDay> CalendarDays =>
        Enumerable.Range(0, 35).Select(index =>
        {
            var day = index - 2;
            var inMonth = day is >= 1 and <= 31;
            CalendarGame? game = inMonth && day % 2 == 0
                ? new CalendarGame($"g{day}", day % 4 == 0 ? "gallery-team-irv" : "gallery-team-ncv", day % 4 == 0 ? "IRV" : "NCV", day % 4 == 0, day < 14 ? (day % 6 == 0 ? "98-101" : "112-104") : null, day < 14 ? day % 6 != 0 : null, day == 10)
                : null;
            return new CalendarDay(inMonth ? day : (day < 1 ? 30 + day : day - 31), inMonth, day == 14, game);
        }).ToList();

    public IReadOnlyList<ShotZoneCell> ShotZonesSample =>
    [
        new(BallGM.Domain.Seasons.ShotZone.Rim, "At the rim", "68.2% · 120/176", "+4.1 pts", "64.1%", 1),
        new(BallGM.Domain.Seasons.ShotZone.Paint, "In the paint", "41.0% · 41/100", "-1.2 pts", "42.2%", 0),
        new(BallGM.Domain.Seasons.ShotZone.MidRange, "Mid-range", "36.5% · 46/126", "-5.9 pts", "42.4%", -1),
        new(BallGM.Domain.Seasons.ShotZone.Corner3, "Corner three", "44.4% · 24/54", "+5.8 pts", "38.6%", 2),
        new(BallGM.Domain.Seasons.ShotZone.AboveBreak3, "Above-the-break three", "35.1% · 66/188", "-0.2 pts", "35.3%", 0),
    ];

    public IReadOnlyList<TimelineEntry> TimelineEntries =>
    [
        new("14 Jan 2041", "Signed a 3-year extension", "$31.2M a season, player option in the final year"),
        new("2 Jul 2039", "Acquired from Ironvale Forge", "With a 2041 second-round pick"),
        new("26 Jun 2034", "Drafted 4th overall"),
    ];

    public IReadOnlyList<StepItem> WizardSteps => Steps;

    public ToastMessage SuccessToast { get; } = new(ToastKind.Success, "Contract signed", "Kovac agreed to 2 years, $18.2M a season.");

    public ToastMessage WarningToast { get; } = new(ToastKind.Warning, "Roster below the minimum", "Sign a player before the next game.");

    public ToastMessage ErrorToast { get; } = new(ToastKind.Error, "Save failed", "The saves folder could not be written.", "Retry", new RelayCommand(() => { }));

    public ToastMessage InfoToast { get; } = new(ToastKind.Info, "Trade deadline in 3 days");

    public IReadOnlyList<ToastMessage> Toasts => [SuccessToast, WarningToast, ErrorToast, InfoToast];

    public DialogRequest ConfirmDialog { get; } = new("Release player?", "Releasing Jonah Kovac leaves $18.2M of dead money on this season's cap.", "Release", "Cancel", isDestructive: true);

    public DialogRequest PlainDialog { get; } = new("Start the season?", "The calendar and fixture list are built; nothing else changes.", "Start season", "Not yet");

    public DialogRequest FormDialog { get; } = new("Name this save", null, "Save", "Cancel", content: new global::Avalonia.Controls.TextBox { Text = "Mariners - season 2040", Width = 360, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left });

    public IReadOnlyList<TableColumn> TableColumns { get; } =
    [
        new("player", "PLAYER", 260, IsFrozen: true, CanSort: true),
        new("pos", "POS", 64),
        new("age", "AGE", 56, IsNumeric: true),
        new("ovr", "OVR", 64, IsNumeric: true),
        new("pot", "POT", 64, IsNumeric: true),
        new("team", "TEAM", 220, Priority: 1),
        new("pts", "PTS", 64, IsNumeric: true),
        new("reb", "REB", 64, IsNumeric: true, Priority: 2),
        new("ast", "AST", 64, IsNumeric: true, Priority: 2),
        new("pm", "+/-", 72, IsNumeric: true, Priority: 2),
        new("salary", "SALARY", 96, IsNumeric: true),
        new("status", "STATUS", 180),
    ];

    private string _tableSort = "ovr";
    private bool _tableDescending = true;

    public string TableSort
    {
        get => _tableSort;
        set => SetProperty(ref _tableSort, value);
    }

    public bool TableDescending
    {
        get => _tableDescending;
        set => SetProperty(ref _tableDescending, value);
    }

    public ICommand TableSortCommand => new ParameterCommand<string>(key =>
    {
        TableDescending = key == TableSort ? !TableDescending : true;
        TableSort = key;
        RaisePropertyChanged(nameof(TableRows));
    });

    /// <summary>Three rows for the table drawn on the team hero.</summary>
    public IReadOnlyList<TableRow> HeroRows => TableRows.Take(3).ToList();

    public IReadOnlyList<TableRow> TableRows
    {
        get
        {
            var rows = SampleRows(40);
            var index = TableColumns.ToList().FindIndex(column => column.Key == TableSort);
            var ordered = rows.OrderBy(row => row.Cells[index] is RatingCell rating ? rating.Value : double.TryParse(row.Cells[index].Text.TrimStart('$', '+').TrimEnd('M'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0);
            return (TableDescending ? ordered.Reverse() : ordered).ToList();
        }
    }

    public IReadOnlyList<TableRow> NoRows { get; } = [];

    /// <summary>Generated sample rows (fictional), also used by the 1,000-row virtualization test.</summary>
    public static IReadOnlyList<TableRow> SampleRows(int count)
    {
        string[] names = ["Maximilian Okonkwo-Vanderbilt", "D'Shawn O'Malley-Fitzgerald", "Jean-Baptiste Kouyaté-Lindqvist", "Tyrese-Montgomery Abernathy-Clarke", "Đorđe Vukčević-Ilunga", "Nguyễn Thành Long", "Kaleo", "Gideon Whitcombe"];
        string[] teams = ["Harbor City Mariners", "Ironvale Forge", "Port Saint Augustine Navigators", "Sun Mesa Scorch"];
        string[] positions = ["PG", "SG", "SF", "PF", "C"];
        return Enumerable.Range(0, count).Select(index =>
        {
            var overall = 95 - (index * 37 % 50);
            var injured = index % 9 == 4;
            return new TableRow(
                "row-" + index,
                [
                    new PlayerCell(names[index % names.Length] + (index >= names.Length ? $" {index / names.Length + 1}" : string.Empty), "gallery-player-" + (1 + (index % 2)), injured),
                    new PositionCell(positions[index % 5]),
                    new TextCell((20 + (index % 17)).ToString(System.Globalization.CultureInfo.InvariantCulture), IsNumeric: true),
                    new RatingCell(overall),
                    new RatingCell(Math.Min(99, overall + (index % 7))),
                    new TeamCell(teams[index % teams.Length], "gallery-team-" + (index % 4 == 0 ? "hcm" : index % 4 == 1 ? "irv" : index % 4 == 2 ? "psa" : "sms")),
                    new TextCell((8 + (overall - 40) * 0.3).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), IsNumeric: true),
                    new TextCell((2 + (index % 9) * 0.9).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), IsNumeric: true),
                    new TextCell((1 + (index % 6) * 1.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), IsNumeric: true),
                    new DeltaCell(((index % 11) - 5 > 0 ? "+" : string.Empty) + ((index % 11) - 5).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), (index % 11) > 5, (index % 11) < 5),
                    new TextCell("$" + (1.2 + (overall - 40) * 0.6).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "M", IsNumeric: true),
                    new StatusCell(injured ? "Out · 2-3 weeks" : index % 5 == 2 ? "Expiring" : string.Empty, injured ? StatusKind.Negative : StatusKind.Warning),
                ],
                IsHighlighted: index == 3);
        }).ToList();
    }

    private void ApplyTeam() => ThemeResources.ApplyUserTeam(TeamTheme.From(_team.Primary, _team.Secondary));
}
