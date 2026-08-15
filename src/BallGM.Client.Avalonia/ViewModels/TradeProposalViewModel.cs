using System.Collections.ObjectModel;
using System.Windows.Input;
using BallGM.Application.Leagues;
using BallGM.Application.Trades;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The trade machine. Assembles two sides from real rosters and real pick assets, asks the rules
/// what they think as often as a GM likes, and — only when asked twice — executes.
/// <para>
/// Every verdict on this screen comes from the rules layer through <see cref="LeagueSession"/>. The
/// view model decides nothing about legality; it formats money, arranges rows, and keeps the
/// assessment it was given, which is why a rejection here reads the same as a rejection anywhere
/// else in the game.
/// </para>
/// </summary>
public sealed class TradeProposalViewModel : ViewModelBase
{
    private readonly LeagueSession _session;
    private readonly Action<LeagueOverview> _onLeagueChanged;

    private LeagueOverview _overview;
    private TeamSummary? _sendingTeam;
    private TeamSummary? _receivingTeam;
    private string _status = "Pick players or picks from each side, then check the trade.";
    private bool _hasAssessment;
    private bool _isLegal;
    private IReadOnlyList<TradeFindingRow> _violations = [];
    private IReadOnlyList<TradeFindingRow> _warnings = [];
    private IReadOnlyList<TradeOutcomeRow> _outcomes = [];

