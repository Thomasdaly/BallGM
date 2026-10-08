using BallGM.Infrastructure.Careers;

namespace BallGM.Infrastructure.Saves;

/// <summary>
/// The whole of a played league, composed from one envelope per concept rather than one flat shape.
/// Replaces the placeholder that carried only a league name and a season year.
/// <para>
/// <b>Composition by embedding already-serialized text, not by nesting typed envelopes.</b>
/// <see cref="RulesetJson"/>, <see cref="Contracts"/>, <see cref="DraftAssets"/>,
/// <see cref="Season"/>, and <see cref="Negotiations"/> are each exactly what
/// <c>LeagueRulesetSerializer</c>, <c>ContractSerializer</c>, <c>DraftAssetSerializer</c>,
/// <c>SeasonSerializer</c>, and <c>NegotiationSerializer</c> already produce and read on their own —
/// this envelope carries their output as text rather than referencing their DTO types directly, so
/// this type never has to change shape when one of theirs does, and each keeps the schema version it
/// already had before this milestone. That is a stricter reading of "each concept keeps its own
/// version" than nesting their envelope objects would give: nesting still couples this type to
/// theirs, and a JSON string does not.
/// </para>
/// <para>
/// <see cref="League"/>, <see cref="Franchises"/>, <see cref="Teams"/>, <see cref="Players"/>, and
/// <see cref="Ledger"/> are new — nothing serialized a league's people before this milestone. They
/// are plain nested DTOs sharing this envelope's <see cref="SchemaVersion"/> rather than carrying one
/// each: unlike the five concepts above, none of them is ever read or written except as part of a
/// whole save, so a version number that could move independently would be a version number nothing
/// would ever move independently.
/// </para>
/// </summary>
public sealed record SaveGameEnvelope(
    int SchemaVersion,
    string RulesetJson,
    int CurrentSeasonYear,
    LeagueEnvelope League,
    IReadOnlyList<FranchiseEnvelope> Franchises,
    IReadOnlyList<TeamEnvelope> Teams,
    IReadOnlyList<PlayerEnvelope> Players,
    IReadOnlyList<string> Contracts,
    string DraftAssets,
    TransactionLedgerEnvelope Ledger,
    string? Season,
    IReadOnlyList<string> Negotiations,
    IReadOnlyList<PlayerCareerEnvelope>? CareerHistory,
    IReadOnlyList<string?>? TaxRepeaterTeams)
{
    /// <summary>
    /// Moved 1 → 2 when <c>PlayerEnvelope</c> traded its single <c>Overall</c> field for the five
    /// <c>PlayerRating</c> attributes it is now derived from. A version-1 save cannot state a
    /// player's attribute split, and rather than inventing one on load (four of five attributes
    /// guessed at, silently), <see cref="SaveGameSerializer"/> refuses a mismatched version outright —
    /// no production save has existed before this version moved, so there is nothing to migrate.
    /// <para>
    /// Moved 2 → 3 when the save started carrying <see cref="CareerHistory"/> and
    /// <see cref="TaxRepeaterTeams"/>. Both live on <c>LeagueSnapshot</c> as init properties and were
    /// silently dropped by every version-2 save, so loading one wiped every concluded season from
    /// every player's career and reset every repeater taxpayer to a first-time one. A version-2 save
    /// cannot say what it lost, so it is refused rather than loaded short.
    /// </para>
    /// </summary>
    public const int CurrentSchemaVersion = 3;
}
