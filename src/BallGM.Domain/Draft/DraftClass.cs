using BallGM.Domain.Common;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.Draft;

/// <summary>
/// One season's crop of draft-eligible <see cref="Prospect"/>s. An aggregate root in its own right —
/// not embedded in <see cref="League"/> — because a class exists and can be scouted before any of it
/// is drafted, and outlives the draft day that consumes it (a save wants to keep last year's class on
/// record, per "records and history").
/// </summary>
public sealed class DraftClass
{
    private const string EmptyClassCode = "draft_class.empty";
    private const string DuplicateProspectCode = "draft_class.duplicate_prospect";

    private DraftClass(DraftClassId id, Season draftSeason, IReadOnlyList<Prospect> prospects)
    {
        Id = id;
        DraftSeason = draftSeason;
        Prospects = prospects;
    }

    public static DomainOperationResult<DraftClass> Create(DraftClassId id, Season draftSeason, IEnumerable<Prospect> prospects)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(prospects);

        var list = prospects.ToArray();
        if (list.Any(prospect => prospect is null))
        {
            throw new ArgumentException("A draft class cannot contain a null prospect.", nameof(prospects));
        }

        if (list.Length == 0)
        {
            return DomainOperationResult<DraftClass>.Failure(new DomainError(
                EmptyClassCode,
                $"The {draftSeason.Year} draft class contains no prospects."));
        }

        var duplicate = list
            .GroupBy(prospect => prospect.Id.Value)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            return DomainOperationResult<DraftClass>.Failure(new DomainError(
                DuplicateProspectCode,
                $"The {draftSeason.Year} draft class lists prospect '{duplicate.Key}' more than once."));
        }

        return DomainOperationResult<DraftClass>.Success(new DraftClass(id, draftSeason, list));
    }

    public DraftClassId Id { get; }

    public Season DraftSeason { get; }

    public IReadOnlyList<Prospect> Prospects { get; }

    public Prospect? Find(ProspectId prospectId)
    {
        ArgumentNullException.ThrowIfNull(prospectId);
        return Prospects.FirstOrDefault(prospect => prospect.Id.Value == prospectId.Value);
    }
}
