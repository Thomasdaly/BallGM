namespace BallGM.Infrastructure.Saves;

/// <summary>Serialization shape for one <c>Franchise</c>: identity that outlives any single season.</summary>
public sealed record FranchiseEnvelope(string FranchiseId, string Name);

/// <summary>
/// Serialization shape for one <c>Team</c>'s roster membership. <see cref="PlayerIds"/> is written in
/// a stable order (by identifier) rather than however the aggregate's internal set happens to
/// enumerate, so a save diffs cleanly and a round trip is not merely set-equal to its source.
/// </summary>
public sealed record TeamEnvelope(
    string TeamId,
    string FranchiseId,
    string Name,
    IReadOnlyList<string> PlayerIds);

/// <summary>
/// Serialization shape for one <c>Player</c>: identity, position, rating, and the two temporal facts
/// every negotiation rule keys off. <see cref="BirthDate"/> travels as <c>yyyy-MM-dd</c> rather than
/// a stored age, for the same reason <c>Player</c> itself carries a birth date rather than an age —
/// an age is only true until the calendar moves.
/// </summary>
public sealed record PlayerEnvelope(
    string PlayerId,
    string FullName,
    string Position,
    int Overall,
    string BirthDate,
    int SeasonsOfService,
    string? InjuryDescription);
