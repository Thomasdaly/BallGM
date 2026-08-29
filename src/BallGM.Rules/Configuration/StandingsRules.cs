using BallGM.Domain.Common;
using BallGM.Domain.Seasons;

namespace BallGM.Rules.Configuration;

/// <summary>
/// How this league separates teams on the same record.
/// <para>
/// An ordered list, straight from the ruleset file. <c>docs/competitive-feature-review.md</c> §7
/// names "a standings tie resolved by a rule that does not match the league's stated one" as the
/// classic silent-wrong-answer bug, and the defence is that the sequence is data rather than an
/// algorithm — including the case where a league states nothing at all.
/// </para>
/// <para>
/// <see cref="None"/> is a real configuration: the league states no tie-break. Ties there fall to
/// the terminal ordering key so that the table is still totally ordered, and every tie it settles
/// comes back as a note on the standings. That is the distinction the whole "optional by absence"
/// scheme rests on — a check that silently passed is indistinguishable from a check that ran.
/// </para>
/// </summary>
public sealed record StandingsRules
{
    private StandingsRules(TieBreakSequence tieBreaks) => TieBreaks = tieBreaks;

    /// <summary>A league that states no tie-break at all.</summary>
    public static StandingsRules None { get; } = new(TieBreakSequence.None);

    public TieBreakSequence TieBreaks { get; }

    public bool HasTieBreaks => !TieBreaks.IsEmpty;

    public static DomainOperationResult<StandingsRules> Create(IEnumerable<StandingsTieBreak>? tieBreaks)
    {
        var sequenceResult = TieBreakSequence.Create(tieBreaks);

        return sequenceResult.IsFailure
            ? DomainOperationResult<StandingsRules>.Failure(sequenceResult.Errors.ToArray())
            : DomainOperationResult<StandingsRules>.Success(new StandingsRules(sequenceResult.Value));
    }

    /// <summary>
    /// Parses the sequence as it appears in a ruleset file — names rather than numbers, so the file
    /// stays readable as the vocabulary grows, and an unknown name fails loudly instead of being
    /// dropped from the order.
    /// </summary>
    public static DomainOperationResult<StandingsRules> Parse(IEnumerable<string>? names)
    {
        if (names is null)
        {
            return DomainOperationResult<StandingsRules>.Success(None);
        }

        var parsed = new List<StandingsTieBreak>();
        var errors = new List<DomainError>();

        foreach (var name in names)
        {
            if (Enum.TryParse<StandingsTieBreak>(name, out var tieBreak) && Enum.IsDefined(tieBreak))
            {
                parsed.Add(tieBreak);
                continue;
            }

            errors.Add(new DomainError(
                "ruleset.unknown_tie_break",
                $"'{name}' is not a standings tie-break this build knows how to apply. Expected one of: {string.Join(", ", Enum.GetNames<StandingsTieBreak>())}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<StandingsRules>.Failure(errors.ToArray())
            : Create(parsed);
    }
}
