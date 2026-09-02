using BallGM.Domain.Common;
using BallGM.Domain.Players;

namespace BallGM.Rules.Configuration;

/// <summary>
/// How this league's own draft classes are procedurally generated: how many prospects, the spread of
/// true rating they are drawn from, and the age they enter the draft at.
/// <para>
/// <see cref="None"/> is a league that generates no classes of its own — a modder supplying a
/// draft-class playlist (Milestone 10) instead, or a league with no draft at all. Absence here is the
/// same statement absence makes everywhere else in this ruleset: not "generate nothing" but "this
/// league does not configure this generator."
/// </para>
/// </summary>
public sealed record DraftClassRules
{
    private const string NonPositiveClassSizeCode = "ruleset.non_positive_draft_class_size";
    private const string InvalidRatingBoundCode = "ruleset.invalid_draft_class_rating_bound";
    private const string InvertedRatingBoundsCode = "ruleset.inverted_draft_class_rating_bounds";
    private const string NonPositiveAgeCode = "ruleset.non_positive_prospect_age";

    private DraftClassRules(int classSize, int minimumRating, int maximumRating, int prospectAgeYears)
    {
        ClassSize = classSize;
        MinimumRating = minimumRating;
        MaximumRating = maximumRating;
        ProspectAgeYears = prospectAgeYears;
    }

    /// <summary>A league that does not procedurally generate its own draft classes.</summary>
    public static DraftClassRules None { get; } = new(0, 0, 0, 0);

    public bool IsConfigured => ClassSize > 0;

    /// <summary>How many prospects one generated class contains.</summary>
    public int ClassSize { get; }

    /// <summary>The lowest true <see cref="PlayerRating.Overall"/> a generated prospect may carry.</summary>
    public int MinimumRating { get; }

    /// <summary>The highest true <see cref="PlayerRating.Overall"/> a generated prospect may carry.</summary>
    public int MaximumRating { get; }

    /// <summary>The age, in completed years, every generated prospect enters the draft at.</summary>
    public int ProspectAgeYears { get; }

    public static DomainOperationResult<DraftClassRules> Create(
        int classSize,
        int minimumRating,
        int maximumRating,
        int prospectAgeYears)
    {
        var errors = new List<DomainError>();

        if (classSize <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveClassSizeCode,
                $"A generated draft class must contain at least one prospect, but the configured size was {classSize}."));
        }

        if (minimumRating < PlayerRating.MinimumOverall || minimumRating > PlayerRating.MaximumOverall)
        {
            errors.Add(new DomainError(
                InvalidRatingBoundCode,
                $"The draft class minimum rating must fall inside {PlayerRating.MinimumOverall}-{PlayerRating.MaximumOverall}, but was {minimumRating}."));
        }

        if (maximumRating < PlayerRating.MinimumOverall || maximumRating > PlayerRating.MaximumOverall)
        {
            errors.Add(new DomainError(
                InvalidRatingBoundCode,
                $"The draft class maximum rating must fall inside {PlayerRating.MinimumOverall}-{PlayerRating.MaximumOverall}, but was {maximumRating}."));
        }

        if (minimumRating > maximumRating)
        {
            errors.Add(new DomainError(
                InvertedRatingBoundsCode,
                $"The draft class minimum rating ({minimumRating}) cannot exceed its maximum ({maximumRating})."));
        }

        if (prospectAgeYears <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveAgeCode,
                $"Prospects must enter the draft at a positive age, but the configured age was {prospectAgeYears}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<DraftClassRules>.Failure(errors.ToArray())
            : DomainOperationResult<DraftClassRules>.Success(new DraftClassRules(classSize, minimumRating, maximumRating, prospectAgeYears));
    }
}
