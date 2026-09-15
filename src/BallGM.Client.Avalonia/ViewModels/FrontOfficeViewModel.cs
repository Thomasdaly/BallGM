using System.Windows.Input;
using BallGM.Application.AI;
using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The Milestone 9 diagnostics screen: what a team's own front office would read, through
/// <see cref="LeagueSession.FrontOfficeAdvisory"/> — and, since the AI-turn-execution slice, what it
/// actually does when asked to, through <see cref="LeagueSession.RunAiFrontOfficeTurn"/>.
/// <para>
/// "Run AI turn" acts for the one team this screen is showing, exactly the same way a human's own
/// trade or free-agency screen would: it hands the front office's own first legal candidate to the
/// real <c>ITradeEngine</c>/<c>ISigningEngine</c>, re-validated at that moment, nothing staged or
/// second-guessed. It is still not autonomous — nothing runs this without a GM pressing the button —
/// and every other screen keeps composing and formatting the four AI models' own reads the same way
/// it always has.
/// </para>
/// </summary>
public sealed class FrontOfficeViewModel : ViewModelBase
{
    private readonly LeagueSession _session;
    private readonly Action<LeagueOverview> _onLeagueChanged;

    private TeamSummary? _team;
    private string _status = string.Empty;
    private string? _turnResultLine;
    private IReadOnlyList<AIFindingRow> _directionFactors = [];
    private string _directionLine = string.Empty;
    private IReadOnlyList<PositionalNeedRow> _positionalNeeds = [];
    private IReadOnlyList<AIFindingRow> _needsNotes = [];
    private IReadOnlyList<TradeTargetRow> _tradeTargets = [];
    private IReadOnlyList<FreeAgentTargetRow> _freeAgentTargets = [];
    private DraftPreviewRow? _draftPreview;
    private string? _draftPreviewNote;

    public FrontOfficeViewModel(LeagueSession session, Action<LeagueOverview> onLeagueChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onLeagueChanged);
        _session = session;
        _onLeagueChanged = onLeagueChanged;

