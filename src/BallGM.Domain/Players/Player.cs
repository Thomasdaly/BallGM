using BallGM.Domain.Common;

namespace BallGM.Domain.Players;

/// <summary>
/// Aggregate root for a basketball participant's identity, position, rating, health status, and
/// the two temporal facts every negotiation rule keys off: when they were born and how long they
/// have been playing. Career history proper and biography arrive in later milestones; this is the
/// foundation the roster (<see cref="Domain.Teams.Team"/>) references by identifier.
/// <para>
/// Birth date rather than a stored age, because an age is only true until the calendar moves: every
/// tier table in the negotiation rules keys off service, and preference keys off age, so both have
/// to stay correct as seasons advance rather than going quietly stale in a field.
/// </para>
/// </summary>
public sealed class Player
{
    private const string AlreadyInjuredCode = "player.already_injured";
    private const string NotInjuredCode = "player.not_injured";
    private const string MissingBirthDateCode = "player.missing_birth_date";
    private const string NegativeServiceCode = "player.negative_seasons_of_service";
    private const string AlreadyRetiredCode = "player.already_retired";

    private Player(
        PlayerId id,
        string fullName,
        Position position,
        PlayerRating rating,
        DateOnly birthDate,
        int seasonsOfService,
        Injury? currentInjury,
        PlayerBiography biography,
        bool isRetired)
    {
        Id = id;
        FullName = fullName;
        Position = position;
        Rating = rating;
        BirthDate = birthDate;
        SeasonsOfService = seasonsOfService;
        CurrentInjury = currentInjury;
        Biography = biography;
        IsRetired = isRetired;
    }

    /// <summary>
    /// Creates a player. A null identifier or an undefined position is a caller bug and throws; a
    /// missing birth date or a negative service figure is something a data pack can legitimately
    /// contain, so it comes back as a structured failure.
    /// </summary>
    public static DomainOperationResult<Player> Create(
        PlayerId id,
        string fullName,
        Position position,
        PlayerRating rating,
        DateOnly birthDate,
        int seasonsOfService,
        Injury? currentInjury = null,
        PlayerBiography? biography = null,
        bool isRetired = false)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(rating);

        if (!Enum.IsDefined(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be a defined basketball position.");
        }

        var errors = new List<DomainError>();

        if (birthDate == default)
        {
            errors.Add(new DomainError(
                MissingBirthDateCode,
                $"Player '{fullName}' has no birth date. Age drives contract eligibility, so it cannot be left out."));
        }

        if (seasonsOfService < 0)
        {
            errors.Add(new DomainError(
                NegativeServiceCode,
                $"Player '{fullName}' has {seasonsOfService} seasons of service, which cannot be negative."));
        }

        return errors.Count > 0
            ? DomainOperationResult<Player>.Failure(errors.ToArray())
            : DomainOperationResult<Player>.Success(
                new Player(
                    id,
                    fullName,
                    position,
                    rating,
                    birthDate,
                    seasonsOfService,
                    currentInjury,
                    biography ?? PlayerBiography.Unknown,
                    isRetired));
    }

    public PlayerId Id { get; }

    public string FullName { get; }

    public Position Position { get; }

    public PlayerRating Rating { get; private set; }

    public DateOnly BirthDate { get; }

    /// <summary>Where this player came from, and which draft — if any — brought them in.</summary>
    public PlayerBiography Biography { get; }

    /// <summary>Whether this player has retired. A retired player is not removed — see <see cref="Retire"/>.</summary>
    public bool IsRetired { get; private set; }

    /// <summary>
    /// Completed seasons on a roster. The key every compensation tier table reads: a league whose
    /// minimum salary does not vary by service is a league where every veteran signs for the rookie
    /// minimum.
    /// </summary>
    public int SeasonsOfService { get; private set; }

    public Injury? CurrentInjury { get; private set; }

    public bool IsInjured => CurrentInjury is not null;

    /// <summary>
    /// Age in completed years on <paramref name="asOf"/>. Takes the date rather than reading a clock,
    /// because a player's age has to be reproducible from a save: the same league re-opened next
    /// week must not quietly age everyone in it.
    /// </summary>
    public int AgeOn(DateOnly asOf)
    {
        var age = asOf.Year - BirthDate.Year;
        return asOf < BirthDate.AddYears(age) ? age - 1 : age;
    }

    /// <summary>
    /// Credits one completed season on a roster — the only way <see cref="SeasonsOfService"/> moves.
    /// Called once per player, at season's end, for everyone a team's roster named through it; a
    /// player who never earns it never ages off the rookie tier of any compensation scale that keys
    /// off service. Always succeeds; returns a result for consistency with every other aggregate
    /// mutator here, and so a future invariant can be added without a signature change.
    /// </summary>
    public DomainOperationResult CompleteSeasonOfService()
    {
        SeasonsOfService++;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Replaces this player's rating — the only way <see cref="Rating"/> moves. Called once per player
    /// per season by <c>BallGM.Rules.Development.PlayerDevelopmentModel</c>, which decides the new
    /// value; this method only ever applies it, the same division of labour every other rule-driven
    /// mutation in this codebase keeps between the aggregate and the rules layer that decided it.
    /// Always succeeds; returns a result for consistency with the rest of this aggregate's mutators.
    /// </summary>
    public DomainOperationResult Develop(PlayerRating newRating)
    {
        ArgumentNullException.ThrowIfNull(newRating);
        Rating = newRating;
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Ends this player's playing career. A retired player is not removed from the league — their
    /// record and their career history stay exactly as reachable as an active player's — so this only
    /// flips the flag rather than tearing down any reference to them.
    /// </summary>
    public DomainOperationResult Retire()
    {
        if (IsRetired)
        {
            return DomainOperationResult.Failure(
                new DomainError(AlreadyRetiredCode, $"Player '{Id.Value}' has already retired."));
        }

        IsRetired = true;
        return DomainOperationResult.Success;
    }

    public DomainOperationResult MarkInjured(Injury injury)
    {
        ArgumentNullException.ThrowIfNull(injury);

        if (IsInjured)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    AlreadyInjuredCode,
                    $"Player '{Id.Value}' is already carrying an injury and must be cleared before a new one is recorded."));
        }

        CurrentInjury = injury;
        return DomainOperationResult.Success;
    }

    public DomainOperationResult ClearInjury()
    {
        if (!IsInjured)
        {
            return DomainOperationResult.Failure(
                new DomainError(
                    NotInjuredCode,
                    $"Player '{Id.Value}' has no injury to clear."));
        }

        CurrentInjury = null;
        return DomainOperationResult.Success;
    }
}
