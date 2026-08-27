namespace BallGM.Application.Negotiations;

/// <summary>
/// A counteroffer as the client can express it: the player's terms, addressed at the team whose
/// offer they are answering. The same primitive-only shape as <see cref="OfferRequest"/>, and for
/// the same reason — the UI never builds a domain offer, because doing so requires knowing which
/// identifiers currently exist.
/// </summary>
public sealed record CounterofferRequest(
    string PlayerId,
    string TeamId,
    string InResponseToOfferId,
    IReadOnlyList<OfferSeasonRequest> Seasons);

/// <summary>
/// One factor's say on one offer, formatted for a screen. The four lines always travel together and
/// are never added up — a GM who was outbid needs to read which factor beat them, and a single
/// number cannot say.
/// </summary>
public sealed record PreferenceFactorLine(
    string Factor,
    int Score,
    int MaterialityBand,
    string RuleCode,
    string Explanation);

/// <summary>
/// Where one offer finished. <paramref name="Rank"/> is 1 for the offer the player took and 0 for an
/// offer that was excluded — and <paramref name="Exclusions"/> says which of the two kinds of
/// exclusion it was: an offer the league would not permit, or one the player would not accept.
/// </summary>
public sealed record MarketStandingLine(
    string OfferId,
    string TeamId,
    string TeamName,
    int Rank,
    bool IsSignable,
    bool MeetsAskingPrice,
    long FirstSeasonCompensation,
    int SeasonCount,
    string Narrative,
    IReadOnlyList<PreferenceFactorLine> Factors,
    IReadOnlyList<SigningFindingLine> Exclusions);

/// <summary>
/// What would happen if this market resolved now. <paramref name="TieBreakUsed"/> is surfaced rather
/// than hidden: where the model genuinely could not separate two offers, "the draw landed that way"
/// is a better answer to a GM than a reason invented after the fact.
/// </summary>
public sealed record MarketAssessmentSummary(
    string NegotiationId,
    string PlayerId,
    string PlayerName,
    int Day,
    string ResolutionMode,
    bool WouldSign,
    string? WinningTeamId,
    string? WinningTeamName,
    bool TieBreakUsed,
    string Narrative,
    IReadOnlyList<MarketStandingLine> Standings,
    IReadOnlyList<SigningFindingLine> Warnings,
    IReadOnlyList<SigningFindingLine> Notes);

/// <summary>One line of a negotiation's history, as a screen shows it.</summary>
public sealed record NegotiationEntryLine(
    int Sequence,
    string Kind,
    int Day,
    string Author,
    string? TeamId,
    string? TeamName,
    long? FirstSeasonCompensation,
    int? SeasonCount,
    string Narrative);

/// <summary>
/// One free agent's market as it stands. <paramref name="LiveOfferCount"/> is what is actually on the
/// table on the day being asked about, which is not the same as how many offers have ever been made.
/// </summary>
public sealed record NegotiationSummary(
    string NegotiationId,
    string PlayerId,
    string PlayerName,
    string State,
    int OpenedOnDay,
    int LiveOfferCount,
    int TotalOfferCount,
    int CounterofferCount,
    string? AcceptedOfferId,
    string? SignedContractId,
    IReadOnlyList<NegotiationEntryLine> History);

/// <summary>
/// One position on the free-agency board: what the team already has there, and the best unsigned
/// players available for it.
/// <para>
/// Columned by position against the team's own depth deliberately — a market a GM cannot read
/// against their own squad is a market they cannot play, and a flat list of the best free agents
/// answers a question nobody asked.
/// </para>
/// </summary>
public sealed record BoardPositionColumn(
    string Position,
    int OwnDepth,
    IReadOnlyList<BoardDepthLine> OwnPlayers,
    IReadOnlyList<BoardCandidateLine> BestAvailable);

/// <summary>One player the team already rosters at a position, best first.</summary>
public sealed record BoardDepthLine(string PlayerId, string FullName, int Overall, int ContractSeasonsRemaining);

/// <summary>
/// One available player in a position column, with where this team stands in their market.
/// <paramref name="AskingPrice"/> is <c>null</c> in a league that configures no salary range — an
/// open market gives a player no range to be placed inside, so they have no asking price.
/// </summary>
public sealed record BoardCandidateLine(
    string PlayerId,
    string FullName,
    int Overall,
    int Age,
    int SeasonsOfService,
    long? MinimumSalary,
    long? MaximumSalary,
    long? AskingPrice,
    string NegotiationState,
    int LiveOfferCount,
    bool HasOurOffer,
    string? OurOfferId,
    long? OurFirstSeasonCompensation,
    int? OurSeasonCount,
    long? CounterofferFirstSeasonCompensation,
    int? CounterofferSeasonCount,
    string? CounterofferNarrative);

/// <summary>
/// The whole board for one team on one day: every position column, plus the negotiations this team
/// is currently in so a GM can see what they are exposed to without walking the columns.
/// </summary>
public sealed record FreeAgencyBoardSummary(
    string TeamId,
    string TeamName,
    int Day,
    string ResolutionMode,
    int? OfferExpiryDays,
    IReadOnlyList<BoardPositionColumn> Columns,
    IReadOnlyList<NegotiationSummary> OurNegotiations);

/// <summary>
/// A market that has been resolved for real, with the league re-projected afterwards so every screen
/// reads the new state from one place. <paramref name="Signed"/> is false where the market resolved
/// on nobody, which is an outcome rather than a failure.
/// </summary>
public sealed record MarketResolutionSubmission(
    MarketAssessmentSummary Assessment,
    NegotiationSummary Negotiation,
    bool Signed,
    string? RouteName,
    int LedgerEntryCount,
    Leagues.LeagueOverview Overview);
