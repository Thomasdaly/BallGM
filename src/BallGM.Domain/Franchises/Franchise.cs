using BallGM.Domain.Common;

namespace BallGM.Domain.Franchises;

/// <summary>
/// Aggregate root for a persistent organisation identity that outlives any single season.
/// <see cref="Domain.Teams.Team"/> references a franchise by identifier rather than embedding it.
/// </summary>
public sealed class Franchise
{
    private Franchise(FranchiseId id, string name)
    {
        Id = id;
        Name = name;
    }

    public static DomainOperationResult<Franchise> Create(FranchiseId id, string name)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return DomainOperationResult<Franchise>.Success(new Franchise(id, name));
    }

    public FranchiseId Id { get; }

    public string Name { get; }
}
