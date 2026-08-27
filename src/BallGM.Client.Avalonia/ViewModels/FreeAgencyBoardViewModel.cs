using System.Globalization;
using System.Windows.Input;
using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The free-agency board: the market columned by position against this team's own depth, and the
/// negotiation controls for whichever player is selected.
/// <para>
/// The market half of Milestone 6. The offer screen answers "what am I allowed to offer"; this
/// answers "who else wants him, what does he want, and what happens when the market resolves". Every
/// verdict comes from the rules layer through <see cref="LeagueSession"/> — this screen composes
/// offers and formats answers, and decides nothing.
/// </para>
/// <para>
/// The day control is deliberately visible rather than hidden behind a clock. Offer expiry is
/// measured in days and there is no calendar yet, so a GM moving the market forward is doing
/// explicitly what advancing a schedule will do for them once one exists.
/// </para>
/// </summary>
public sealed class FreeAgencyBoardViewModel : ViewModelBase
{
    private readonly LeagueSession _session;
    private readonly Action<LeagueOverview> _onLeagueChanged;

    private LeagueOverview _overview;
    private TeamSummary? _team;
    private BoardCandidateRow? _candidate;
    private int _day;
    private string _firstSeasonSalary = "10.0";
    private int _seasons = 3;
    private string _status = "Pick a position column and a free agent to open a market.";
    private string _marketLine = string.Empty;
    private string _boardLine = string.Empty;
    private IReadOnlyList<BoardColumnRow> _columns = [];
    private IReadOnlyList<BoardNegotiationRow> _ourNegotiations = [];
    private IReadOnlyList<MarketStandingRow> _standings = [];
    private IReadOnlyList<SigningFindingRow> _warnings = [];
    private IReadOnlyList<SigningFindingRow> _notes = [];

    public FreeAgencyBoardViewModel(
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
        _team = overview.Teams.FirstOrDefault();

        OfferCommand = new RelayCommand(PlaceOffer);
        CounterCommand = new RelayCommand(Counter);
        CheckMarketCommand = new RelayCommand(CheckMarket);
        ResolveMarketCommand = new RelayCommand(ResolveMarket);

        Refresh();
    }

    public string Title => "Free agency board";

    public ICommand OfferCommand { get; }

    public ICommand CounterCommand { get; }

    public ICommand CheckMarketCommand { get; }

    public ICommand ResolveMarketCommand { get; }

    /// <summary>The team the board is read for. Set by the shell, like every other team-scoped screen.</summary>
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

    public BoardCandidateRow? Candidate
    {
        get => _candidate;
        set
        {
            if (SetProperty(ref _candidate, value))
            {
                ClearMarket();
                RaisePropertyChanged(nameof(HasCandidate));
                RaisePropertyChanged(nameof(CandidateLine));
            }
        }
    }

    public bool HasCandidate => _candidate is not null;

    public string CandidateLine => _candidate is null
        ? "Nobody selected."
        : $"{_candidate.FullName} · {_candidate.Overall} overall · {_candidate.Detail}. {_candidate.AskLine}. {_candidate.MarketLine}. {_candidate.OurOfferLine}.";

    /// <summary>
    /// The day the market is being read on. Offers expire relative to this, so moving it forward is
    /// how a GM finds out what their delay cost them.
    /// </summary>
    public int Day
    {
        get => _day;
        set
        {
            if (SetProperty(ref _day, value))
            {
                Refresh();
            }
        }
    }

    public string FirstSeasonSalary
    {
        get => _firstSeasonSalary;
        set => SetProperty(ref _firstSeasonSalary, value);
    }

