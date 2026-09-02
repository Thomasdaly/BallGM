using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// The weighted-draw odds for a league's draft lottery, worst-team-first. Entry <c>0</c> is the
/// weight of the worst team's ball in the draw, entry <c>1</c> the next-worst, and so on — a weighting
/// table in the ruleset, exactly like <see cref="CapThresholds"/> or the standings tie-break sequence,
/// never an algorithm's own compiled-in odds.
/// <para>
/// The whole pool named by <see cref="Weights"/> is drawn by weight, one slot at a time without
/// replacement, rather than only a top slice of it (the way a real league's lottery decides only its
/// top few picks and lets the rest fall in standings order). That is a simplification stated here
/// rather than discovered later: it needs one ruleset field instead of two, and it still rewards a
/// worse finish with better average odds. A league that wants only its worst four teams in the draw
/// states four weights; anyone finishing outside that count already picks in plain reverse-standings
/// order because <see cref="Weights"/> never reaches them.
/// </para>
/// </summary>
public sealed record DraftLotteryRules
{
    private const string NonPositiveWeightCode = "ruleset.non_positive_lottery_weight";

    private DraftLotteryRules(IReadOnlyList<int> weights) => Weights = weights;

    /// <summary>A league that states no lottery odds. Valid only where the draft has no lottery enabled.</summary>
    public static DraftLotteryRules None { get; } = new([]);

    public bool IsConfigured => Weights.Count > 0;

    public IReadOnlyList<int> Weights { get; }

    public static DomainOperationResult<DraftLotteryRules> Create(IEnumerable<int> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var list = weights.ToArray();
        if (list.Length == 0)
        {
            return DomainOperationResult<DraftLotteryRules>.Success(None);
        }

        var errors = list
            .Where(weight => weight <= 0)
            .Select(weight => new DomainError(
                NonPositiveWeightCode,
                $"Every draft lottery weight must be positive, but {weight} was configured."))
            .ToArray();

        return errors.Length > 0
            ? DomainOperationResult<DraftLotteryRules>.Failure(errors)
            : DomainOperationResult<DraftLotteryRules>.Success(new DraftLotteryRules(list));
    }
}
