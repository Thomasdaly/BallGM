using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;

namespace BallGM.Application.Leagues;

/// <summary>
/// The loaded aggregates and configuration one league needs, as handed to the Application layer
/// by an <see cref="ILeagueDataSource"/>. This is an input model, not a read model: it still
/// carries Domain aggregates, because mapping them to the presentation-facing
/// <see cref="LeagueOverview"/> is <see cref="GetLeagueOverviewQuery"/>'s job.
/// </summary>
/// <remarks>
/// <see cref="Players"/> can legitimately include people no team's roster references — a released
/// player whose guaranteed money is still on the books is exactly that case, and the cap sheet
/// needs their name to explain the dead-money line.
/// </remarks>
public sealed record LeagueSnapshot(
    League League,
    Season CurrentSeason,
    IReadOnlyCollection<Franchise> Franchises,
    IReadOnlyCollection<Team> Teams,
    IReadOnlyCollection<Player> Players,
    IReadOnlyCollection<Contract> Contracts,
    DraftAssetBook DraftAssets,
    TransactionLedger Ledger,
    LeagueConfiguration Configuration);

/// <summary>
/// The subset of a league's configured ruleset the Application layer needs, expressed in Domain
/// value objects. Deliberately not <c>BallGM.Rules.Configuration.LeagueRuleset</c>: Application
/// does not reference Rules, so whoever implements <see cref="ILeagueDataSource"/> owns the
/// mapping from the on-disk ruleset onto this shape.
/// <para>
/// A null threshold means the league does not have that line, and a <see cref="DraftRoundCount"/>
/// of zero means it holds no draft. Absence is carried all the way through rather than flattened to
/// a zero at this boundary, because a zero here would be indistinguishable from a configured zero
/// by the time it reached the rules.
/// </para>
/// </summary>
public sealed record LeagueConfiguration(
    string RulesetName,
    int RegularSeasonGameCount,
    RosterSizeLimits RosterLimits,
    Money? PayrollFloor,
    Money? SoftCap,
    Money? LuxuryTax,
    Money? FirstApron,
    Money? SecondApron,
    Money? HardCap,
    int DraftRoundCount,
    bool DraftLotteryEnabled,
    int TradableFutureDraftHorizon,
    int RetainedRoundNumber,
    int RetainedRoundInterval,
    int? SalaryMatchPercent,
    Money SalaryMatchAllowance,
    InjuredPlayerTradeEligibility InjuredPlayerTradeEligibility,
    bool SecondApronBlocksSalaryIncrease,
    NegotiationConfiguration Negotiation,
    SeasonScheduleConfiguration? Schedule = null,
    TieBreakSequence? StandingsTieBreaks = null,
    PostseasonConfiguration? Postseason = null,
    DraftClassConfiguration? DraftClass = null,
    IReadOnlyList<int>? DraftLotteryWeights = null)
{
    /// <summary>Whether this league configures any threshold at all.</summary>
    public bool IsUncapped =>
        PayrollFloor is null && SoftCap is null && LuxuryTax is null &&
        FirstApron is null && SecondApron is null && HardCap is null;

    /// <summary>Whether this league holds a draft. False means no franchise can hold a pick.</summary>
    public bool HasDraft => DraftRoundCount > 0;

    /// <summary>
    /// The phase lengths and opponent weighting, or the shortest playable calendar where the league
    /// states none. Absent means "a regular season and nothing either side of it", which is a real
    /// league rather than a missing configuration.
    /// </summary>
    public SeasonScheduleConfiguration ResolvedSchedule => Schedule ?? SeasonScheduleConfiguration.Minimal;

    /// <summary>The stated tie-break sequence, or <see cref="TieBreakSequence.None"/> where the league states none.</summary>
    public TieBreakSequence ResolvedTieBreaks => StandingsTieBreaks ?? TieBreakSequence.None;

    /// <summary>Whether this league holds a postseason at all.</summary>
    public bool HasPostseason => Postseason is not null;

    /// <summary>Whether this league procedurally generates its own draft classes.</summary>
    public bool GeneratesDraftClasses => DraftClass is not null;

    /// <summary>
    /// Whether this league states draft lottery odds. Independent of <see cref="DraftClass"/> — a
    /// league can run the lottery over classes a data pack supplies without generating its own.
    /// </summary>
    public bool HasDraftLotteryWeights => DraftLotteryWeights is { Count: > 0 };
}

