using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>The counting stat one award ranks its candidates by — the same four <c>PlayerStatLine</c> already tracks.</summary>
public enum AwardStatBasis
{
    TotalPoints = 1,
    TotalRebounds = 2,
    TotalAssists = 3,
    TotalMinutes = 4,
}

/// <summary>
/// One award this league hands out: its code, its display name, and the stat it ranks candidates by.
/// A bare record with no factory of its own, validated by <see cref="AwardRules.Create"/> the way
/// <c>DraftOrderSlot</c> is validated by the snapshot that contains it — an award only means anything
/// in the context of the full list, where its code has to be unique.
/// </summary>
public sealed record AwardDefinition(string Code, string Name, AwardStatBasis StatBasis);

/// <summary>
/// Which awards this league hands out, and how each is decided — data, not code, so a modded league
/// with no defensive award needs no code change to leave it out, and one that wants an award this
/// build never shipped only needs a new entry once the stat it ranks by exists.
/// <para>
/// "How they are voted" here means a stat leaderboard, not a simulated media panel: every award is
/// decided by whichever <see cref="AwardStatBasis"/> it names, computed from
/// <c>BallGM.Rules.Seasons.PlayerSeasonStatsCalculator</c>'s output. A genuinely voted award — cast by
/// simulated voters weighing more than one number, the way real awards are — needs AI front offices
/// and explainable decision-making (Milestone 9) to mean anything; this milestone builds the
/// declaration and the leaderboard mechanism voting will eventually sit behind, not the vote itself.
/// </para>
/// </summary>
public sealed record AwardRules
{
    private const string EmptyCodeCode = "ruleset.award_missing_code";
    private const string EmptyNameCode = "ruleset.award_missing_name";
    private const string DuplicateCodeCode = "ruleset.award_duplicate_code";

    private AwardRules(IReadOnlyList<AwardDefinition> awards) => Awards = awards;

    /// <summary>A league that hands out no awards at all.</summary>
    public static AwardRules None { get; } = new([]);

    public bool IsConfigured => Awards.Count > 0;

    public IReadOnlyList<AwardDefinition> Awards { get; }

    public static DomainOperationResult<AwardRules> Create(IEnumerable<AwardDefinition>? awards)
    {
        if (awards is null)
        {
            return DomainOperationResult<AwardRules>.Success(None);
        }

        var list = awards.ToArray();
        if (list.Any(award => award is null))
        {
            throw new ArgumentException("The award list cannot contain a null award.", nameof(awards));
        }

        if (list.Length == 0)
        {
            return DomainOperationResult<AwardRules>.Success(None);
        }

        var errors = new List<DomainError>();

        foreach (var award in list)
        {
            if (string.IsNullOrWhiteSpace(award.Code))
            {
                errors.Add(new DomainError(EmptyCodeCode, $"The award '{award.Name}' has no code."));
            }

            if (string.IsNullOrWhiteSpace(award.Name))
            {
                errors.Add(new DomainError(EmptyNameCode, $"The award '{award.Code}' has no display name."));
            }
        }

        var duplicate = list
            .Where(award => !string.IsNullOrWhiteSpace(award.Code))
            .GroupBy(award => award.Code, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            errors.Add(new DomainError(DuplicateCodeCode, $"Award code '{duplicate.Key}' is stated more than once."));
        }

        return errors.Count > 0
            ? DomainOperationResult<AwardRules>.Failure(errors.ToArray())
            : DomainOperationResult<AwardRules>.Success(new AwardRules(list));
    }
}