    public TradeProposalViewModel(
        LeagueOverview overview,
        LeagueSession session,
        Action<LeagueOverview> onLeagueChanged)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onLeagueChanged);

        _overview = overview;
        _session = session;
        _onLeagueChanged = onLeagueChanged;

        Teams = overview.Teams;
        _sendingTeam = Teams.FirstOrDefault();
        _receivingTeam = Teams.Skip(1).FirstOrDefault();

        SendingPlayers = [];
        ReceivingPlayers = [];
        SendingPicks = [];
        ReceivingPicks = [];

        CheckCommand = new RelayCommand(Check);
        SubmitCommand = new RelayCommand(Submit);
        ClearCommand = new RelayCommand(ClearSelections);
    }

    public string Title => "Trade";

    /// <summary>
    /// The league's teams as of the last refresh. Replaced after a trade rather than held from
    /// construction: the summaries carry rosters and cap sheets, and a trade makes the old ones lies.
    /// </summary>
    public IReadOnlyList<TeamSummary> Teams { get; private set; }

    public ICommand CheckCommand { get; }

    public ICommand SubmitCommand { get; }

    public ICommand ClearCommand { get; }

    public TeamSummary? SendingTeam
    {
        get => _sendingTeam;
        set
        {
            if (SetProperty(ref _sendingTeam, value))
            {
                ResetSide(sending: true);
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
                ResetSide(sending: false);
            }
        }
    }

    public IReadOnlyList<TradePlayerRow> SendingRoster => RosterFor(_sendingTeam);

    public IReadOnlyList<TradePlayerRow> ReceivingRoster => RosterFor(_receivingTeam);

    public IReadOnlyList<TradePickRow> SendingPickAssets => PickAssetsFor(_sendingTeam);

    public IReadOnlyList<TradePickRow> ReceivingPickAssets => PickAssetsFor(_receivingTeam);

    public ObservableCollection<TradePlayerRow> SendingPlayers { get; }

    public ObservableCollection<TradePlayerRow> ReceivingPlayers { get; }

    public ObservableCollection<TradePickRow> SendingPicks { get; }

    public ObservableCollection<TradePickRow> ReceivingPicks { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool HasAssessment
    {
        get => _hasAssessment;
        private set => SetProperty(ref _hasAssessment, value);
    }

    /// <summary>Whether the last assessment passed. Drives the submit button, so nothing illegal can be sent.</summary>
    public bool IsLegal
    {
        get => _isLegal;
        private set
        {
            if (SetProperty(ref _isLegal, value))
            {
                RaisePropertyChanged(nameof(Verdict));
            }
        }
    }

    public string Verdict => !HasAssessment
        ? string.Empty
        : IsLegal
            ? "Legal under this league's rules."
            : "Rejected. Nothing has been changed.";

    public IReadOnlyList<TradeFindingRow> Violations
    {
        get => _violations;
        private set
        {
            if (SetProperty(ref _violations, value))
            {
                RaisePropertyChanged(nameof(HasViolations));
            }
        }
    }

    public IReadOnlyList<TradeFindingRow> Warnings
    {
        get => _warnings;
        private set
        {
            if (SetProperty(ref _warnings, value))
            {
                RaisePropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public IReadOnlyList<TradeOutcomeRow> Outcomes
    {
        get => _outcomes;
        private set => SetProperty(ref _outcomes, value);
    }

    public bool HasViolations => Violations.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    private void Check()
    {
        var request = BuildRequest();
        if (request is null)
        {
            return;
        }

        var result = _session.AssessTrade(request);
        if (result.IsFailure)
        {
            ShowFailure(result.Errors.Select(error => new TradeFindingRow(error.Code, error.Message, null)).ToList());
            Status = "The proposal could not be assessed.";
            return;
        }

        Apply(result.Value);
        Status = IsLegal
            ? "This trade would go through. Submit it to execute."
            : "This trade would be rejected. The reasons are below.";
    }

    /// <summary>
    /// Executes. The engine re-validates against the league as it stands, so a proposal that passed a
    /// check a moment ago can still be refused here — and that refusal is the system working.
    /// </summary>
    private void Submit()
    {
        var request = BuildRequest();
        if (request is null)
        {
            return;
        }

        var result = _session.SubmitTrade(request);
        if (result.IsFailure)
        {
            ShowFailure(result.Errors.Select(error => new TradeFindingRow(error.Code, error.Message, null)).ToList());
            Status = "The trade was not executed and nothing was changed.";
            return;
        }

        var submission = result.Value;
        Apply(submission.Assessment);

        var moved = SendingPlayers.Count + ReceivingPlayers.Count + SendingPicks.Count + ReceivingPicks.Count;
        Status = $"Trade executed: {moved} assets moved, {submission.LedgerEntryCount} ledger entries recorded. " +
                 "The rosters, cap sheets, and pick board now show the result.";

        ClearSelections();
        RefreshFrom(submission.Overview);
        _onLeagueChanged(submission.Overview);
    }

    /// <summary>
    /// Rebinds the screen to a league that has moved. Both sides are re-resolved by identifier, so
    /// the form keeps the teams a GM was working with instead of snapping back to the first two.
    /// </summary>
    private void RefreshFrom(LeagueOverview overview)
    {
        _overview = overview;
        Teams = overview.Teams;
        RaisePropertyChanged(nameof(Teams));

        _sendingTeam = Teams.FirstOrDefault(team => team.TeamId == _sendingTeam?.TeamId);
        _receivingTeam = Teams.FirstOrDefault(team => team.TeamId == _receivingTeam?.TeamId);

        RaisePropertyChanged(nameof(SendingTeam));
        RaisePropertyChanged(nameof(ReceivingTeam));
        RaisePropertyChanged(nameof(SendingRoster));
        RaisePropertyChanged(nameof(ReceivingRoster));
        RaisePropertyChanged(nameof(SendingPickAssets));
        RaisePropertyChanged(nameof(ReceivingPickAssets));
    }

    private TradeRequest? BuildRequest()
    {
        if (_sendingTeam is null || _receivingTeam is null)
        {
            Status = "Pick two teams first.";
            return null;
        }

        if (_sendingTeam.TeamId == _receivingTeam.TeamId)
        {
            Status = "A team cannot trade with itself.";
            return null;
        }

        var assets = new List<TradeAssetRequest>();

        foreach (var player in SendingPlayers)
        {
            assets.Add(new TradeAssetRequest(
                TradeAssetRequest.PlayerKind, player.Spot.PlayerId, _sendingTeam.TeamId, _receivingTeam.TeamId));
        }

        foreach (var player in ReceivingPlayers)
        {
            assets.Add(new TradeAssetRequest(
                TradeAssetRequest.PlayerKind, player.Spot.PlayerId, _receivingTeam.TeamId, _sendingTeam.TeamId));
        }

        foreach (var pick in SendingPicks)
        {
            assets.Add(new TradeAssetRequest(
                TradeAssetRequest.PickKind, pick.PickId, _sendingTeam.TeamId, _receivingTeam.TeamId));
        }

        foreach (var pick in ReceivingPicks)
        {
            assets.Add(new TradeAssetRequest(
                TradeAssetRequest.PickKind, pick.PickId, _receivingTeam.TeamId, _sendingTeam.TeamId));
        }

        if (assets.Count == 0)
        {
            Status = "Select at least one player or pick to trade.";
            return null;
        }

        return new TradeRequest([_sendingTeam.TeamId, _receivingTeam.TeamId], assets);
    }

    private static IReadOnlyList<TradePlayerRow> RosterFor(TeamSummary? team) =>
        team is null ? [] : team.Roster.Select(TradePlayerRow.From).ToList();

    /// <summary>
    /// The picks a team's franchise controls today. Assets it is merely owed do not appear: a pick
    /// that has not conveyed is not the franchise's to trade, and the pick board already says so.
    /// </summary>
    private IReadOnlyList<TradePickRow> PickAssetsFor(TeamSummary? team)
    {
        if (team is null)
        {
            return [];
        }

        return _overview.PickBoard.Franchises
            .Where(row => string.Equals(row.FranchiseName, team.FranchiseName, StringComparison.Ordinal))
            .SelectMany(row => row.Drafts)
            .SelectMany(cell => cell.Assets
                .Where(asset => asset.State is "Own" or "Acquired")
                .Select(asset => TradePickRow.From(cell.DraftSeason, asset)))
            .ToList();
    }

    private void Apply(TradeAssessmentSummary assessment)
    {
        Violations = assessment.Violations.Select(TradeFindingRow.From).ToList();
        Warnings = assessment.Warnings.Select(TradeFindingRow.From).ToList();
        Outcomes = assessment.Teams.Select(TradeOutcomeRow.From).ToList();
        IsLegal = assessment.IsLegal;
        HasAssessment = true;
        RaisePropertyChanged(nameof(Verdict));
    }

    private void ShowFailure(IReadOnlyList<TradeFindingRow> findings)
    {
        Violations = findings;
        Warnings = [];
        Outcomes = [];
        IsLegal = false;
        HasAssessment = true;
        RaisePropertyChanged(nameof(Verdict));
    }

    private void ResetSide(bool sending)
    {
        if (sending)
        {
            SendingPlayers.Clear();
            SendingPicks.Clear();
            RaisePropertyChanged(nameof(SendingRoster));
            RaisePropertyChanged(nameof(SendingPickAssets));
        }
        else
        {
            ReceivingPlayers.Clear();
            ReceivingPicks.Clear();
            RaisePropertyChanged(nameof(ReceivingRoster));
            RaisePropertyChanged(nameof(ReceivingPickAssets));
        }

        HasAssessment = false;
    }

    private void ClearSelections()
    {
        SendingPlayers.Clear();
        ReceivingPlayers.Clear();
        SendingPicks.Clear();
        ReceivingPicks.Clear();
        HasAssessment = false;
        RaisePropertyChanged(nameof(SendingPickAssets));
        RaisePropertyChanged(nameof(ReceivingPickAssets));
    }
}
