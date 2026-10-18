using System.Windows.Input;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.Imaging;
using BallGM.Client.Avalonia.Theming;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>A team the gallery can re-tint to (spec 03 section 2: at least HCM, NGG, SMS, NCV, IRV).</summary>
public sealed record GalleryTeam(string Abbreviation, string Name, string Primary, string Secondary);

/// <summary>
/// The dev-only Component Gallery (S33): every library component in every variant and state, on
/// Surface1 and on a team-gradient hero, with a team switcher that re-tints live. Reached with the
/// <c>--gallery</c> launch argument, never from release navigation. Sample data is fictional.
/// </summary>
public sealed class ComponentGalleryViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> SectionNames = ["Foundations", "Actions", "Navigation", "Inputs", "Data", "Media", "Sports", "Feedback"];

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
            }
        }
    }

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

    public IReadOnlyList<string> TabNames => ["OVERVIEW", "RATINGS", "CONTRACTS", "STATS"];

    public IReadOnlyList<string> Segments => ["Per game", "Totals", "Per 36"];

    public IReadOnlyList<string> Positions => ["PG", "SG", "SF", "PF", "C"];

    public IReadOnlyList<KeyValueItem> BioFacts =>
    [
        new("Height", "6'8\""), new("Weight", "220 lb"), new("Age", "27"), new("Experience", "6th season"),
        new("Born", "5 Apr 2013"), new("Contract", "$31.2M x 3"),
    ];

    public PlayerCardModel StarCard => new(
        "gallery-player-1", "Maximilian Okonkwo-Vanderbilt", "gallery-team-hcm", "Harbor City Mariners", "SF/PF", 27, 93, 95,
        "$31.2M · 3 YRS · PLAYER OPTION", [new("Healthy", StatusKind.Positive), new("Extension eligible", StatusKind.Info)]);

    public PlayerCardModel InjuredCard => new(
        "gallery-player-2", "D'Shawn O'Malley-Fitzgerald", "gallery-team-irv", "Ironvale Forge", "PG", 24, 82, 90,
        "$9.8M · 2 YRS · ROOKIE SCALE", [new("Out · 2-3 weeks", StatusKind.Negative), new("Rookie scale", StatusKind.Neutral)], IsInjured: true);

    public TeamCardModel TeamCardSample => new("gallery-team-hcm", "Harbor City Mariners", "24–11", "2nd East", "$179.7M payroll");

    public TeamCardModel LongTeamCard => new("gallery-team-psa", "Port Saint Augustine Navigators", "19–16", "6th West", "$141.0M payroll");

    public IReadOnlyList<double> FormValues => [102, 96, 111, 99, 118, 104, 92, 121, 108, 115];

    public IReadOnlyList<FormResult> FormResults => [new(true), new(true), new(false), new(true), new(true), new(false), new(true), new(true), new(true), new(false)];

    public IReadOnlyList<CapSegment> CapSegments => [new("Guaranteed", 128_400_000), new("Options", 9_200_000), new("Holds", 3_600_000)];

    public IReadOnlyList<CapMarker> CapMarkers => [new("CAP $141M", 141_000_000), new("TAX $172M", 172_000_000), new("APRON $179M", 179_000_000)];

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
                ? new CalendarGame($"g{day}", "gallery-team-irv", day % 4 == 0 ? "IRV" : "NCV", day % 4 == 0, day < 14 ? (day % 6 == 0 ? "98-101" : "112-104") : null, day < 14 ? day % 6 != 0 : null, day == 10)
                : null;
            return new CalendarDay(inMonth ? day : (day < 1 ? 30 + day : day - 31), inMonth, day == 14, game);
        }).ToList();

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

    private void ApplyTeam() => ThemeResources.ApplyUserTeam(TeamTheme.From(_team.Primary, _team.Secondary));
}