/// <summary>
/// How this league procedurally builds its own draft classes: how many prospects, the true-rating
/// spread they are drawn from, the age they enter at, and how scouting narrows what a team knows
/// about one. Absent means this league generates no classes of its own — a draft-class playlist
/// (Milestone 10) or simply no draft — the same "absence is a real configuration" reading every other
/// optional section here uses. Grouped the way <see cref="NegotiationConfiguration"/> is: one section
/// of the ruleset file, and a GM setting up a league's draft asks about all of it at once.
/// <para>
/// The draft lottery's weighting table travels separately, as <see cref="LeagueConfiguration.DraftLotteryWeights"/>,
/// because it is independently optional: a league can run a weighted lottery over a draft class a data
/// pack supplies without configuring this generator at all.
/// </para>
/// </summary>
public sealed record DraftClassConfiguration(
    int ClassSize,
    int MinimumRating,
    int MaximumRating,
    int ProspectAgeYears,
    int ScoutingBaseConfidence,
    int ScoutingMaxRangeWidth,
    BandedScale ScoutingInvestmentConfidence)
{
    public bool HasScoutingModel => ScoutingMaxRangeWidth > 0;
}

/// <summary>
/// How long each phase of a season runs and how often each kind of opponent is played, in the shape
/// the Application layer carries it.
/// <para>
/// Note what is <em>not</em> here: which teams are in which conference and division. That is league
/// content and travels on the <c>League</c> aggregate inside <see cref="LeagueSnapshot"/>, so one
/// ruleset can serve two differently aligned leagues.
/// </para>
/// </summary>
public sealed record SeasonScheduleConfiguration(
    int PreseasonDays,
    int RegularSeasonDays,
    int OffseasonDays,
    int? GamesVersusDivisionOpponent,
    int? GamesVersusConferenceOpponent,
    int? GamesVersusOtherConferenceOpponent)
{
    /// <summary>A regular season and nothing either side of it.</summary>
    public static SeasonScheduleConfiguration Minimal { get; } = new(0, 1, 0, null, null, null);

    public bool HasOpponentWeighting =>
        GamesVersusDivisionOpponent is not null &&
        GamesVersusConferenceOpponent is not null &&
        GamesVersusOtherConferenceOpponent is not null;
}

/// <summary>
/// This league's postseason format. The whole record is absent in a league that holds no
/// postseason, which is a league rather than a misconfiguration — and one of the cases the client
/// has to be able to render as "there is none" instead of as a bracket of nought rounds.
/// </summary>
public sealed record PostseasonConfiguration(
    int PostseasonDays,
    int QualifyingTeamsPerConference,
    IReadOnlyList<int> SeriesLengths,
    string HomeCourtSequence,
    int? PlayoffEligibilityCutoffDay);

/// <summary>
/// What this league permits in a contract offer, and how a team may pay for it, in the shape the
/// Application layer carries it. Grouped rather than flattened into
/// <see cref="LeagueConfiguration"/> because it is one section of the ruleset file and reads as one
/// thing on screen — a GM asking "what can I offer here" is asking about all of it at once.
/// <para>
/// A configuration with nothing set is an open market: any team may offer any player any amount for
/// any term. It is emphatically not a league where nobody may sign — signing is a capability, and it
/// is the routes that gate it. The tables travel as <see cref="BandedScale"/> rather than as the
/// rules layer's own wrapper types for the usual reason: Application does not reference Rules.
/// </para>
/// </summary>
public sealed record NegotiationConfiguration(
    int? MaximumContractSeasons,
    int? MaximumIncumbentContractSeasons,
    int? MaximumAnnualEscalationPercent,
    int? MaximumAnnualDeescalationPercent,
    BandedScale CompensationCeilingTiers,
    BandedScale CompensationFloorScale,
    Money? StandardOverCapAllowance,
    CapThresholdKind? StandardOverCapAllowanceUnavailableAbove,
    bool AllowanceMaySplitAcrossPlayers,
    MarketResolutionMode MarketResolution,
    int? OfferExpiryDays,
    int? InSeasonSigningWindowOpensDay = null,
    int? InSeasonSigningWindowClosesDay = null,
    int? ShortTermContractDays = null)
{
    /// <summary>A league that constrains nothing about what may be offered or how it is paid for.</summary>
    public static NegotiationConfiguration OpenMarket { get; } = new(
        null,
        null,
        null,
        null,
        BandedScale.None,
        BandedScale.None,
        null,
        null,
        false,
        MarketResolutionMode.ResolutionPoint,
        null);

    public bool HasTermLimit => MaximumContractSeasons is not null;

    public bool HasCompensationCeiling => !CompensationCeilingTiers.IsEmpty;

    public bool HasCompensationFloor => !CompensationFloorScale.IsEmpty;

    public bool HasStandardOverCapAllowance => StandardOverCapAllowance is not null;

    /// <summary>Whether this league restricts which days of the season a signing may happen on.</summary>
    public bool HasInSeasonSigningWindow =>
        InSeasonSigningWindowOpensDay is not null && InSeasonSigningWindowClosesDay is not null;

    public bool HasShortTermContracts => ShortTermContractDays is not null;
}
