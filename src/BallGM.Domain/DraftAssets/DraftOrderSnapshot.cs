using BallGM.Domain.Common;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.DraftAssets;

/// <summary>One selection in a draft order: which round, at what number, belonging to which franchise's finish.</summary>
public sealed record DraftOrderSlot(int Round, int SelectionNumber, FranchiseId OriginalFranchiseId);

/// <summary>
/// Where every franchise's selection lands in one draft, supplied from outside rather than
/// generated here. This is the seam that keeps conveyance testable: a protection test needs a draft
/// order, and if the only way to get one were to run a lottery, every protection test would become
/// a seeded-simulation test. The lottery arrives at Milestone 8 and will produce one of these; until
/// then a fixture or a test writes one directly.
/// </summary>
public sealed class DraftOrderSnapshot
{
    private const string EmptyOrderCode = "draft_order.empty";
    private const string InvalidRoundCode = "draft_order.invalid_round";
    private const string DuplicateFranchiseCode = "draft_order.duplicate_franchise_in_round";
    private const string NonContiguousCode = "draft_order.selections_not_contiguous";

    private readonly Dictionary<(int Round, string FranchiseId), int> _selectionsByFranchise;

    private DraftOrderSnapshot(Season draftSeason, IReadOnlyList<DraftOrderSlot> slots)
    {
        DraftSeason = draftSeason;
        Slots = slots;
        _selectionsByFranchise = slots.ToDictionary(
            slot => (slot.Round, slot.OriginalFranchiseId.Value),
            slot => slot.SelectionNumber);
    }

    public Season DraftSeason { get; }

    public IReadOnlyList<DraftOrderSlot> Slots { get; }

    /// <summary>
    /// Builds a draft order, refusing anything a real draft could not be. Selection numbers must run
    /// 1..n contiguously within each round and each franchise appears once per round — an order with
    /// two franchises at selection 3 would make a protection's answer depend on iteration order.
    /// </summary>
    public static DomainOperationResult<DraftOrderSnapshot> Create(Season draftSeason, IEnumerable<DraftOrderSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(slots);

        var slotList = slots.ToArray();
        if (slotList.Any(slot => slot is null))
        {
            throw new ArgumentException("A draft order cannot contain null slots.", nameof(slots));
        }

        if (slotList.Length == 0)
        {
            return DomainOperationResult<DraftOrderSnapshot>.Failure(
                new DomainError(EmptyOrderCode, $"The {draftSeason.Year} draft order contains no selections."));
        }

        var errors = new List<DomainError>();

        foreach (var round in slotList.GroupBy(slot => slot.Round).OrderBy(group => group.Key))
        {
            if (round.Key < 1)
            {
                errors.Add(new DomainError(
                    InvalidRoundCode,
                    $"The {draftSeason.Year} draft order contains round {round.Key}; rounds start at 1."));
                continue;
            }

            var franchises = round.Select(slot => slot.OriginalFranchiseId.Value).ToArray();
            if (franchises.Length != franchises.Distinct(StringComparer.Ordinal).Count())
            {
                errors.Add(new DomainError(
                    DuplicateFranchiseCode,
                    $"Round {round.Key} of the {draftSeason.Year} draft order gives a franchise more than one selection."));
            }

            var numbers = round.Select(slot => slot.SelectionNumber).OrderBy(number => number).ToArray();
            if (numbers.Where((number, index) => number != index + 1).Any())
            {
                errors.Add(new DomainError(
                    NonContiguousCode,
                    $"Round {round.Key} of the {draftSeason.Year} draft order must number its selections 1 to {numbers.Length} without gaps or repeats."));
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<DraftOrderSnapshot>.Failure(errors.ToArray());
        }

        var ordered = slotList
            .OrderBy(slot => slot.Round)
            .ThenBy(slot => slot.SelectionNumber)
            .ToList();

        return DomainOperationResult<DraftOrderSnapshot>.Success(new DraftOrderSnapshot(draftSeason, ordered));
    }

    /// <summary>
    /// Where a franchise's own selection lands in a round, or <c>null</c> if this order does not
    /// cover it. Keyed on the <em>original</em> franchise, never the current owner: a pick's draft
    /// position is decided by whose season produced it, not by who happens to hold the asset.
    /// </summary>
    public int? SelectionFor(int round, FranchiseId originalFranchiseId)
    {
        ArgumentNullException.ThrowIfNull(originalFranchiseId);

        return _selectionsByFranchise.TryGetValue((round, originalFranchiseId.Value), out var selection)
            ? selection
            : null;
    }

    public int SelectionCountInRound(int round) => Slots.Count(slot => slot.Round == round);
}
