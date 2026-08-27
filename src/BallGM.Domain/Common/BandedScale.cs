namespace BallGM.Domain.Common;

/// <summary>
/// One band of a <see cref="BandedScale"/>: the lowest key it covers, and the value that applies
/// from there until the next band starts.
/// </summary>
public sealed record ScaleBand(long MinimumKey, long Value);

/// <summary>
/// An ordered lookup from an integer key to a value, defined by the bands the key falls into. The
/// first ruleset content that is a <em>table</em> rather than a scalar, and deliberately one shape
/// rather than several: compensation floors and ceilings key off seasons of service here, the
/// draft-slot scale keys off pick number in a later milestone, and the tax-bracket table keys off
/// the amount above a threshold. Three tables that are the same shape should be the same type, and
/// getting that wrong once means getting it wrong three times.
/// <para>
/// The key is a <see cref="long"/> rather than an <see cref="int"/> for the third of those cases:
/// a bracket keyed off money needs the width. Callers do not see the primitive — the typed wrappers
/// around this (see <c>BallGM.Rules.Configuration</c>) take a service count or a
/// <see cref="Money"/> and hand back the same.
/// </para>
/// <para>
/// An empty scale is <see cref="None"/>, and it means the league does not have this table at all —
/// the same absence-is-absence reading the cap thresholds and the draft already use. It is not a
/// table of zeroes.
/// </para>
/// </summary>
public sealed record BandedScale
{
    private const string MissingBaseBandCode = "scale.missing_base_band";
    private const string DuplicateKeyCode = "scale.duplicate_band_key";
    private const string NegativeValueCode = "scale.negative_band_value";

    private BandedScale(IReadOnlyList<ScaleBand> bands) => Bands = bands;

    /// <summary>A league that does not configure this table. Not a table of zeroes — no table.</summary>
    public static BandedScale None { get; } = new([]);

    /// <summary>The configured bands, ascending by key.</summary>
    public IReadOnlyList<ScaleBand> Bands { get; }

    public bool IsEmpty => Bands.Count == 0;

    /// <summary>
    /// Builds a scale from its bands, or explains why the bands are not a scale. Passing no bands at
    /// all is <see cref="None"/> rather than a failure: a ruleset that leaves the table out is
    /// stating something, and it is not a mistake.
    /// </summary>
    public static DomainOperationResult<BandedScale> Create(IEnumerable<ScaleBand>? bands)
    {
        if (bands is null)
        {
            return DomainOperationResult<BandedScale>.Success(None);
        }

        var ordered = bands.ToList();
        if (ordered.Any(band => band is null))
        {
            throw new ArgumentException("A banded scale cannot contain null bands.", nameof(bands));
        }

        if (ordered.Count == 0)
        {
            return DomainOperationResult<BandedScale>.Success(None);
        }

        ordered = ordered.OrderBy(band => band.MinimumKey).ToList();
        var errors = new List<DomainError>();

        // The base band is not a convention, it is the thing that makes the table total. Without a
        // band starting at zero there is a key with no answer, and the alternatives are inventing
        // one or throwing at lookup time — both worse than refusing the file.
        if (ordered[0].MinimumKey != 0)
        {
            errors.Add(new DomainError(
                MissingBaseBandCode,
                $"A banded scale must start at 0, but its lowest band starts at {ordered[0].MinimumKey}. Without a band covering the bottom of the range there is no answer for a key below it."));
        }

        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].MinimumKey == ordered[index - 1].MinimumKey)
            {
                errors.Add(new DomainError(
                    DuplicateKeyCode,
                    $"A banded scale cannot carry two bands starting at {ordered[index].MinimumKey}."));
            }
        }

        foreach (var band in ordered.Where(band => band.Value < 0))
        {
            errors.Add(new DomainError(
                NegativeValueCode,
                $"The band starting at {band.MinimumKey} has a value of {band.Value}, which cannot be negative."));
        }

        return errors.Count > 0
            ? DomainOperationResult<BandedScale>.Failure(errors.ToArray())
            : DomainOperationResult<BandedScale>.Success(new BandedScale(ordered));
    }

    /// <summary>
    /// The value for one key: the highest band whose minimum the key reaches. Returns <c>null</c> on
    /// an unconfigured scale, which is the caller's cue that the league has no such rule — a zero
    /// here would be a rule saying "nothing", which is a different statement.
    /// </summary>
    public long? ValueFor(long key)
    {
        long? value = null;

        foreach (var band in Bands)
        {
            if (band.MinimumKey > key)
            {
                break;
            }

            value = band.Value;
        }

        return value;
    }
}
