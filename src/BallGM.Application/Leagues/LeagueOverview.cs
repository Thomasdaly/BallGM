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
    PickBoardSummary PickBoard);

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

public sealed record CapThresholdSummary(
    long SoftCap,
    long LuxuryTax,
    long FirstApron,
    long SecondApron,
    long HardCap);

/// <summary>
/// One team's finances for the current season, every figure derived from contracts that exist on
/// that team. <see cref="Charges"/> is what the total is made of and <see cref="Transactions"/> is
/// how it got that way, so the screen can answer "why" without the client recomputing anything.
/// </summary>
public sealed record TeamCapSummary(
    int SeasonYear,
    long CommittedSalary,
    long DeadMoney,
    long TotalPayroll,
    IReadOnlyList<ThresholdStandingSummary> Thresholds,
    IReadOnlyList<CapChargeLine> Charges,
    IReadOnlyList<TransactionLine> Transactions);

/// <summary>
/// Where the payroll sits against one threshold. <paramref name="SignedDistance"/> is the threshold
/// minus the payroll — positive is room left, negative is the amount over — and
/// <paramref name="RuleCode"/> is the machine-readable half the UI can key behaviour off.
/// </summary>
public sealed record ThresholdStandingSummary(
    string ThresholdName,
    string RuleCode,
    long ThresholdAmount,
    long SignedDistance,
    bool IsOver,
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
