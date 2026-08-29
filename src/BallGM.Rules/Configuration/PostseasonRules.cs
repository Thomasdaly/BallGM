using BallGM.Domain.Common;
using BallGM.Domain.Seasons;

namespace BallGM.Rules.Configuration;

/// <summary>
/// This league's postseason: how long it runs, how many teams reach it, how long each round's
/// series is, and how home advantage alternates inside one.
/// <para>
/// <see cref="None"/> is a league that does not hold a postseason, which is a league rather than a
/// misconfiguration — the season simply ends when the schedule does. That case is one of this
/// milestone's stated unhappy paths precisely because it is the one a build assumes away.
/// </para>
/// </summary>
public sealed record PostseasonRules
{
    private const string NonPositiveDaysCode = "ruleset.non_positive_postseason_days";
    private const string QualifiersNotPowerOfTwoCode = "ruleset.postseason_qualifiers_not_a_power_of_two";
    private const string TooFewQualifiersCode = "ruleset.too_few_postseason_qualifiers";
    private const string NoSeriesLengthsCode = "ruleset.no_postseason_series_lengths";
    private const string EvenSeriesLengthCode = "ruleset.even_postseason_series_length";
    private const string RoundCountMismatchCode = "ruleset.postseason_round_count_mismatch";
    private const string CutoffAfterPostseasonCode = "ruleset.playoff_cutoff_after_postseason_starts";

    private PostseasonRules(
        int postseasonDays,
        int qualifyingTeamsPerConference,
        IReadOnlyList<int> seriesLengths,
        HomeCourtPattern homeCourtSequence,
        int? playoffEligibilityCutoffDay)
    {
        PostseasonDays = postseasonDays;
        QualifyingTeamsPerConference = qualifyingTeamsPerConference;
        SeriesLengths = seriesLengths;
        HomeCourtSequence = homeCourtSequence;
        PlayoffEligibilityCutoffDay = playoffEligibilityCutoffDay;
    }

    /// <summary>A league with no postseason. The season ends when the regular season does.</summary>
    public static PostseasonRules None { get; } = new(0, 0, [], HomeCourtPattern.Parse("1").Value, null);

    public int PostseasonDays { get; }

    /// <summary>
    /// Teams reaching the postseason from each conference — or from the league as a whole, in a flat
    /// league. A power of two, because this build draws a single-elimination bracket and a bracket
    /// with byes is a different format rather than this one with a rounding rule.
    /// </summary>
    public int QualifyingTeamsPerConference { get; }

    /// <summary>Games in each round's series, in round order, longest-round-last.</summary>
    public IReadOnlyList<int> SeriesLengths { get; }

    public HomeCourtPattern HomeCourtSequence { get; }

    /// <summary>
    /// The last season day a player may be added and still be eligible for the postseason. Only
    /// expressible once a calendar exists. Absent means this league sets no cutoff, which is
    /// reported rather than assumed.
    /// </summary>
    public int? PlayoffEligibilityCutoffDay { get; }

    public bool IsConfigured => PostseasonDays > 0 && QualifyingTeamsPerConference > 0;

    public bool HasEligibilityCutoff => PlayoffEligibilityCutoffDay is not null;

    /// <summary>Rounds needed to reduce the qualifiers of one conference to a single team.</summary>
    public int RoundsPerConference => QualifyingTeamsPerConference <= 1
        ? 0
        : (int)Math.Log2(QualifyingTeamsPerConference);

    public int SeriesLengthForRound(int roundNumber)
    {
        if (roundNumber < 1 || roundNumber > SeriesLengths.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(roundNumber), roundNumber, "Postseason rounds are counted from 1.");
        }

