using BallGM.Application.Leagues;

namespace BallGM.Application.Negotiations;

/// <summary>
/// An offer as the client can express it: identifiers as strings and money as plain integers, exactly
/// as they came off the read model. The session turns this into a domain offer — the UI never builds
/// one, because building one requires knowing which identifiers currently exist.
/// </summary>
public sealed record OfferRequest(
    string TeamId,
    string PlayerId,
    IReadOnlyList<OfferSeasonRequest> Seasons);

/// <summary>
/// One proposed season. <paramref name="GuaranteedAmount"/> is what survives a release; leaving it
/// equal to the compensation is a fully guaranteed season, which is what an offer screen defaults to.
/// </summary>
public sealed record OfferSeasonRequest(int SeasonYear, long Compensation, long GuaranteedAmount);

/// <summary>
/// The verdict in presentation terms. The three lists stay separate for the reason the trade summary
/// keeps them separate: a rule this league does not have is information about the league, not a
/// caution about this offer, and folding them together teaches a GM to ignore all of it.
/// </summary>
public sealed record SigningAssessmentSummary(
    bool IsLegal,
    string PlayerId,
    string PlayerName,
    string TeamId,
    string TeamName,
    int SeasonCount,
    long FirstSeasonCompensation,
    long TotalCompensation,
    long TotalGuaranteed,
    IReadOnlyList<SigningFindingLine> Violations,
    IReadOnlyList<SigningFindingLine> Warnings,
    IReadOnlyList<SigningFindingLine> Notes,
    IReadOnlyList<SigningRouteLine> Routes,
    string? PermittingRouteName,
    long PayrollBefore,
    long PayrollAfter,
    int RosterCountBefore,
    int RosterCountAfter,
    long? CapRoomBefore);

public sealed record SigningFindingLine(string RuleCode, string Explanation);

/// <summary>
/// One route's verdict. <paramref name="Applicable"/> is what separates "this league has no such
/// line" from "you cannot afford it", and a screen that renders the two the same way is a screen
/// that teaches GMs the rules of a league they are not playing in.
/// </summary>
public sealed record SigningRouteLine(
    string RouteName,
    bool Applicable,
    bool Permits,
    long? MaximumFirstSeasonCompensation,
    string RuleCode,
    string Explanation);

/// <summary>
/// A completed signing, with the league re-projected afterwards so every screen reads the new state
/// from one place rather than each patching its own copy.
/// </summary>
public sealed record SigningSubmission(
    SigningAssessmentSummary Assessment,
    string RouteName,
    int LedgerEntryCount,
    LeagueOverview Overview);
