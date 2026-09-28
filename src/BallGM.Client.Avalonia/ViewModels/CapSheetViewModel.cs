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
            RaisePropertyChanged(nameof(HasTaxBill));
            RaisePropertyChanged(nameof(TaxOwed));
            RaisePropertyChanged(nameof(TaxBillLine));
            RaisePropertyChanged(nameof(TaxBrackets));
            RaisePropertyChanged(nameof(Restrictions));
            RaisePropertyChanged(nameof(HasRestrictions));
            RaisePropertyChanged(nameof(GaugeFill));
            RaisePropertyChanged(nameof(GaugeMarkers));
        }
    }

    /// <summary>Pixel width of the cap-position gauge; markers are placed against it.</summary>
    public const double GaugeWidth = 820;

    private double GaugeMaximum => _team is null
        ? 1
        : Math.Max(_team.CapSheet.TotalPayroll, _team.CapSheet.Thresholds.Select(threshold => threshold.ThresholdAmount).DefaultIfEmpty(0).Max()) * 1.08;

    /// <summary>How far along the gauge the payroll reaches, in pixels.</summary>
    public double GaugeFill => _team is null ? 0 : GaugeWidth * _team.CapSheet.TotalPayroll / GaugeMaximum;

    /// <summary>One tick per configured line, placed where its amount falls on the gauge.</summary>
    public IReadOnlyList<GaugeMarker> GaugeMarkers => _team is null
        ? []
        : _team.CapSheet.Thresholds
            .Select((threshold, index) => new GaugeMarker(
                threshold.ThresholdName,
                MoneyDisplay.ToMillions(threshold.ThresholdAmount),
                GaugeWidth * threshold.ThresholdAmount / GaugeMaximum,
                threshold.IsBreached,
                LabelTop: index % 2 == 0 ? 44 : 0))
            .ToList();

    public bool HasTaxBill => _team?.CapSheet.TaxBill is not null;

    public string TaxOwed => _team?.CapSheet.TaxBill is { } bill ? MoneyDisplay.ToMillions(bill.TaxOwed) : NoTeam;

    public string TaxBillLine => _team?.CapSheet.TaxBill switch
    {
        null => string.Empty,
        { AmountOverTaxLine: <= 0 } bill => $"Under the tax line by {MoneyDisplay.ToMillions(bill.TaxLine - (_team!.CapSheet.TotalPayroll))} — no tax owed{(bill.IsRepeater ? " (a repeater would pay the higher schedule)" : string.Empty)}.",
        var bill => $"{MoneyDisplay.ToMillions(bill.AmountOverTaxLine)} over the tax line, charged at the {(bill.IsRepeater ? "repeater" : "standard")} rates.",
    };

    public IReadOnlyList<TaxBracketRow> TaxBrackets => _team?.CapSheet.TaxBill is { } bill
        ? bill.Brackets.Select(TaxBracketRow.From).ToList()
        : [];

    public IReadOnlyList<RestrictionRow> Restrictions => _team?.CapSheet.Restrictions is { } restrictions
        ? restrictions.Select(line => new RestrictionRow(line.Explanation, line.RuleCode)).ToList()
        : [];

    public bool HasRestrictions => _team?.CapSheet.Restrictions is { Count: > 0 };

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

public sealed record GaugeMarker(string Name, string Amount, double Left, bool IsBreached, double LabelTop);

public sealed record RestrictionRow(string Explanation, string RuleCode);

public sealed record TaxBracketRow(string Bracket, string Salary, string Rate, string Tax)
{
    public static TaxBracketRow From(TaxBracketLine line) => new(
        $"Bracket {line.Bracket}",
        MoneyDisplay.ToMillions(line.SalaryInBracket),
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"${line.RatePercent / 100m:0.00} per $1"),
        MoneyDisplay.ToMillions(line.Tax));
}
