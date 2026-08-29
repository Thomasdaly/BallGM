using BallGM.Domain.Common;

namespace BallGM.Domain.Seasons;

/// <summary>
/// How home advantage alternates through a postseason series, written the way leagues write it:
/// <c>2-2-1-1-1</c> means the higher seed hosts two, the lower seed hosts two, then they alternate
/// single games.
/// <para>
/// Configured rather than fixed, because <c>docs/competitive-feature-review.md</c> §4 dates
/// "configurable series length and home-court sequence" to this milestone, and because a league
/// that plays <c>2-3-2</c> is not a league that needs a code change. The blocks alternate starting
/// with the higher seed; a league that wanted to start with the lower seed would write a leading
/// zero-length block, which is refused — that is a different series, not this one with a quirk.
/// </para>
/// </summary>
public sealed record HomeCourtPattern
{
    private const string EmptyPatternCode = "postseason.empty_home_court_sequence";
    private const string MalformedBlockCode = "postseason.malformed_home_court_sequence";
    private const string NonPositiveBlockCode = "postseason.non_positive_home_court_block";

    private HomeCourtPattern(IReadOnlyList<int> blocks) => Blocks = blocks;

    /// <summary>The blocks of consecutive home games, alternating from the higher seed.</summary>
    public IReadOnlyList<int> Blocks { get; }

    public int TotalGames => Blocks.Sum();

    /// <summary>
    /// Parses a sequence as it appears in a ruleset file — digits separated by hyphens. Untrusted
    /// input, so a structured failure rather than a throw.
    /// </summary>
    public static DomainOperationResult<HomeCourtPattern> Parse(string sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return DomainOperationResult<HomeCourtPattern>.Failure(new DomainError(
                EmptyPatternCode,
                "A home-court sequence has to name at least one block of games, for example \"2-2-1-1-1\"."));
        }

        var parts = sequence.Split('-', StringSplitOptions.TrimEntries);
        var blocks = new List<int>(parts.Length);
        var errors = new List<DomainError>();

        foreach (var part in parts)
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var block))
            {
                errors.Add(new DomainError(
                    MalformedBlockCode,
                    $"'{part}' in home-court sequence '{sequence}' is not a number of games."));
                continue;
            }

            if (block <= 0)
            {
                errors.Add(new DomainError(
                    NonPositiveBlockCode,
                    $"Home-court sequence '{sequence}' contains a block of {block} games. A block of no games is a different sequence written misleadingly — write the sequence that is actually played."));
                continue;
            }

            blocks.Add(block);
        }

        return errors.Count > 0
            ? DomainOperationResult<HomeCourtPattern>.Failure(errors.ToArray())
            : DomainOperationResult<HomeCourtPattern>.Success(new HomeCourtPattern(blocks));
    }

    /// <summary>
    /// Whether the higher seed hosts game <paramref name="gameNumber"/>, counted from 1. Games past
    /// the end of the pattern keep alternating one at a time from wherever the pattern left off, so
    /// a sequence shorter than its series is extended rather than refused — a longer series is a
    /// ruleset combination a league may legitimately state.
    /// </summary>
    public bool HigherSeedHosts(int gameNumber)
    {
        if (gameNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(gameNumber), gameNumber, "Games in a series are counted from 1.");
        }

        var remaining = gameNumber;

        for (var index = 0; index < Blocks.Count; index++)
        {
            if (remaining <= Blocks[index])
            {
                return index % 2 == 0;
            }

            remaining -= Blocks[index];
        }

        // Past the stated pattern, single games alternate, continuing from the parity the last
        // stated block left behind.
        var parityAfterPattern = Blocks.Count % 2 == 0;
        return ((remaining - 1) % 2 == 0) == parityAfterPattern;
    }

    public override string ToString() => string.Join('-', Blocks);
}
