using System.Collections.ObjectModel;
using System.Windows.Input;
using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// Stub trade-proposal form. It lets a human pick two teams and assemble two sides from real
/// roster data, then refuses to do anything with it: salary matching, roster constraints, cap and
/// apron restrictions, and atomic execution are Milestone 5. The point of the screen at this
/// milestone is whether assembling a trade in this shape feels right, not whether it is legal.
/// </summary>
public sealed class TradeProposalViewModel : ViewModelBase
{
    private TeamSummary? _sendingTeam;
    private TeamSummary? _receivingTeam;
    private string _status = string.Empty;

    public TradeProposalViewModel(LeagueOverview overview)
    {
        Teams = overview.Teams;
        _sendingTeam = Teams.FirstOrDefault();
        _receivingTeam = Teams.Skip(1).FirstOrDefault();
        SendingSelection = [];
        ReceivingSelection = [];
        ProposeCommand = new RelayCommand(Propose);
    }

    public string Title => "Trade";

    public IReadOnlyList<TeamSummary> Teams { get; }

    public ICommand ProposeCommand { get; }

    public string StubWarning =>
        "STUB — no validation, no execution. Salary matching, roster and apron restrictions, and atomic execution land in Milestone 5.";

    public TeamSummary? SendingTeam
    {
        get => _sendingTeam;
        set
        {
            if (SetProperty(ref _sendingTeam, value))
            {
                SendingSelection.Clear();
                RaisePropertyChanged(nameof(SendingRoster));
            }
        }
    }

    public TeamSummary? ReceivingTeam
    {
        get => _receivingTeam;
        set
        {
            if (SetProperty(ref _receivingTeam, value))
            {
                ReceivingSelection.Clear();
                RaisePropertyChanged(nameof(ReceivingRoster));
            }
        }
    }

    public IReadOnlyList<RosterSpot> SendingRoster => _sendingTeam?.Roster ?? [];

    public IReadOnlyList<RosterSpot> ReceivingRoster => _receivingTeam?.Roster ?? [];

    public ObservableCollection<RosterSpot> SendingSelection { get; }

    public ObservableCollection<RosterSpot> ReceivingSelection { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private void Propose()
    {
        if (_sendingTeam is null || _receivingTeam is null)
        {
            Status = "Pick two teams first.";
            return;
        }

        if (ReferenceEquals(_sendingTeam, _receivingTeam))
        {
            Status = "A team cannot trade with itself.";
            return;
        }

        var outgoing = SendingSelection.Count;
        var incoming = ReceivingSelection.Count;

        Status =
            $"Not submitted. {_sendingTeam.TeamName} would send {outgoing} player(s) and receive {incoming} from " +
            $"{_receivingTeam.TeamName}. The trade engine that validates and executes this arrives in Milestone 5.";
    }
}
