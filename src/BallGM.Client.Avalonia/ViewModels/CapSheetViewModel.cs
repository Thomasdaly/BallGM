using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The cap sheet. Every figure here comes from <see cref="TeamCapSummary"/> — that is, from
/// contracts that actually exist on this team — and every threshold line carries the rules layer's
/// own explanation rather than one the client invents. The view model formats money and picks the
/// headline; it does not decide what any threshold means.
/// </summary>
public sealed class CapSheetViewModel(LeagueOverview overview) : ViewModelBase
{
    private const string NoTeam = "—";

    private TeamSummary? _team;

    public string Title => "Cap sheet";

    public TeamSummary? Team
    {
        get => _team;
        set
        {
            if (!SetProperty(ref _team, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(TeamName));
            RaisePropertyChanged(nameof(Headline));
            RaisePropertyChanged(nameof(CommittedSalary));
            RaisePropertyChanged(nameof(DeadMoney));
            RaisePropertyChanged(nameof(TotalPayroll));
            RaisePropertyChanged(nameof(Thresholds));
            RaisePropertyChanged(nameof(Charges));
            RaisePropertyChanged(nameof(Transactions));
        }
    }

    public string TeamName => _team?.TeamName ?? "No team selected";

    public string SeasonLine => $"Season {overview.SeasonYear} · thresholds from ruleset \"{overview.RulesetName}\"";

    /// <summary>
    /// The verdict in one line: the payroll, the strictest line it has crossed, and how far the
    /// next line up still is. Deliberately does not repeat the threshold's own explanation — that
    /// sentence appears once, against its own row, rather than twice on the same screen.
    /// </summary>
    public string Headline
    {
        get
        {
            if (_team is null)
            {
                return "Select a team.";
            }

            var capSheet = _team.CapSheet;
            var payroll = MoneyDisplay.ToMillions(capSheet.TotalPayroll);
            var crossed = capSheet.Thresholds.LastOrDefault(threshold => threshold.IsOver);
            var nextLine = capSheet.Thresholds.FirstOrDefault(threshold => !threshold.IsOver);

            if (crossed is null)
            {
                return nextLine is null
                    ? $"{payroll} payroll."
                    : $"{payroll} payroll — {MoneyDisplay.ToMillions(nextLine.SignedDistance)} of room under the {Lower(nextLine)}.";
            }

            var verdict = $"{payroll} payroll — over the {Lower(crossed)} by {MoneyDisplay.ToMillions(Math.Abs(crossed.SignedDistance))}";
            return nextLine is null
                ? $"{verdict}, and past every configured line."
                : $"{verdict}, {MoneyDisplay.ToMillions(nextLine.SignedDistance)} below the {Lower(nextLine)}.";
        }
    }

    private static string Lower(ThresholdStandingSummary threshold) => threshold.ThresholdName.ToLowerInvariant();

    public string CommittedSalary => Format(_team?.CapSheet.CommittedSalary);

    public string DeadMoney => Format(_team?.CapSheet.DeadMoney);

    public string TotalPayroll => Format(_team?.CapSheet.TotalPayroll);

    public IReadOnlyList<ThresholdRow> Thresholds =>
        _team is null
            ? []
            : _team.CapSheet.Thresholds.Select(ThresholdRow.From).ToList();

    public IReadOnlyList<ChargeRow> Charges =>
        _team is null
            ? []
            : _team.CapSheet.Charges.Select(ChargeRow.From).ToList();

    public IReadOnlyList<LedgerRow> Transactions =>
        _team is null
            ? []
            : _team.CapSheet.Transactions.Select(LedgerRow.From).ToList();

    private static string Format(long? smallestUnits) =>
        smallestUnits is null ? NoTeam : MoneyDisplay.ToMillions(smallestUnits.Value);
}
