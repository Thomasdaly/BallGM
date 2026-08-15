using BallGM.Domain.Common;

namespace BallGM.Domain.Players;

/// <summary>
/// Aggregate root for a basketball participant's identity, position, rating, and health
/// status. Contract and career-history data arrive in later milestones; this is the
/// Milestone 1 foundation the roster (<see cref="Domain.Teams.Team"/>) references by identifier.
/// </summary>
public sealed class Player
{
    private const string AlreadyInjuredCode = "player.already_injured";
    private const string NotInjuredCode = "player.not_injured";

    private Player(PlayerId id, string fullName, Position position, PlayerRating rating, Injury? currentInjury)
    {
        Id = id;
        FullName = fullName;
        Position = position;
        Rating = rating;
        CurrentInjury = currentInjury;
    }

    public static DomainOperationResult<Player> Create(
        PlayerId id,
        string fullName,
        Position position,
        PlayerRating rating,
        Injury? currentInjury = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(rating);

        if (!Enum.IsDefined(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be a defined basketball position.");
        }

        return DomainOperationResult<Player>.Success(new Player(id, fullName, position, rating, currentInjury));
    }

    public PlayerId Id { get; }

    public string FullName { get; }

    public Position Position { get; }

    public PlayerRating Rating { get; }

    public Injury? CurrentInjury { get; private set; }

    public bool IsInjured => CurrentInjury is not null;

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
