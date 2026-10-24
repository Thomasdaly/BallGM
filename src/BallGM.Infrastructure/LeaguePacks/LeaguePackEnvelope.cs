namespace BallGM.Infrastructure.LeaguePacks;

/// <summary>
/// The on-disk shape of a league pack: the league content — franchises, teams, alignment, players,
/// and the contracts they are signed to — that <see cref="LeaguePackDataSource"/> turns into a
/// <see cref="BallGM.Application.Leagues.LeagueSnapshot"/>. The rules the league plays under stay in
/// their own ruleset file, referenced by path, so a pack and a ruleset can be swapped independently.
/// <para>
/// A save/mod DTO, deliberately separate from the domain types it describes. Everything here is
/// untrusted input: nullable where absence is meaningful, and validated by the data source before
/// any aggregate is built.
/// </para>
/// </summary>
public sealed record LeaguePackEnvelope(
    int SchemaVersion,
    string? Name,
    int? Season,
    string? RulesetFile,
    IReadOnlyList<LeaguePackConferenceEnvelope>? Conferences,
    IReadOnlyList<LeaguePackTeamEnvelope>? Teams,
    IReadOnlyList<LeaguePackPlayerEnvelope>? Players,
    IReadOnlyList<LeaguePackPickTradeEnvelope>? PickTrades = null);

/// <summary>
/// One pick that is not simply held by the franchise it originally belonged to. The pick is named by
/// <see cref="Draft"/>, <see cref="Round"/>, and the team key it <see cref="Original"/>ly belonged
/// to; exactly one of three statements follows:
/// <list type="bullet">
/// <item><see cref="Owner"/>: the pick was traded outright and that team controls it.</item>
/// <item><see cref="OwedTo"/>: the pick's controller owes it to that team, protected through the
/// top <see cref="ProtectedTop"/> selections draft by draft (empty: unprotected), ending in
/// <see cref="Fallback"/> (<c>ConveysUnprotected</c>, <c>ConvertsToRound</c> with
/// <see cref="FallbackRound"/>, or <c>Extinguishes</c>).</item>
/// <item><see cref="SwapHolder"/>: that team may swap its own pick in the same draft and round for
/// this one.</item>
/// </list>
/// Outright transfers apply first, so an obligation or swap is written by whoever controls the pick
/// once they have. <see cref="Note"/> is free text for the ledger line.
/// </summary>
public sealed record LeaguePackPickTradeEnvelope(
    int? Draft,
    int? Round,
    string? Original,
    string? Owner = null,
    string? OwedTo = null,
    IReadOnlyList<int>? ProtectedTop = null,
    string? Fallback = null,
    int? FallbackRound = null,
    string? SwapHolder = null,
    string? Note = null);

public sealed record LeaguePackConferenceEnvelope(
    string? Name,
    IReadOnlyList<LeaguePackDivisionEnvelope>? Divisions);

public sealed record LeaguePackDivisionEnvelope(
    string? Name,
    IReadOnlyList<string>? Teams);

/// <summary>
/// One team, keyed by a pack-local string. The key only links players and divisions to the team
/// inside this file; the loaded league mints its own identifiers, like every other load path.
/// <see cref="Logo"/> is an optional image path relative to the pack file (png or jpg), and
/// <see cref="Colours"/> the team's optional colours (schema version 2 onwards).
/// </summary>
public sealed record LeaguePackTeamEnvelope(
    string? Key,
    string? FranchiseName,
    string? Name,
    string? Logo = null,
    bool TaxRepeater = false,
    LeaguePackColoursEnvelope? Colours = null);

/// <summary>
/// A team's colours as <c>#RRGGBB</c> hex: <see cref="Primary"/> is required when the block is
/// stated, <see cref="Secondary"/> optional. Presentation only — a screen draws them, no rule reads
/// them.
/// </summary>
public sealed record LeaguePackColoursEnvelope(
    string? Primary,
    string? Secondary = null);

/// <summary>
/// One player. <see cref="Team"/> is a team key, or absent for a free agent — the same "on no roster
/// and under no contract" definition the fixture league uses, so there is no separate pool.
/// <see cref="Portrait"/> is an optional image path relative to the pack file (png or jpg), and
/// <see cref="Career"/> an optional list of past seasons.
/// </summary>
public sealed record LeaguePackPlayerEnvelope(
    string? Name,
    string? Position,
    DateOnly? BirthDate,
    int? SeasonsOfService,
    string? Team,
    IReadOnlyDictionary<string, int?>? Ratings,
    string? Injury,
    LeaguePackContractEnvelope? Contract,
    string? Portrait = null,
    IReadOnlyList<Careers.CareerSeasonEnvelope>? Career = null,
    IReadOnlyDictionary<string, int?>? Hidden = null,
    int? Potential = null,
    Players.PlayerBodyEnvelope? Body = null,
    IReadOnlyDictionary<string, string>? Positions = null,
    IReadOnlyList<string>? Traits = null,
    IReadOnlyDictionary<string, int?>? Tendencies = null);

/// <summary>
/// A contract as consecutive season salaries starting with the pack's season, in the currency's
/// smallest units. Every season is fully guaranteed except an undecided option, which — as everywhere
/// else — is not money owed until somebody takes it up.
/// <para>
/// Schema version 4 adds the contract's history and shape, each optional:
/// <see cref="Options"/> states an option on any season (<c>Player</c>, <c>Team</c>, or null), one per
/// salary, in place of <see cref="FinalSeasonOption"/> — a rookie deal's options on its third and
/// fourth seasons; <see cref="Guaranteed"/> each season's guaranteed amount, one per salary, for a
/// partly or non-guaranteed season; <see cref="SignedSeason"/> the season the deal began, when that
/// was before the pack's season — the seasons already played are kept as history at the first stated
/// salary, so the rules can see a deal signed this summer, a rookie deal, and how long it has run;
/// and <see cref="TeamSince"/> the season the player joined the team, when earlier than the deal,
/// so a team's years with its own player (what incumbent rights read) survive the load.
/// </para>
/// </summary>
public sealed record LeaguePackContractEnvelope(
    IReadOnlyList<long>? Salaries,
    string? FinalSeasonOption,
    IReadOnlyList<string?>? Options = null,
    IReadOnlyList<long?>? Guaranteed = null,
    int? SignedSeason = null,
    int? TeamSince = null);
