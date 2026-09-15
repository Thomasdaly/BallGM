using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The Milestone 9 diagnostics screen: what a team's own front office would read and do, through
/// <see cref="LeagueSession.FrontOfficeAdvisory"/>. Read-only, like every other read this session
/// answers — nothing on this screen proposes a trade, places an offer, or drafts anyone. It composes
/// and formats what the four AI models already decided; the trade and free-agency screens remain
/// where a GM actually acts on a suggestion shown here.
/// </summary>
public sealed class FrontOfficeViewModel : ViewModelBase
{
    private readonly LeagueSession _session;

    private TeamSummary? _team;
    private string _status = string.Empty;
    private string _directionLine = string.Empty;
    private IReadOnlyList<AIFindingRow> _directionFactors = [];
    private IReadOnlyList<PositionalNeedRow> _positionalNeeds = [];
    private IReadOnlyList<AIFindingRow> _needsNotes = [];
    private IReadOnlyList<TradeTargetRow> _tradeTargets = [];
    private IReadOnlyList<FreeAgentTargetRow> _freeAgentTargets = [];
    private DraftPreviewRow? _draftPreview;
    private string? _draftPreviewNote;

    public FrontOfficeViewModel(LeagueSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public string Title => "Front office";

    /// <summary>The team the screen is read for. Set by the shell, like every other team-scoped screen.</summary>
    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (SetProperty(ref _team, value))
            {
                Refresh();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

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

        Status = $"Read for {advisory.TeamName}. Nothing on this screen has executed.";
        DirectionLine = advisory.Direction.Direction;
        DirectionFactors = advisory.Direction.Factors.Select(AIFindingRow.From).ToList();

        PositionalNeeds = advisory.Needs.PositionalNeeds.Select(PositionalNeedRow.From).ToList();
        NeedsNotes = advisory.Needs.Notes.Select(AIFindingRow.From).ToList();

        TradeTargets = advisory.TradeTargets.Select(target => TradeTargetRow.From(target, advisory.TeamId)).ToList();
        FreeAgentTargets = advisory.FreeAgentTargets.Select(FreeAgentTargetRow.From).ToList();

        DraftPreview = advisory.DraftPreview is { } preview ? DraftPreviewRow.From(preview) : null;
        DraftPreviewNote = advisory.DraftPreviewNote;
    }
}
