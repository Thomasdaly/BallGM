using System.Globalization;
using System.Windows.Input;
using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The offer screen. Picks a team and an unsigned player, builds a contract offer, asks the rules
/// what they think as often as a GM likes, and — only when asked twice — signs.
/// <para>
/// Every verdict here comes from the rules layer through <see cref="LeagueSession"/>. The view model
/// composes an offer and formats the answer; it decides nothing about legality, which is why a
/// refusal on this screen reads the same as a refusal anywhere else in the game.
/// </para>
/// <para>
/// This is the offer half of the milestone. Who <em>else</em> is chasing the same player, what that
/// player would rather have, and what happens when two teams want them at once are the market half,
/// and this screen has a visible hole where they go: it can tell a GM what they are allowed to
/// offer, and cannot yet tell them what it would take.
/// </para>
/// </summary>
public sealed class FreeAgencyViewModel : ViewModelBase
{
    private readonly LeagueSession _session;
    private readonly Action<LeagueOverview> _onLeagueChanged;

    private LeagueOverview _overview;
    private TeamSummary? _team;
    private FreeAgentRow? _player;
    private string _firstSeasonSalary = "10.0";
    private int _seasons = 3;
    private int _annualRaisePercent;
    private string _status = "Pick a team and a free agent, set the terms, then check the offer.";
    private bool _hasAssessment;
    private bool _isLegal;
    private IReadOnlyList<SigningFindingRow> _violations = [];
    private IReadOnlyList<SigningFindingRow> _warnings = [];
    private IReadOnlyList<SigningFindingRow> _notes = [];
    private IReadOnlyList<SigningRouteRow> _routes = [];
    private string _outcomeLine = string.Empty;

    public FreeAgencyViewModel(
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
        FreeAgents = overview.FreeAgents.Players.Select(FreeAgentRow.From).ToList();

        _team = Teams.FirstOrDefault();
        _player = FreeAgents.FirstOrDefault();

        CheckCommand = new RelayCommand(Check);
        SignCommand = new RelayCommand(Sign);
    }

    public string Title => "Free agency";

    public IReadOnlyList<TeamSummary> Teams { get; private set; }

    public IReadOnlyList<FreeAgentRow> FreeAgents { get; private set; }

    public ICommand CheckCommand { get; }

    public ICommand SignCommand { get; }

    public bool HasFreeAgents => FreeAgents.Count > 0;

