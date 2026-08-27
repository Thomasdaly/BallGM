namespace BallGM.Application.Leagues;

/// <summary>
/// Presentation-facing read model for the league. Every field is a primitive or a string so the
/// client renders a league without touching aggregate internals — the Avalonia project can see
/// Domain transitively, and this shape is what stops it from depending on that.
/// Money is carried as smallest units; formatting it for a human is the view model's job.
/// </summary>
public sealed record LeagueOverview(
    string LeagueName,
    string RulesetName,
    int SeasonYear,
    int RegularSeasonGameCount,
    int MinimumRosterPlayers,
    int MaximumRosterPlayers,
    CapThresholdSummary CapThresholds,
    IReadOnlyList<TeamSummary> Teams,
    PickBoardSummary PickBoard,
    FreeAgentMarketSummary FreeAgents);

public sealed record TeamSummary(
    string TeamId,
    string TeamName,
    string FranchiseName,
    int RosterCount,
    IReadOnlyList<RosterSpot> Roster,
    TeamCapSummary CapSheet);

public sealed record RosterSpot(
    string PlayerId,
    string FullName,
    string Position,
    int Overall,
    bool IsInjured,
    string? InjuryDescription,
    long CapCharge,
    int ContractSeasonsRemaining);

/// <summary>
/// The league's configured lines, each null when the league does not have it. Null rather than zero
/// all the way to the screen: a cap sheet that renders a missing soft cap as "0" tells a GM in an
/// uncapped league that they are over it by their entire payroll.
/// </summary>
public sealed record CapThresholdSummary(
    long? PayrollFloor,
    long? SoftCap,
    long? LuxuryTax,
    long? FirstApron,
    long? SecondApron,
    long? HardCap)
{
    /// <summary>Whether this league has no cap system at all, and so nothing to measure a payroll against.</summary>
    public bool IsUncapped =>
        PayrollFloor is null && SoftCap is null && LuxuryTax is null &&
        FirstApron is null && SecondApron is null && HardCap is null;
}

/// <summary>
/// One team's finances for the current season, every figure derived from contracts that exist on
/// that team plus the spots it still has to fill. <see cref="Charges"/> is what the total is made of
/// and <see cref="Transactions"/> is how it got that way, so the screen can answer "why" without the
/// client recomputing anything. <see cref="RosterHolds"/> is included in
/// <see cref="TotalPayroll"/>: room a team is obliged to spend filling its roster is not room.
/// </summary>
public sealed record TeamCapSummary(
    int SeasonYear,
    long CommittedSalary,
    long DeadMoney,
    long RosterHolds,
    long TotalPayroll,
    IReadOnlyList<ThresholdStandingSummary> Thresholds,
    IReadOnlyList<CapChargeLine> Charges,
    IReadOnlyList<TransactionLine> Transactions);

/// <summary>
/// Where the payroll sits against one threshold. <paramref name="SignedDistance"/> is the threshold
/// minus the payroll — positive is room left, negative is the amount over — and
/// <paramref name="RuleCode"/> is the machine-readable half the UI can key behaviour off.
/// </summary>
/// <summary>
/// <paramref name="IsBreached"/> rather than <paramref name="IsOver"/> is what a screen should key
/// off: the payroll floor is the one line a team is on the wrong side of by being <em>under</em> it.
/// </summary>
public sealed record ThresholdStandingSummary(
    string ThresholdName,
    string RuleCode,
    long ThresholdAmount,
    long SignedDistance,
    bool IsOver,
    bool IsBreached,
    bool IsFloor,
    string Explanation);

public sealed record CapChargeLine(
    string PlayerName,
    string Kind,
    long Amount,
    bool IsDeadMoney);

public sealed record TransactionLine(
    string RecordedAt,
    string Kind,
    long? Amount,
    string Reason);

/// <summary>
/// The pick-ownership board: franchises down, the next several drafts across. Every figure here is
/// projected from the same ownership book the rules layer validates against, so the screen cannot
/// disagree with the rules about who owns what.
/// </summary>
public sealed record PickBoardSummary(
    int FirstDraftSeason,
    int DraftCount,
    int RoundCount,
    IReadOnlyList<int> DraftSeasons,
    IReadOnlyList<FranchisePickRow> Franchises);

public sealed record FranchisePickRow(
    string FranchiseId,
    string FranchiseName,
    IReadOnlyList<FranchisePickCell> Drafts);

public sealed record FranchisePickCell(
    int DraftSeason,
    IReadOnlyList<PickAssetSummary> Assets);

/// <summary>
/// One pick as the board shows it. <paramref name="ProtectionSummary"/> and
/// <paramref name="OutcomeIfProtectionHolds"/> come from the rules layer rather than from the view,
/// and <paramref name="History"/> is the drill-down: every ledger line this asset has ever produced.
/// </summary>
public sealed record PickAssetSummary(
    string PickId,
    int Round,
    string Label,
    string State,
    string OriginalFranchiseName,
    string CurrentOwnerName,
    string? CounterpartyName,
    string? ProtectionSummary,
    string? OutcomeIfProtectionHolds,
    IReadOnlyList<TransactionLine> History);

/// <summary>
/// Who is available to sign, and what this league permits anyone to pay them. Deliberately not a
/// ranking: what a player is <em>worth</em> and what they will <em>accept</em> are the preference
/// model's answers, and putting a number here before that exists would be a number a GM would learn
/// to trust and then find was invented.
/// </summary>
public sealed record FreeAgentMarketSummary(
    IReadOnlyList<FreeAgentLine> Players,
    bool LeagueHasCompensationFloor,
    bool LeagueHasCompensationCeiling,
    int? MaximumContractSeasons);

/// <summary>
/// One available player. <paramref name="MinimumSalary"/> and <paramref name="MaximumSalary"/> are
/// what the rules permit for this player's service, not what they are asking — both are <c>null</c>
/// in a league that configures no such line, which is the whole reason they are nullable.
/// </summary>
public sealed record FreeAgentLine(
    string PlayerId,
    string FullName,
    string Position,
    int Overall,
    int Age,
    int SeasonsOfService,
    bool IsInjured,
    string? InjuryDescription,
    long? MinimumSalary,
    long? MaximumSalary);
