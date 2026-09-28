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
/// <see cref="Logo"/> is an optional image path relative to the pack file (png or jpg).
/// </summary>
public sealed record LeaguePackTeamEnvelope(
    string? Key,
    string? FranchiseName,
    string? Name,
    string? Logo = null,
    bool TaxRepeater = false);

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
    LeaguePackRatingEnvelope? Ratings,
    string? Injury,
    LeaguePackContractEnvelope? Contract,
    string? Portrait = null,
    IReadOnlyList<LeaguePackCareerSeasonEnvelope>? Career = null);

/// <summary>
/// One past season of a player's career, as totals, for the profile's history table. Presentation
/// content: no rule reads it. <see cref="Team"/> is free text (a team from before the pack's own
/// league existed need not be one of its teams).
/// </summary>
public sealed record LeaguePackCareerSeasonEnvelope(
    string? Season,
    string? Team,
    int? GamesPlayed,
    int? Minutes,
    int? Points,
    int? Rebounds,
    int? Assists,
    IReadOnlyDictionary<string, double>? Metrics = null);

public sealed record LeaguePackRatingEnvelope(
    int? Height,
    int? Speed,
    int? Strength,
    int? Passing,
    int? LateralQuickness);

/// <summary>
/// A contract as consecutive season salaries starting with the pack's season, in the currency's
/// smallest units. Every season is fully guaranteed except an undecided option on the final one,
/// which — as everywhere else — is not money owed until somebody takes it up.
/// </summary>
public sealed record LeaguePackContractEnvelope(
    IReadOnlyList<long>? Salaries,
    string? FinalSeasonOption);
