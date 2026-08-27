using BallGM.Infrastructure.Contracts;

namespace BallGM.Infrastructure.Negotiations;

/// <summary>
/// Flat, primitive-only serialization shape for one in-flight negotiation — the save surface for
/// Milestone 6b. Versioned from its first commit for the same reason contracts are: a free agency
/// half-finished when a player closes the game is a market they would lose to a silent shape drift.
/// <para>
/// Its own <see cref="CurrentSchemaVersion"/>, independent of the ruleset's and of
/// <c>LeagueSaveEnvelope</c>'s. A negotiation and a ruleset change for different reasons and at
/// different times, and one version number covering both would force a migration on everyone every
/// time either moved.
/// </para>
/// <para>
/// Carries no validation of its own. <see cref="NegotiationSerializer"/> is the trust boundary: it
/// replays this history through <see cref="Domain.Negotiations.Negotiation"/>'s own methods, so a
/// save claiming a sequence the aggregate would refuse is refused on load rather than loaded into a
/// state no live negotiation could ever reach.
/// </para>
/// </summary>
public sealed record NegotiationEnvelope(
    int SchemaVersion,
    string NegotiationId,
    string PlayerId,
    int OpenedOnDay,
    string State,
    string? AcceptedOfferId,
    string? SignedContractId,
    IReadOnlyList<NegotiationEntryEnvelope> History)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// One line of a serialized history. <see cref="Kind"/> and <see cref="Author"/> are stored as names
/// rather than numbers so a save stays readable — and stays valid — if the underlying enums gain
/// members in a later milestone.
/// </summary>
public sealed record NegotiationEntryEnvelope(
    int Sequence,
    string Kind,
    int Day,
    string Author,
    string? TeamId,
    string? InResponseToOfferId,
    OfferEnvelope? Offer,
    string Narrative);

/// <summary>
/// One offer inside a negotiation's history. Reuses <see cref="ContractSeasonTermEnvelope"/> rather
/// than declaring a near-identical shape: an offer's seasons and a contract's seasons are the same
/// thing in the domain, and two serialization shapes for one concept is two places for a guarantee
/// to go missing.
/// </summary>
public sealed record OfferEnvelope(
    string OfferId,
    string TeamId,
    string PlayerId,
    IReadOnlyList<ContractSeasonTermEnvelope> Seasons);