        RunAiTurnCommand = new RelayCommand(RunAiTurn);
    }

    public ICommand RunAiTurnCommand { get; }

    public string Title => "Front office";

    /// <summary>The team the screen is read for. Set by the shell, like every other team-scoped screen.</summary>
    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (SetProperty(ref _team, value))
            {
                // A turn result belongs to whichever team it was run for — showing it under a
                // different team the GM has since switched to would misattribute what happened.
                TurnResultLine = null;
                Refresh();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>What the last "run AI turn" on this screen actually did, or <c>null</c> before one has run.</summary>
    public string? TurnResultLine
    {
        get => _turnResultLine;
        private set
        {
            if (SetProperty(ref _turnResultLine, value))
            {
                RaisePropertyChanged(nameof(HasTurnResult));
            }
        }
    }

    public bool HasTurnResult => TurnResultLine is not null;

    public string DirectionLine
    {
        get => _directionLine;
        private set => SetProperty(ref _directionLine, value);
    }

    public IReadOnlyList<AIFindingRow> DirectionFactors
    {
        get => _directionFactors;
        private set => SetProperty(ref _directionFactors, value);
    }

    public IReadOnlyList<PositionalNeedRow> PositionalNeeds
    {
        get => _positionalNeeds;
        private set => SetProperty(ref _positionalNeeds, value);
    }

    public IReadOnlyList<AIFindingRow> NeedsNotes
    {
        get => _needsNotes;
        private set
        {
            if (SetProperty(ref _needsNotes, value))
            {
                RaisePropertyChanged(nameof(HasNeedsNotes));
            }
        }
    }

    public bool HasNeedsNotes => NeedsNotes.Count > 0;

    public IReadOnlyList<TradeTargetRow> TradeTargets
    {
        get => _tradeTargets;
        private set
        {
            if (SetProperty(ref _tradeTargets, value))
            {
                RaisePropertyChanged(nameof(HasTradeTargets));
            }
        }
    }

    public bool HasTradeTargets => TradeTargets.Count > 0;

    public IReadOnlyList<FreeAgentTargetRow> FreeAgentTargets
    {
        get => _freeAgentTargets;
        private set
        {
            if (SetProperty(ref _freeAgentTargets, value))
            {
                RaisePropertyChanged(nameof(HasFreeAgentTargets));
            }
        }
    }

    public bool HasFreeAgentTargets => FreeAgentTargets.Count > 0;

    public DraftPreviewRow? DraftPreview
    {
        get => _draftPreview;
        private set
        {
            if (SetProperty(ref _draftPreview, value))
            {
                RaisePropertyChanged(nameof(HasDraftPreview));
            }
        }
    }

    public bool HasDraftPreview => DraftPreview is not null;

    public string? DraftPreviewNote
    {
        get => _draftPreviewNote;
        private set => SetProperty(ref _draftPreviewNote, value);
    }

    /// <summary>Rebuilds every section against the league as it stands, for the currently selected team.</summary>
    public void Refresh()
    {
        if (_team is null)
        {
            Status = "No team selected.";
            return;
        }

        var result = _session.FrontOfficeAdvisory(_team.TeamId);
        if (result.IsFailure)
        {
            Status = string.Join(" ", result.Errors.Select(error => $"{error.Code}: {error.Message}"));
            DirectionLine = string.Empty;
            DirectionFactors = [];
            PositionalNeeds = [];
            NeedsNotes = [];
            TradeTargets = [];
            FreeAgentTargets = [];
            DraftPreview = null;
            DraftPreviewNote = null;
            return;
        }

        var advisory = result.Value;

        Status = $"Read for {advisory.TeamName}.";
        DirectionLine = advisory.Direction.Direction;
        DirectionFactors = advisory.Direction.Factors.Select(AIFindingRow.From).ToList();

        PositionalNeeds = advisory.Needs.PositionalNeeds.Select(PositionalNeedRow.From).ToList();
        NeedsNotes = advisory.Needs.Notes.Select(AIFindingRow.From).ToList();

        TradeTargets = advisory.TradeTargets.Select(target => TradeTargetRow.From(target, advisory.TeamId)).ToList();
        FreeAgentTargets = advisory.FreeAgentTargets.Select(FreeAgentTargetRow.From).ToList();

        DraftPreview = advisory.DraftPreview is { } preview ? DraftPreviewRow.From(preview) : null;
        DraftPreviewNote = advisory.DraftPreviewNote;
    }

    /// <summary>
    /// Runs one AI turn for the team this screen is showing: the front office's own first legal
    /// trade, else its first legal free-agent offer, else nothing — see
    /// <see cref="LeagueSession.RunAiFrontOfficeTurn"/> for the full rule. Re-reads and re-broadcasts
    /// the league afterward the same way <see cref="TradeProposalViewModel.Submit"/> does, because an
    /// executed action makes every other screen's cached read stale.
    /// </summary>
    private void RunAiTurn()
    {
        if (_team is null)
        {
            return;
        }

        var result = _session.RunAiFrontOfficeTurn([_team.TeamId]);
        if (result.IsFailure)
        {
            TurnResultLine = string.Join(" ", result.Errors.Select(error => $"{error.Code}: {error.Message}"));
            return;
        }

        var outcome = result.Value.Outcomes[0];
        TurnResultLine = outcome.Action switch
        {
            AiTurnAction.TradeExecuted =>
                $"Trade executed: sent {outcome.Trade!.OutgoingPlayerName} to {outcome.Trade.CounterpartyTeamName} for {outcome.Trade.IncomingPlayerName}.",
            AiTurnAction.SigningExecuted =>
                $"Signed free agent {outcome.Signing!.PlayerName}.",
            _ => outcome.Notes.Count > 0
                ? outcome.Notes[0].Explanation
                : "No legal trade or free-agent candidate was found.",
        };

        var overviewResult = _session.Overview();
        if (overviewResult.IsSuccess)
        {
            _onLeagueChanged(overviewResult.Value);
        }
    }
}
