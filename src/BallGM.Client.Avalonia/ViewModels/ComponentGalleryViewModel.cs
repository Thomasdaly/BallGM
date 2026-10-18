using System.Windows.Input;
using BallGM.Client.Avalonia.Controls;
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

    private void ApplyTeam() => ThemeResources.ApplyUserTeam(TeamTheme.From(_team.Primary, _team.Secondary));
}
