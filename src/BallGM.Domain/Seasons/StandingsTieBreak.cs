using BallGM.Domain.Common;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One rule a league may state for separating teams on the same record. The vocabulary, not the
/// order — the order is the league's, and it lives in <see cref="TieBreakSequence"/>.
/// </summary>
public enum StandingsTieBreak
{
    /// <summary>The record between the tied teams themselves.</summary>
    HeadToHeadRecord = 1,

    /// <summary>Record against opponents in the same division. Not a rule in a league with no divisions.</summary>
    DivisionRecord = 2,

    /// <summary>Record against opponents in the same conference. Not a rule in a league with no conferences.</summary>
    ConferenceRecord = 3,

    /// <summary>Points scored less points conceded, across the games counted in the table.</summary>
    PointDifferential = 4,

    /// <summary>Points scored.</summary>
    PointsScored = 5,
}

/// <summary>
/// The ordered list of tie-breaks a league states, applied in the order the league states them.
/// <para>
/// An ordered list rather than a fixed algorithm because "a standings tie resolved by a rule the
/// league never stated" is the classic silent-wrong-answer bug — see
/// <c>docs/competitive-feature-review.md</c> §7. The whole point is that the sequence is data.
/// </para>
/// <para>
/// <see cref="None"/> is a real answer, not a missing one: a league that states no tie-break has no
/// tie-break. Ties in such a league fall to the terminal ordering key (team identifier), and the
/// standings say so in a note rather than presenting the accident as a ruling.
/// </para>
/// </summary>
public sealed record TieBreakSequence
{
    private const string DuplicateTieBreakCode = "standings.duplicate_tie_break";

    private TieBreakSequence(IReadOnlyList<StandingsTieBreak> steps) => Steps = steps;

    /// <summary>A league that states no tie-break at all.</summary>
    public static TieBreakSequence None { get; } = new([]);

    public static DomainOperationResult<TieBreakSequence> Create(IEnumerable<StandingsTieBreak>? steps)
    {
        if (steps is null)
        {
            return DomainOperationResult<TieBreakSequence>.Success(None);
        }

        var ordered = steps.ToArray();
        var errors = new List<DomainError>();

        foreach (var step in ordered.Where(step => !Enum.IsDefined(step)).Distinct())
        {
            errors.Add(new DomainError(
                "standings.unknown_tie_break",
                $"'{step}' is not a tie-break this build knows how to apply."));
        }

        var seen = new HashSet<StandingsTieBreak>();
        foreach (var step in ordered.Where(step => !seen.Add(step)))
        {
            errors.Add(new DomainError(
                DuplicateTieBreakCode,
                $"Tie-break '{step}' appears twice in the sequence. A second application of a rule that already failed to separate two teams cannot separate them either."));
        }

        return errors.Count > 0
            ? DomainOperationResult<TieBreakSequence>.Failure(errors.ToArray())
            : DomainOperationResult<TieBreakSequence>.Success(new TieBreakSequence(ordered));
    }

    public IReadOnlyList<StandingsTieBreak> Steps { get; }

    public bool IsEmpty => Steps.Count == 0;
}