    /// <summary>
    /// What the league permits, stated before a GM offers anything rather than only after they are
    /// refused. A market screen that answers "no" without ever having said what "yes" looks like
    /// teaches bidding blind.
    /// </summary>
    public string LeagueTermsLine
    {
        get
        {
            var market = _overview.FreeAgents;
            var parts = new List<string>
            {
                market.MaximumContractSeasons is { } seasons
                    ? $"contracts up to {seasons} seasons"
                    : "no limit on contract length",
                market.LeagueHasCompensationFloor ? "a minimum salary by service" : "no minimum salary",
                market.LeagueHasCompensationCeiling ? "a maximum salary by service" : "no maximum salary",
            };

            return $"This league has {string.Join(", ", parts)}.";
        }
    }

    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (SetProperty(ref _team, value))
            {
                ClearAssessment();
            }
        }
    }

    public FreeAgentRow? Player
    {
        get => _player;
        set
        {
            if (SetProperty(ref _player, value))
            {
                ClearAssessment();
            }
        }
    }

    /// <summary>The first season's salary, in millions, as typed. Parsed when the offer is built.</summary>
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

    /// <summary>
    /// The annual raise, as a percentage of the first season. Expressed that way rather than as a
    /// compounding percentage because that is how the league's own escalation limit is expressed,
    /// and an offer form whose control means something different from the rule it runs into is a
    /// form that produces surprises.
    /// </summary>
    public int AnnualRaisePercent
    {
        get => _annualRaisePercent;
        set => SetProperty(ref _annualRaisePercent, value);
    }

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

    /// <summary>Whether the last assessment passed. Drives the sign button, so nothing illegal is sent.</summary>
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
            ? "This offer is legal, and some route pays for it."
            : "Refused. Nothing has been changed.";

    public IReadOnlyList<SigningFindingRow> Violations
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
    /// Rules this league does not configure, and that the validator therefore skipped. Shown plainly
    /// rather than as warnings: a GM in an uncapped league needs to know no ceiling was applied, and
    /// needs not to read that as a problem with the offer.
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

    /// <summary>Every route's verdict, including the ones that do not apply here.</summary>
    public IReadOnlyList<SigningRouteRow> Routes
    {
        get => _routes;
        private set => SetProperty(ref _routes, value);
    }

    public string OutcomeLine
    {
        get => _outcomeLine;
        private set => SetProperty(ref _outcomeLine, value);
    }

    public bool HasViolations => Violations.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasNotes => Notes.Count > 0;

    private void Check()
    {
        var request = BuildRequest();
        if (request is null)
        {
            return;
        }

        var result = _session.AssessOffer(request);
        if (result.IsFailure)
        {
            ShowFailure(result.Errors.Select(error => new SigningFindingRow(error.Code, error.Message)).ToList());
            Status = "The offer could not be assessed.";
            return;
        }

        Apply(result.Value);
        Status = IsLegal
            ? "This offer would be permitted. Sign it to execute."
            : "This offer would be refused. The reasons are below.";
    }

    /// <summary>
    /// Signs. The engine re-validates against the league as it stands, so an offer that passed a
    /// check a moment ago can still be refused here — and that refusal is the system working.
    /// </summary>
    private void Sign()
    {
        var request = BuildRequest();
        if (request is null)
        {
            return;
        }

        var result = _session.SubmitOffer(request);
        if (result.IsFailure)
        {
            ShowFailure(result.Errors.Select(error => new SigningFindingRow(error.Code, error.Message)).ToList());
            Status = "The player was not signed and nothing was changed.";
            return;
        }

        var submission = result.Value;
        Apply(submission.Assessment);

        Status = $"{submission.Assessment.PlayerName} signed with {submission.Assessment.TeamName} " +
                 $"using {submission.RouteName.ToLowerInvariant()}. " +
                 $"{submission.LedgerEntryCount} ledger entry recorded; the roster and cap sheet now show it.";

        RefreshFrom(submission.Overview);
        _onLeagueChanged(submission.Overview);
    }

    private void RefreshFrom(LeagueOverview overview)
    {
        _overview = overview;
        Teams = overview.Teams;
        FreeAgents = overview.FreeAgents.Players.Select(FreeAgentRow.From).ToList();

        _team = Teams.FirstOrDefault(team => team.TeamId == _team?.TeamId) ?? Teams.FirstOrDefault();
        _player = FreeAgents.FirstOrDefault(player => player.PlayerId == _player?.PlayerId) ?? FreeAgents.FirstOrDefault();

        RaisePropertyChanged(nameof(Teams));
        RaisePropertyChanged(nameof(FreeAgents));
        RaisePropertyChanged(nameof(HasFreeAgents));
        RaisePropertyChanged(nameof(Team));
        RaisePropertyChanged(nameof(Player));
        RaisePropertyChanged(nameof(LeagueTermsLine));
    }

    /// <summary>
    /// Composes the offer the GM has described. Arithmetic, not judgement: the raise is laid out
    /// across the seasons exactly as typed and every season is fully guaranteed, and whether any of
    /// that is legal is a question only the rules layer answers.
    /// </summary>
    private OfferRequest? BuildRequest()
    {
        if (_team is null)
        {
            Status = "Pick a team first.";
            return null;
        }

        if (_player is null)
        {
            Status = "Pick a free agent first.";
            return null;
        }

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

        var firstSeason = (long)Math.Round(millions * 1_000_000d);
        var step = firstSeason * _annualRaisePercent / 100;

        var seasons = Enumerable
            .Range(0, _seasons)
            .Select(index =>
            {
                var compensation = firstSeason + (step * index);
                return new OfferSeasonRequest(_overview.SeasonYear + index, compensation, compensation);
            })
            .ToList();

        return new OfferRequest(_team.TeamId, _player.PlayerId, seasons);
    }

    private void Apply(SigningAssessmentSummary assessment)
    {
        HasAssessment = true;
        IsLegal = assessment.IsLegal;
        Violations = assessment.Violations.Select(SigningFindingRow.From).ToList();
        Warnings = assessment.Warnings.Select(SigningFindingRow.From).ToList();
        Notes = assessment.Notes.Select(SigningFindingRow.From).ToList();
        Routes = assessment.Routes.Select(SigningRouteRow.From).ToList();

        var room = assessment.CapRoomBefore is { } capRoom
            ? $"Cap room {MoneyDisplay.ToMillions(capRoom)}. "
            : "This league sets no cap, so there is no room to measure. ";

        OutcomeLine =
            $"{room}Payroll {MoneyDisplay.ToMillions(assessment.PayrollBefore)} → {MoneyDisplay.ToMillions(assessment.PayrollAfter)}. " +
            $"Roster {assessment.RosterCountBefore} → {assessment.RosterCountAfter}. " +
            $"Total commitment {MoneyDisplay.ToMillions(assessment.TotalCompensation)} over {assessment.SeasonCount} season(s).";
    }

    private void ShowFailure(IReadOnlyList<SigningFindingRow> findings)
    {
        HasAssessment = true;
        IsLegal = false;
        Violations = findings;
        Warnings = [];
        Notes = [];
        Routes = [];
        OutcomeLine = string.Empty;
    }

    private void ClearAssessment()
    {
        HasAssessment = false;
        IsLegal = false;
        Violations = [];
        Warnings = [];
        Notes = [];
        Routes = [];
        OutcomeLine = string.Empty;
        Status = "Terms changed. Check the offer again.";
    }
}