    public int Seasons
    {
        get => _seasons;
        set => SetProperty(ref _seasons, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>What this league's market rules are, said before a GM runs into one of them.</summary>
    public string BoardLine
    {
        get => _boardLine;
        private set => SetProperty(ref _boardLine, value);
    }

    public string MarketLine
    {
        get => _marketLine;
        private set => SetProperty(ref _marketLine, value);
    }

    public IReadOnlyList<BoardColumnRow> Columns
    {
        get => _columns;
        private set => SetProperty(ref _columns, value);
    }

    public IReadOnlyList<BoardNegotiationRow> OurNegotiations
    {
        get => _ourNegotiations;
        private set
        {
            if (SetProperty(ref _ourNegotiations, value))
            {
                RaisePropertyChanged(nameof(HasOurNegotiations));
            }
        }
    }

    /// <summary>Every competing offer in finishing order, with the factor breakdown behind each.</summary>
    public IReadOnlyList<MarketStandingRow> Standings
    {
        get => _standings;
        private set
        {
            if (SetProperty(ref _standings, value))
            {
                RaisePropertyChanged(nameof(HasStandings));
            }
        }
    }

    public IReadOnlyList<SigningFindingRow> Warnings
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

    /// <summary>
    /// Rules this league does not configure. Shown plainly rather than as cautions, for the reason
    /// the offer screen shows its own notes plainly: an uncapped league is not a broken one.
    /// </summary>
    public IReadOnlyList<SigningFindingRow> Notes
    {
        get => _notes;
        private set
        {
            if (SetProperty(ref _notes, value))
            {
                RaisePropertyChanged(nameof(HasNotes));
            }
        }
    }

    public bool HasStandings => Standings.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasNotes => Notes.Count > 0;

    public bool HasOurNegotiations => OurNegotiations.Count > 0;

    /// <summary>Rebuilds the board against the league and the day as they stand.</summary>
    public void RefreshFrom(LeagueOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        _overview = overview;
        _team = overview.Teams.FirstOrDefault(team => team.TeamId == _team?.TeamId) ?? overview.Teams.FirstOrDefault();
        RaisePropertyChanged(nameof(Team));
        Refresh();
    }

    private void Refresh()
    {
        if (_team is null)
        {
            Columns = [];
            OurNegotiations = [];
            BoardLine = "No team selected.";
            return;
        }

        var result = _session.FreeAgencyBoard(_team.TeamId, _day);
        if (result.IsFailure)
        {
            Columns = [];
            OurNegotiations = [];
            BoardLine = string.Join(" ", result.Errors.Select(error => error.Message));
            return;
        }

        var board = result.Value;
        Columns = board.Columns.Select(BoardColumnRow.From).ToList();
        OurNegotiations = board.OurNegotiations.Select(BoardNegotiationRow.From).ToList();

        var expiry = board.OfferExpiryDays is { } days
            ? $"offers stand for {days} day(s)"
            : "offers never expire";

        var mode = board.ResolutionMode == "Immediate"
            ? "the first acceptable offer wins the moment it lands"
            : "offers accumulate and the market resolves at a point you choose";

        BoardLine = $"Day {board.Day}. In this league {mode}, and {expiry}.";

        // The selected candidate is re-read from the rebuilt columns so the panel keeps showing the
        // same player rather than a stale copy of what they looked like before the last offer.
        if (_candidate is not null)
        {
            _candidate = Columns
                .SelectMany(column => column.Candidates)
                .FirstOrDefault(candidate => candidate.PlayerId == _candidate.PlayerId);

            RaisePropertyChanged(nameof(Candidate));
            RaisePropertyChanged(nameof(HasCandidate));
            RaisePropertyChanged(nameof(CandidateLine));
        }
    }

    private void PlaceOffer()
    {
        var request = BuildOfferRequest();
        if (request is null)
        {
            return;
        }

        var result = _session.PlaceOffer(request, _day);
        if (result.IsFailure)
        {
            Status = string.Join(" ", result.Errors.Select(error => $"{error.Code}: {error.Message}"));
            return;
        }

        var negotiation = result.Value;
        Status = $"Offer on the table for {negotiation.PlayerName}. " +
                 $"{negotiation.LiveOfferCount} offer(s) standing on day {_day}. Check the market to see who would win.";

        Refresh();
    }

    /// <summary>
    /// Records what the player would rather have from this team. Not an acceptance and not a state
    /// change — the market stays open, and the team answers a counter by offering again.
    /// </summary>
    private void Counter()
    {
        if (_team is null || _candidate is null)
        {
            Status = "Pick a team and a free agent first.";
            return;
        }

        // The offer identifier comes off the board's own read model. A counter has to name what it
        // answers, and a screen that went and found that out for itself would be a screen holding a
        // domain aggregate.
        if (_candidate.OurOfferId is not { } answeredOfferId)
        {
            Status = "Nothing of ours is on the table to be countered. Make an offer first.";
            return;
        }

        var seasons = BuildSeasons();
        if (seasons is null)
        {
            return;
        }

        var result = _session.Counteroffer(
            new CounterofferRequest(_candidate.PlayerId, _team.TeamId, answeredOfferId, seasons),
            _day);

        if (result.IsFailure)
        {
            Status = string.Join(" ", result.Errors.Select(error => $"{error.Code}: {error.Message}"));
            return;
        }

        Status = $"{result.Value.PlayerName} countered. The market is still open — answer it with a new offer.";
        Refresh();
    }

    private void CheckMarket()
    {
        if (_candidate is null)
        {
            Status = "Pick a free agent first.";
            return;
        }

        var result = _session.AssessMarket(_candidate.PlayerId, _day);
        if (result.IsFailure)
        {
            ShowFailure(result.Errors.Select(error => new SigningFindingRow(error.Code, error.Message)).ToList());
            Status = "The market could not be assessed.";
            return;
        }

        Apply(result.Value);
        Status = "Nothing has been changed. Resolve the market to make it happen.";
    }

    /// <summary>
    /// Resolves for real. Every competing offer is re-checked against the league as it stands, so an
    /// offer that would have won a moment ago can still be refused here — and that refusal is the
    /// system working.
    /// </summary>
    private void ResolveMarket()
    {
        if (_candidate is null)
        {
            Status = "Pick a free agent first.";
            return;
        }

        var result = _session.ResolveMarket(_candidate.PlayerId, _day);
        if (result.IsFailure)
        {
            ShowFailure(result.Errors.Select(error => new SigningFindingRow(error.Code, error.Message)).ToList());
            Status = "The market was not resolved and nothing was changed.";
            return;
        }

        var submission = result.Value;
        Apply(submission.Assessment);

        Status = submission.Signed
            ? $"{submission.Assessment.PlayerName} signed with {submission.Assessment.WinningTeamName} using " +
              $"{submission.RouteName?.ToLowerInvariant()}. {submission.LedgerEntryCount} ledger entry recorded."
            : $"The market closed with nobody signing {submission.Assessment.PlayerName}.";

        RefreshFrom(submission.Overview);
        _onLeagueChanged(submission.Overview);
    }

    private void Apply(MarketAssessmentSummary assessment)
    {
        Standings = assessment.Standings.Select(MarketStandingRow.From).ToList();
        Warnings = assessment.Warnings.Select(SigningFindingRow.From).ToList();
        Notes = assessment.Notes.Select(SigningFindingRow.From).ToList();

        var draw = assessment.TieBreakUsed
            ? " A seeded draw settled it: nothing separated the leading offers on any factor."
            : string.Empty;

        MarketLine = $"{assessment.Narrative}{draw}";
    }

    private void ShowFailure(IReadOnlyList<SigningFindingRow> findings)
    {
        Standings = [];
        Warnings = findings;
        Notes = [];
        MarketLine = string.Empty;
    }

    private void ClearMarket()
    {
        Standings = [];
        Warnings = [];
        Notes = [];
        MarketLine = string.Empty;
        Status = "Selection changed. Check the market again.";
    }

    private OfferRequest? BuildOfferRequest()
    {
        if (_team is null)
        {
            Status = "Pick a team first.";
            return null;
        }

        if (_candidate is null)
        {
            Status = "Pick a free agent first.";
            return null;
        }

        var seasons = BuildSeasons();
        return seasons is null ? null : new OfferRequest(_team.TeamId, _candidate.PlayerId, seasons);
    }

    /// <summary>
    /// Lays the typed terms out across the seasons. Arithmetic, not judgement: every season is flat
    /// and fully guaranteed, and whether any of it is legal is a question only the rules layer answers.
    /// </summary>
    private IReadOnlyList<OfferSeasonRequest>? BuildSeasons()
    {
        if (!double.TryParse(_firstSeasonSalary, NumberStyles.Float, CultureInfo.InvariantCulture, out var millions) || millions <= 0)
        {
            Status = $"'{_firstSeasonSalary}' is not a salary. Enter the first season in millions, such as 12.5.";
            return null;
        }

        if (_seasons < 1)
        {
            Status = "An offer has to cover at least one season.";
            return null;
        }

        var compensation = (long)Math.Round(millions * 1_000_000d);

        return Enumerable
            .Range(0, _seasons)
            .Select(index => new OfferSeasonRequest(_overview.SeasonYear + index, compensation, compensation))
            .ToList();
    }
}