        return SeriesLengths[roundNumber - 1];
    }

    public static DomainOperationResult<PostseasonRules> Create(
        int postseasonDays,
        int qualifyingTeamsPerConference,
        IEnumerable<int> seriesLengths,
        string homeCourtSequence,
        int? playoffEligibilityCutoffDay,
        int regularSeasonEndDay,
        bool? includesFinal = null)
    {
        ArgumentNullException.ThrowIfNull(seriesLengths);

        var lengths = seriesLengths.ToArray();
        var errors = new List<DomainError>();

        if (postseasonDays <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveDaysCode,
                $"The postseason is configured to run for {postseasonDays} days. Leave the whole section out if this league does not hold one."));
        }

        if (qualifyingTeamsPerConference < 2)
        {
            errors.Add(new DomainError(
                TooFewQualifiersCode,
                $"{qualifyingTeamsPerConference} team(s) qualify per conference. A postseason needs at least two teams to have a series at all."));
        }
        else if ((qualifyingTeamsPerConference & (qualifyingTeamsPerConference - 1)) != 0)
        {
            errors.Add(new DomainError(
                QualifiersNotPowerOfTwoCode,
                $"{qualifyingTeamsPerConference} teams qualify per conference, which is not a power of two. This build draws a single-elimination bracket; a format with byes is a different format, not this one with a rounding rule."));
        }

        if (lengths.Length == 0)
        {
            errors.Add(new DomainError(
                NoSeriesLengthsCode,
                "The postseason states no series lengths, so no round has a length to be played over."));
        }

        foreach (var length in lengths.Where(length => length <= 0 || length % 2 == 0))
        {
            errors.Add(new DomainError(
                EvenSeriesLengthCode,
                $"A series of {length} games cannot be won outright — a series length is an odd number of games, so that one side reaches a majority."));
        }

        // A conference bracket of N qualifiers takes log2(N) rounds; a league with conferences plays
        // one more for the final between the two conference winners. A ruleset stating a different
        // number of series lengths has described a postseason with a round nobody can play, or one
        // whose last round has no stated length.
        //
        // Whether there is a final depends on the league's alignment, not on the ruleset — the same
        // file loaded by a flat league and by a two-conference one has a different round count. So
        // the caller says which it is, and a caller that does not know (the ruleset serializer, which
        // reads a file no league is attached to yet) passes null and leaves the check to the adapter
        // that does. Skipping it is stated here rather than approximated with a guess.
        if (includesFinal is { } playsFinal &&
            qualifyingTeamsPerConference >= 2 &&
            (qualifyingTeamsPerConference & (qualifyingTeamsPerConference - 1)) == 0 &&
            lengths.Length > 0)
        {
            var expectedRounds = (int)Math.Log2(qualifyingTeamsPerConference) + (playsFinal ? 1 : 0);

            if (lengths.Length != expectedRounds)
            {
                errors.Add(new DomainError(
                    RoundCountMismatchCode,
                    $"This postseason has {expectedRounds} round(s) — {qualifyingTeamsPerConference} qualifiers per conference{(playsFinal ? " plus a final" : string.Empty)} — but {lengths.Length} series length(s) are stated."));
            }
        }

        var patternResult = HomeCourtPattern.Parse(homeCourtSequence);
        if (patternResult.IsFailure)
        {
            errors.AddRange(patternResult.Errors);
        }

        if (playoffEligibilityCutoffDay is { } cutoff && cutoff > regularSeasonEndDay)
        {
            errors.Add(new DomainError(
                CutoffAfterPostseasonCode,
                $"The playoff eligibility cutoff falls on day {cutoff}, after the regular season ends on day {regularSeasonEndDay}. A cutoff that lands inside the postseason is a cutoff nobody can miss."));
        }

        return errors.Count > 0
            ? DomainOperationResult<PostseasonRules>.Failure(errors.ToArray())
            : DomainOperationResult<PostseasonRules>.Success(new PostseasonRules(
                postseasonDays,
                qualifyingTeamsPerConference,
                lengths,
                patternResult.Value,
                playoffEligibilityCutoffDay));
    }
}
