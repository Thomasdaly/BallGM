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
/// an age is only true until the calendar moves. The full sheet travels in the same shape a league
/// pack states it (<c>PlayerSheetCodec</c>); <c>PlayerRating.Overall</c> is derived and never stored, the same "re-derived, never stored"
/// reading the rest of this save format gives a value computed from state already on the envelope.
/// </summary>
public sealed record PlayerEnvelope(
    string PlayerId,
    string FullName,
    string Position,
    IReadOnlyDictionary<string, int?> Ratings,
    IReadOnlyDictionary<string, int?> Hidden,
    int Potential,
    Players.PlayerBodyEnvelope? Body,
    IReadOnlyDictionary<string, string> Positions,
    IReadOnlyList<string> Traits,
    string BirthDate,
    int SeasonsOfService,
    string? InjuryDescription,
    bool IsRetired = false,
    PlayerBiographyEnvelope? Biography = null,
    IReadOnlyDictionary<string, int?>? Tendencies = null,
    int? InjuryDaysRemaining = null);

/// <summary>Where a player came from: birthplace, prior programme and his draft, any of them unknown.</summary>
public sealed record PlayerBiographyEnvelope(string? Birthplace, string? PriorProgramme, int? DraftSeasonYear, int? DraftRound, int? DraftSelection);
