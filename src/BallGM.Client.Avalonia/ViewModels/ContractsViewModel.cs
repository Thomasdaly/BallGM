using System.Globalization;
using BallGM.Application.Leagues;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The contracts screen: the payroll outlook across coming seasons, the free-agent classes they
/// produce, re-signing a player before he gets there, and an alert for every refusal. Everything
/// decided here is display — who is eligible, what he asks, whether he accepts all come from the
/// session.
/// </summary>
public sealed class ContractsViewModel : ViewModelBase
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly LeagueSession _session;
    private readonly Action _onContractsChanged;
    private TeamSummary? _team;
    private int _capGrowthPercent = 5;
    private bool _wholeLeague;
    private UpcomingFreeAgentRow? _selected;
    private ExtensionTermsSummary? _terms;
    private string _offerSalary = string.Empty;
    private decimal _offerSeasons = 4;
    private decimal _offerRaise = 5;
    private string? _outcome;
    private bool _outcomeAccepted;

    public ContractsViewModel(LeagueSession session, Action onContractsChanged)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _onContractsChanged = onContractsChanged ?? throw new ArgumentNullException(nameof(onContractsChanged));
        OfferCommand = new RelayCommand(Offer);
        SelectForExtensionCommand = new ParameterCommand<string>(SelectForExtension);
    }

    public string Title => "Contracts";

    public TeamSummary? Team
    {
        get => _team;
        set
        {
            var sameTeam = _team?.TeamId == value?.TeamId;
            if (SetProperty(ref _team, value))
            {
                // A league change hands back a fresh summary of the same team; only a different team
                // clears the player being negotiated with.
                if (!sameTeam)
                {
                    Selected = null;
                    Terms = null;
                    Outcome = null;
                }

                Refresh();
            }
        }
    }

    public IReadOnlyList<int> GrowthOptions { get; } = [0, 5, 8, 10];

    /// <summary>Assumed cap growth per season for projecting the lines forward.</summary>
    public int CapGrowthPercent
    {
        get => _capGrowthPercent;
        set
        {
            if (SetProperty(ref _capGrowthPercent, value))
            {
                RaiseOutlook();
            }
        }
    }

    public bool WholeLeague
    {
        get => _wholeLeague;
        set
        {
            if (SetProperty(ref _wholeLeague, value))
            {
                RaisePropertyChanged(nameof(FreeAgentClasses));
            }
        }
    }

    public void Refresh()
    {
        RaiseOutlook();
        RaisePropertyChanged(nameof(FreeAgentClasses));
        RaisePropertyChanged(nameof(Alerts));
        RaisePropertyChanged(nameof(HasAlerts));
        RaisePropertyChanged(nameof(AlertCountLine));
    }

    private CapOutlookSummary? Outlook => _team is null ? null : _session.CapOutlook(_team.TeamId, _capGrowthPercent) is { IsSuccess: true } result ? result.Value : null;

    private void RaiseOutlook()
    {
        RaisePropertyChanged(nameof(Seasons));
        RaisePropertyChanged(nameof(OutlookRows));
        RaisePropertyChanged(nameof(OutlookTotals));
        RaisePropertyChanged(nameof(OutlookHeadline));
    }

    public IReadOnlyList<string> Seasons => Outlook?.Seasons ?? [];

    public IReadOnlyList<OutlookRow> OutlookRows => Outlook?.Players.Select(OutlookRow.From).ToList() ?? [];

    /// <summary>The totals block of the grid: one labelled row per measure, a cell per season.</summary>
    public IReadOnlyList<OutlookTotalRow> OutlookTotals
    {
        get
        {
            if (Outlook is not { } outlook)
            {
                return [];
            }

            OutlookTotalRow Row(string label, Func<CapOutlookSeason, string> cell, bool emphasis = false, Func<CapOutlookSeason, bool>? bad = null) =>
                new(label, outlook.Totals.Select(season => new OutlookTotalCell(cell(season), bad?.Invoke(season) ?? false)).ToList(), emphasis);

            return
            [
                Row("Players under contract", season => season.PlayersUnderContract.ToString(Invariant)),
                Row("Guaranteed", season => Money(season.Guaranteed)),
                Row("+ Options (if taken)", season => season.Options == 0 ? "–" : Money(season.Options)),
                Row("+ Roster holds", season => season.RosterHolds == 0 ? "–" : Money(season.RosterHolds)),
                Row("Payroll", season => Money(season.TotalPayroll), emphasis: true),
                Row("Salary cap (projected)", season => Money(season.SoftCap)),
                Row("Cap room", season => Signed(season.CapRoom), emphasis: true, bad: season => season.CapRoom < 0),
                Row("Tax line (projected)", season => Money(season.LuxuryTax)),
                Row("Room under the tax", season => Signed(season.RoomUnderTax), bad: season => season.RoomUnderTax < 0),
                Row("Position", season => season.Standing, bad: season => season.Standing.Contains("apron", StringComparison.Ordinal)),
            ];
        }
    }

    public string OutlookHeadline => Outlook is { } outlook && outlook.Totals.Count > 1
        ? $"{outlook.Totals[1].Season}: {Money(outlook.Totals[1].Guaranteed)} committed to {outlook.Totals[1].PlayersUnderContract} players — {Signed(outlook.Totals[1].CapRoom)} of cap room at {_capGrowthPercent}% cap growth."
        : string.Empty;

    public IReadOnlyList<FreeAgentClassRow> FreeAgentClasses => _session
        .UpcomingFreeAgents(_wholeLeague ? null : _team?.TeamId)
        .Select(freeAgentClass => new FreeAgentClassRow(
            freeAgentClass.Summer.ToUpper(Invariant),
            $"{freeAgentClass.Players.Count} player{(freeAgentClass.Players.Count == 1 ? string.Empty : "s")}",
            freeAgentClass.Players.Select(UpcomingFreeAgentRow.From).ToList()))
        .ToList();

    public IReadOnlyList<ExtensionRefusalLine> Alerts => _session.ExtensionAlerts(_wholeLeague ? null : _team?.TeamId);

    public bool HasAlerts => Alerts.Count > 0;

    public string AlertCountLine => HasAlerts ? $"{Alerts.Count} player{(Alerts.Count == 1 ? " has" : "s have")} refused to re-sign" : string.Empty;

    // Extension panel.
    public ParameterCommand<string> SelectForExtensionCommand { get; }

    public RelayCommand OfferCommand { get; }

    public UpcomingFreeAgentRow? Selected
    {
        get => _selected;
        private set
        {
            if (SetProperty(ref _selected, value))
            {
                RaisePropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public ExtensionTermsSummary? Terms
    {
        get => _terms;
        private set
        {
            if (SetProperty(ref _terms, value))
            {
                RaisePropertyChanged(nameof(TermsLine));
                RaisePropertyChanged(nameof(AskingLine));
                RaisePropertyChanged(nameof(CanOffer));
            }
        }
    }

    public string TermsLine => _terms is null
        ? string.Empty
        : _terms.Eligible
            ? $"Extension would start {LeagueSession.SeasonLabel(_terms.StartSeason)} · up to {_terms.MaximumSeasons} seasons · salary {Money(_terms.MinimumSalary)} to {Money(_terms.MaximumSalary)} a season."
            : _terms.Reason;

    public string AskingLine => _terms?.AskingPrice is { } ask ? $"He is asking for {Money(ask)} a season (earning {Money(_terms.CurrentSalary)} now)." : string.Empty;

    public bool CanOffer => _terms is { Eligible: true };

    public string OfferSalary
    {
        get => _offerSalary;
        set => SetProperty(ref _offerSalary, value);
    }

    public decimal OfferSeasons
    {
        get => _offerSeasons;
        set => SetProperty(ref _offerSeasons, value);
    }

    public decimal OfferRaise
    {
        get => _offerRaise;
        set => SetProperty(ref _offerRaise, value);
    }

    public string? Outcome
    {
        get => _outcome;
        private set
        {
            if (SetProperty(ref _outcome, value))
            {
                RaisePropertyChanged(nameof(HasOutcome));
            }
        }
    }

    public bool HasOutcome => _outcome is not null;

    public bool OutcomeAccepted
    {
        get => _outcomeAccepted;
        private set => SetProperty(ref _outcomeAccepted, value);
    }

    private void SelectForExtension(string playerId)
    {
        var row = FreeAgentClasses.SelectMany(freeAgentClass => freeAgentClass.Players).FirstOrDefault(candidate => candidate.PlayerId == playerId);
        Selected = row;
        Outcome = null;
        var terms = _session.ExtensionTerms(playerId);
        Terms = terms.IsSuccess ? terms.Value : null;
        if (Terms?.AskingPrice is { } ask)
        {
            // Rounded up, never down: a pre-filled "what he asks" must actually meet what he asks.
            OfferSalary = (Math.Ceiling(ask / 100_000m) / 10m).ToString("0.0", Invariant);
        }
    }

    private void Offer()
    {
        if (_selected is null)
        {
            Outcome = "Pick an extension-eligible player first.";
            OutcomeAccepted = false;
            return;
        }

        if (!decimal.TryParse(_offerSalary, NumberStyles.Number, Invariant, out var millions) || millions <= 0)
        {
            Outcome = "Enter a first-season salary in millions, e.g. 24.5.";
            OutcomeAccepted = false;
            return;
        }

        var result = _session.OfferExtension(_selected.PlayerId, (int)_offerSeasons, (long)(millions * 1_000_000m), (int)_offerRaise);
        if (result.IsFailure)
        {
            Outcome = string.Join(" ", result.Errors.Select(error => error.Message));
            OutcomeAccepted = false;
            return;
        }

        var outcome = result.Value;
        Outcome = outcome.IsLegal ? outcome.Explanation : string.Join(" ", outcome.Violations);
        OutcomeAccepted = outcome.Accepted;
        if (outcome.Accepted)
        {
            Selected = null;
            Terms = null;
        }

        Refresh();
        _onContractsChanged();
    }

    internal static string Money(long? amount) => amount is null ? "–" : MoneyDisplay.ToMillions(amount.Value);

    internal static string Signed(long? amount) => amount is null ? "–" : (amount < 0 ? "−" : "+") + MoneyDisplay.ToMillions(Math.Abs(amount.Value));
}

public sealed record OutlookRow(string FullName, string Position, int Overall, IReadOnlyList<OutlookCell> Cells)
{
    public static OutlookRow From(CapOutlookPlayerRow row) => new(
        row.FullName,
        row.Position,
        row.Overall,
        row.Cells.Select(cell => new OutlookCell(
            cell.Salary is null ? string.Empty : ContractsViewModel.Money(cell.Salary) + (cell.Option switch { "Player" => " PO", "Team" => " TO", _ => string.Empty }),
            cell.Option is not null,
            cell.IsExtension)).ToList());
}

public sealed record OutlookCell(string Text, bool IsOption, bool IsExtension);

public sealed record OutlookTotalRow(string Label, IReadOnlyList<OutlookTotalCell> Cells, bool Emphasis);

public sealed record OutlookTotalCell(string Text, bool IsBad);

public sealed record FreeAgentClassRow(string Summer, string CountLine, IReadOnlyList<UpcomingFreeAgentRow> Players);

public sealed record UpcomingFreeAgentRow(
    string PlayerId, string FullName, string TeamName, string Position, int Overall, string Age, string Salary,
    string Status, bool ExtensionEligible, bool RefusedExtension, string? PortraitPath)
{
    public static UpcomingFreeAgentRow From(UpcomingFreeAgentLine line) => new(
        line.PlayerId,
        line.FullName,
        line.TeamName,
        line.Position,
        line.Overall,
        line.Age.ToString(CultureInfo.InvariantCulture),
        MoneyDisplay.ToMillions(line.CurrentSalary),
        line.RefusedExtension ? "Refused to re-sign"
            : line.FinalSeasonOption is { } option ? $"{option} option"
            : line.ExtensionEligible ? "Extension eligible"
            : "Under contract",
        line.ExtensionEligible,
        line.RefusedExtension,
        line.PortraitPath);
}
