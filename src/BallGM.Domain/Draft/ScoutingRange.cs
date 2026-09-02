using BallGM.Domain.Common;
using BallGM.Domain.Players;

namespace BallGM.Domain.Draft;

/// <summary>
/// What scouting has revealed about a prospect's <see cref="PlayerRating.Overall"/>: a band the true
/// value falls inside, and how confident that band is. A value object in its own right rather than a
/// fudge applied to the real number, so a screen can render "78-88, 40% confidence" honestly instead
/// of a single number quietly wrong by a hidden margin.
/// <para>
/// <see cref="Confidence"/> is 0-100. At 100 the band collapses to a single point — full knowledge —
/// which is why <see cref="Certain"/> exists rather than every fully-scouted prospect needing its own
/// band arithmetic worked out by every caller.
/// </para>
/// </summary>
public sealed record ScoutingRange
{
    private const string InvalidBoundCode = "scouting_range.bound_out_of_rating_range";
    private const string InvertedBoundsCode = "scouting_range.lower_above_upper";
    private const string InvalidConfidenceCode = "scouting_range.confidence_out_of_range";

    private ScoutingRange(int lowerBound, int upperBound, int confidence)
    {
        LowerBound = lowerBound;
        UpperBound = upperBound;
        Confidence = confidence;
    }

    /// <summary>The lowest <see cref="PlayerRating.Overall"/> scouting believes is plausible.</summary>
    public int LowerBound { get; }

    /// <summary>The highest <see cref="PlayerRating.Overall"/> scouting believes is plausible.</summary>
    public int UpperBound { get; }

    /// <summary>How sure scouting is, 0 (no idea) to 100 (exact knowledge).</summary>
    public int Confidence { get; }

    public static DomainOperationResult<ScoutingRange> Create(int lowerBound, int upperBound, int confidence)
    {
        var errors = new List<DomainError>();

        if (lowerBound < PlayerRating.MinimumOverall || lowerBound > PlayerRating.MaximumOverall)
        {
            errors.Add(new DomainError(
                InvalidBoundCode,
                $"A scouting range's lower bound must fall inside {PlayerRating.MinimumOverall}-{PlayerRating.MaximumOverall}, but was {lowerBound}."));
        }

        if (upperBound < PlayerRating.MinimumOverall || upperBound > PlayerRating.MaximumOverall)
        {
            errors.Add(new DomainError(
                InvalidBoundCode,
                $"A scouting range's upper bound must fall inside {PlayerRating.MinimumOverall}-{PlayerRating.MaximumOverall}, but was {upperBound}."));
        }

        if (lowerBound > upperBound)
        {
            errors.Add(new DomainError(
                InvertedBoundsCode,
                $"A scouting range's lower bound ({lowerBound}) cannot exceed its upper bound ({upperBound})."));
        }

        if (confidence < 0 || confidence > 100)
        {
            errors.Add(new DomainError(
                InvalidConfidenceCode,
                $"Scouting confidence must be between 0 and 100, but was {confidence}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<ScoutingRange>.Failure(errors.ToArray())
            : DomainOperationResult<ScoutingRange>.Success(new ScoutingRange(lowerBound, upperBound, confidence));
    }

    /// <summary>Full knowledge: the band collapses onto the true rating at 100 confidence.</summary>
    public static ScoutingRange Certain(int trueOverall) => Create(trueOverall, trueOverall, 100).Value;
}
