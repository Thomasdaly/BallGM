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
            RaisePropertyChanged(nameof(HasThresholds));
            RaisePropertyChanged(nameof(Charges));
            RaisePropertyChanged(nameof(Transactions));
        }
    }

    public string TeamName => _team?.TeamName ?? "No team selected";

    public string SeasonLine => $"Season {overview.SeasonYear} · thresholds from ruleset \"{overview.RulesetName}\"";

    /// <summary>
    /// Whether this league configures any threshold at all. When it does not, the cap sheet is a
    /// payroll and nothing else, which is the truth about the league rather than a screen that
    /// failed to load.
    /// </summary>
    public bool HasThresholds => _team is { CapSheet.Thresholds.Count: > 0 };

    /// <summary>
    /// What to show instead of the threshold table in a league with no cap system. A blank panel and
    /// "this league has no cap" are the same amount of screen and very different amounts of answer.
    /// </summary>
    public string NoThresholdsExplanation =>
        "This league has no salary cap, no tax line, and no payroll floor. What a team may spend is limited by its roster and its owner, not by a line in the rules, so there is nothing here to measure a payroll against.";

    /// <summary>
    /// The verdict in one line: the payroll, the strictest line it has breached, and how far the
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

            if (capSheet.Thresholds.Count == 0)
            {
                return $"{payroll} payroll. This league sets no cap, so there is no line to be over.";
            }

            // Ceilings only: the payroll floor is breached from below, and "the strictest line you
            // have crossed" is a statement about ceilings.
            var ceilings = capSheet.Thresholds.Where(threshold => !threshold.IsFloor).ToList();
            var floor = capSheet.Thresholds.FirstOrDefault(threshold => threshold.IsFloor);

            if (floor is { IsBreached: true })
            {
                return $"{payroll} payroll — {MoneyDisplay.ToMillions(floor.SignedDistance)} below this league's payroll floor.";
            }

            var crossed = ceilings.LastOrDefault(threshold => threshold.IsOver);
            var nextLine = ceilings.FirstOrDefault(threshold => !threshold.IsOver);

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
