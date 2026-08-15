using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

public sealed class RosterViewModel(LeagueOverview overview) : ViewModelBase
{
    private TeamSummary? _team;

    public string Title => "Roster";

    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (SetProperty(ref _team, value))
            {
                RaisePropertyChanged(nameof(TeamName));
                RaisePropertyChanged(nameof(FranchiseName));
                RaisePropertyChanged(nameof(RosterCountLabel));
                RaisePropertyChanged(nameof(Roster));
            }
        }
    }

    public string TeamName => _team?.TeamName ?? "No team selected";

    public string FranchiseName => _team is null ? string.Empty : $"Franchise: {_team.FranchiseName}";

    public string RosterCountLabel =>
        _team is null
            ? string.Empty
            : $"{_team.RosterCount} under contract — ruleset allows {overview.MinimumRosterPlayers}–{overview.MaximumRosterPlayers}";

    public IReadOnlyList<RosterSpot> Roster => _team?.Roster ?? [];
}
