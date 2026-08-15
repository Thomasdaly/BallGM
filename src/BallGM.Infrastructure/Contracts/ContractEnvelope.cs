namespace BallGM.Infrastructure.Contracts;

/// <summary>
/// Flat, primitive-only serialization shape for one contract — the save and data-pack surface for
/// everything Milestone 3 added. Versioned from the first commit on purpose: contracts are the part
/// of a save a player would lose a career to, so a schema change has to be a visible, migratable
/// event rather than a silent shape drift.
/// <para>
/// Carries no validation of its own. <see cref="ContractSerializer"/> is the trust boundary: it
/// maps this DTO onto <see cref="Domain.Contracts.Contract"/>, where the invariants live.
/// </para>
/// </summary>
public sealed record ContractEnvelope(
    int SchemaVersion,
    string ContractId,
    string TeamId,
    string PlayerId,
    int? TerminatedFromSeasonYear,
    IReadOnlyList<ContractSeasonTermEnvelope> Seasons)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// One season of a serialized contract. <see cref="Option"/> and <see cref="OptionStatus"/> are
/// stored as names rather than numbers so a save stays readable — and stays valid — if the
/// underlying enums gain members in a later milestone.
/// </summary>
public sealed record ContractSeasonTermEnvelope(
    int SeasonYear,
    long Compensation,
    long GuaranteedAmount,
    string Option,
    string OptionStatus);
