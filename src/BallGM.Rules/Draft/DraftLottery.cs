using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Draft;

/// <summary>
/// Turns a reverse-standings finish into a <see cref="DraftOrderSnapshot"/>, weighting the first
/// round by <see cref="DraftLotteryRules.Weights"/> when the league has one configured. This is the
/// producer <c>docs/domain-language.md</c> names as still owed to <see cref="DraftOrderSnapshot"/>:
/// until now every order fed to pick conveyance came from a fixture or a test.
/// <para>
/// Only round one is drawn. Every later round runs in the same worst-to-best order the standings
/// state, because a franchise's <em>original</em> draft position — before any pick has changed hands —
/// does not usually vary round to round in a real draft either; the trades and protections that move a
/// pick around are what <c>PickConveyanceEvaluator</c> already handles downstream of this order.
/// </para>
/// </summary>
public static class DraftLottery
{
    private const string NoDraftCode = "draft_lottery.no_draft";
    private const string EmptyStandingsCode = "draft_lottery.empty_standings";
    private const string PoolLargerThanLeagueCode = "draft_lottery.pool_larger_than_league";

    public static DomainOperationResult<DraftOrderSnapshot> Run(
        Season draftSeason,
        IReadOnlyList<FranchiseId> reverseStandingsOrder,
        DraftRules draftRules,
        DraftLotteryRules lotteryRules,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(reverseStandingsOrder);
        ArgumentNullException.ThrowIfNull(draftRules);
        ArgumentNullException.ThrowIfNull(lotteryRules);
        ArgumentNullException.ThrowIfNull(random);

        if (!draftRules.HasDraft)
        {
            return DomainOperationResult<DraftOrderSnapshot>.Failure(new DomainError(
                NoDraftCode,
                "This league holds no draft, so it has no draft order to build."));
        }

        if (reverseStandingsOrder.Count == 0)
        {
            return DomainOperationResult<DraftOrderSnapshot>.Failure(new DomainError(
                EmptyStandingsCode,
                $"No teams were supplied to seed the {draftSeason.Year} draft order."));
        }

        if (draftRules.LotteryEnabled && lotteryRules.Weights.Count > reverseStandingsOrder.Count)
        {
            return DomainOperationResult<DraftOrderSnapshot>.Failure(new DomainError(
                PoolLargerThanLeagueCode,
                $"The lottery pool states {lotteryRules.Weights.Count} weight(s) but the league has only {reverseStandingsOrder.Count} team(s)."));
        }

        var round1Order = draftRules.LotteryEnabled && lotteryRules.IsConfigured
            ? DrawPool(reverseStandingsOrder, lotteryRules.Weights, random)
            : reverseStandingsOrder;

        var slots = new List<DraftOrderSlot>();
        for (var round = 1; round <= draftRules.RoundCount; round++)
        {
            var order = round == 1 ? round1Order : reverseStandingsOrder;
            for (var index = 0; index < order.Count; index++)
            {
                slots.Add(new DraftOrderSlot(round, index + 1, order[index]));
            }
        }

        return DraftOrderSnapshot.Create(draftSeason, slots);
    }

    private static IReadOnlyList<FranchiseId> DrawPool(
        IReadOnlyList<FranchiseId> standingsOrder,
        IReadOnlyList<int> weights,
        IRandomSource random)
    {
        var poolSize = weights.Count;
        var remainingIds = standingsOrder.Take(poolSize).ToList();
        var remainingWeights = weights.ToList();
        var drawn = new List<FranchiseId>(poolSize);

        while (remainingIds.Count > 0)
        {
            var total = remainingWeights.Sum();
            var target = random.NextInt32(0, total);
            var cumulative = 0;
            var pickedIndex = remainingIds.Count - 1;

            for (var index = 0; index < remainingIds.Count; index++)
            {
                cumulative += remainingWeights[index];
                if (target < cumulative)
                {
                    pickedIndex = index;
                    break;
                }
            }

            drawn.Add(remainingIds[pickedIndex]);
            remainingIds.RemoveAt(pickedIndex);
            remainingWeights.RemoveAt(pickedIndex);
        }

        drawn.AddRange(standingsOrder.Skip(poolSize));
        return drawn;
    }
}
