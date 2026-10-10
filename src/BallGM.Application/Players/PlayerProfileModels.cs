using BallGM.Application.Leagues;
using BallGM.Domain.Seasons;

namespace BallGM.Application.Players;

/// <summary>
/// Everything the player profile screen shows about one player: who they are, how the ratings
/// break down, what they are paid, how they have played lately, and what their career has been.
/// A read model like <c>LeagueOverview</c> — flattened and presentation-facing, never a Domain
/// aggregate or entity. The one Domain type it carries is <c>CountingStats</c>, an immutable value
/// with no identity: mirroring it field for field here would be exactly the kind of copy that once
/// dropped half the box score on its way to the screen.
/// </summary>
public sealed record PlayerProfileSummary(
    string PlayerId,
    string FullName,
    string Position,
    int Age,
    DateOnly BirthDate,
    int SeasonsOfService,
    int Overall,
    AttributeSheet Attributes,
    string? TeamName,
    string? PortraitPath,
    bool IsInjured,
    string? InjuryDescription,
    IReadOnlyList<ContractSeasonLine> Contract,
    CareerSeasonLine? CurrentSeason,
    IReadOnlyList<RecentGameLine> RecentGames,
    IReadOnlyList<CareerSeasonLine> Career,
    SeasonDetailLine? SeasonDetail = null,
    ContractDetailLine? ContractDetail = null,
    IReadOnlyList<RecentGameLine>? GameLog = null,
    ShotZones? LeagueZones = null,
    IReadOnlyList<Seasons.StatPercentile>? Percentiles = null,
    LeaderQualifiersConfiguration? LeaderQualifiers = null);

/// <summary>
/// Everything counted for a player this season, plus the team and opponent totals from the same
/// games that the rate stats (assist %, rebound %) need. Totals only; <see cref="StatFormulas"/>
/// derives the rest.
/// </summary>
public sealed record SeasonDetailLine(
    CountingStats Totals,
    CountingStats Team,
    CountingStats Opponent,
    int DoubleDoubles,
    int TripleDoubles,
    RatingAverage? AverageRating = null);

/// <summary>
/// A contract in depth: every remaining season with its guarantee and option, the player's place
/// between his minimum and maximum, and when he reaches free agency.
/// </summary>
public sealed record ContractDetailLine(
    long? SoftCap,
    long? MaximumSalary,
    int? MaximumPercentOfCap,
    long? MinimumSalary,
    long TotalValue,
    long GuaranteedValue,
    long AverageAnnualValue,
    int? FreeAgentYear,
    IReadOnlyList<ContractYearLine> Years);

/// <summary>One season of a contract. <see cref="Option"/> is "Player", "Team", or null.</summary>
public sealed record ContractYearLine(int Season, long Salary, long Guaranteed, string? Option, double? ShareOfCap);

/// <summary>
/// A player's scouting sheet as a screen shows it: the visible attributes group by group in sheet
/// order, by their stable keys (the pack and save keys; a screen words them); potential;
/// measurements; how at home he is at each position; his traits by key; and the one-line personality
/// the hidden layer reads as. Hidden attribute values never reach the screen.
/// </summary>
public sealed record AttributeSheet(
    IReadOnlyList<AttributeGroupLine> Groups,
    int PotentialAbility,
    BodyLine? Body,
    IReadOnlyList<PositionFamiliarityLine> Positions,
    IReadOnlyList<string> Traits,
    string Personality);

public sealed record AttributeGroupLine(string Group, IReadOnlyList<AttributeLine> Attributes);

/// <summary>One attribute: its stable key (the enum name) and its value.</summary>
public sealed record AttributeLine(string Key, int Value);

public sealed record BodyLine(int HeightCm, int WingspanCm, int WeightKg);

public sealed record PositionFamiliarityLine(string Position, string Familiarity);

/// <summary>One season of a contract, in smallest money units; an option season is not yet owed.</summary>
public sealed record ContractSeasonLine(int Season, long Salary, bool IsOption);

/// <summary>One recent game from this player's side of the box score.</summary>
public sealed record RecentGameLine(
    int Day,
    string Date,
    string Opponent,
    bool IsHome,
    bool Won,
    int TeamPoints,
    int OpponentPoints,
    CountingStats Stats);

/// <summary>
/// One season of a career, as totals. <see cref="Season"/> is a label ("2025-26") because career
/// rows arrive from two places — a data pack's stated history and seasons concluded in play — and a
/// label is the one thing both can state honestly. <see cref="Metrics"/> carries any further
/// statistics a data source states for that season, keyed by stat code ("USG_PCT", "NET_RATING",
/// "DEFLECTIONS", ...) — open-ended so a pack can say more than the engine itself counts.
/// <see cref="IsPostseason"/> marks a playoff line: a season concludes into a regular-season row and,
/// for a player who reached the postseason, a separate playoff row, never one mixed total.
/// </summary>
public sealed record CareerSeasonLine(
    string Season,
    string? TeamName,
    int GamesPlayed,
    int Minutes,
    int Points,
    int Rebounds,
    int Assists,
    bool IsCurrent = false,
    IReadOnlyDictionary<string, double>? Metrics = null,
    bool IsPostseason = false);
