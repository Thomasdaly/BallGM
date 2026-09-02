using BallGM.Domain.Common;
using BallGM.Domain.Players;

namespace BallGM.Domain.Draft;

/// <summary>
/// One entrant in a <see cref="DraftClass"/>: a basketball participant who has not yet been selected
/// into the league. Owned by the <see cref="DraftClass"/> that produced it rather than referenced by
/// identifier the way <see cref="Domain.Teams.Team"/> references a <see cref="Player"/> — a prospect
/// has no roster to belong to until draft day selects it, so there is nothing else for it to be owned
/// by yet.
/// <para>
/// <see cref="TrueRating"/> is the honest number. What a scout's report says about it —
/// <see cref="ScoutingRange"/> — is deliberately not carried here: this type states what is true, and
/// how much of that truth a given amount of scouting investment has revealed is a rules question,
/// answered fresh by <c>BallGM.Rules.Draft.ScoutingModel</c> rather than cached on the prospect.
/// </para>
/// </summary>
public sealed class Prospect
{
    private const string MissingBirthDateCode = "prospect.missing_birth_date";

    private Prospect(ProspectId id, string fullName, Position position, DateOnly birthDate, PlayerRating trueRating)
    {
        Id = id;
        FullName = fullName;
        Position = position;
        BirthDate = birthDate;
        TrueRating = trueRating;
    }

    public static DomainOperationResult<Prospect> Create(
        ProspectId id,
        string fullName,
        Position position,
        DateOnly birthDate,
        PlayerRating trueRating)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(trueRating);

        if (!Enum.IsDefined(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be a defined basketball position.");
        }

        if (birthDate == default)
        {
            return DomainOperationResult<Prospect>.Failure(new DomainError(
                MissingBirthDateCode,
                $"Prospect '{fullName}' has no birth date."));
        }

        return DomainOperationResult<Prospect>.Success(new Prospect(id, fullName, position, birthDate, trueRating));
    }

    public ProspectId Id { get; }

    public string FullName { get; }

    public Position Position { get; }

    public DateOnly BirthDate { get; }

    /// <summary>The real skill level. Not what any scout knows — see the type-level remarks.</summary>
    public PlayerRating TrueRating { get; }

    public int AgeOn(DateOnly asOf)
    {
        var age = asOf.Year - BirthDate.Year;
        return asOf < BirthDate.AddYears(age) ? age - 1 : age;
    }
}
