using BallGM.Application.Leagues;

namespace BallGM.Application.Trades;

/// <summary>
/// A trade as the client can express it: identifiers as strings, exactly as they came off the read
/// model. The session turns this into a domain proposal — the UI never builds one, because building
/// one requires knowing which identifiers currently exist.
/// </summary>
public sealed record TradeRequest(IReadOnlyList<string> TeamIds, IReadOnlyList<TradeAssetRequest> Assets);

/// <summary>One asset, one direction. <paramref name="AssetKind"/> is "player" or "pick".</summary>
public sealed record TradeAssetRequest(string AssetKind, string AssetId, string FromTeamId, string ToTeamId)
{
    public const string PlayerKind = "player";
    public const string PickKind = "pick";
}

/// <summary>
/// The verdict in presentation terms. Warnings travel separately from violations because a trade
/// machine that shows a GM one undifferentiated list of complaints teaches them to ignore all of it.
/// </summary>
public sealed record TradeAssessmentSummary(
    bool IsLegal,
    IReadOnlyList<TradeFindingLine> Violations,
    IReadOnlyList<TradeFindingLine> Warnings,
    IReadOnlyList<TradeTeamOutcomeLine> Teams);

public sealed record TradeFindingLine(string RuleCode, string Explanation, string? TeamName);

/// <summary>
/// What the trade does to one team's books and roster. Present on legal and illegal proposals
/// alike: a rejection with no arithmetic behind it cannot be negotiated against.
/// </summary>
public sealed record TradeTeamOutcomeLine(
    string TeamId,
    string TeamName,
    long IncomingSalary,
    long OutgoingSalary,
    long PayrollBefore,
    long PayrollAfter,
    long PayrollChange,
    int RosterCountBefore,
    int RosterCountAfter,
    int PicksBefore,
    int PicksAfter,
    IReadOnlyList<ThresholdStandingSummary> ThresholdsAfter);

/// <summary>
/// An executed trade, with the league re-projected afterwards so every screen reads the new state
/// from one place rather than each patching its own copy.
/// </summary>
public sealed record TradeSubmission(
    TradeAssessmentSummary Assessment,
    int LedgerEntryCount,
    LeagueOverview Overview);
