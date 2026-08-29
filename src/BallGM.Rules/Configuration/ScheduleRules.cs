using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// How long a season's phases run, and how often a team plays each kind of opponent.
/// <para>
/// The phase lengths are here and the <em>alignment</em> — who is in which conference and division —
/// is on the <c>League</c> aggregate, deliberately. Who plays out of which division is league
/// content that moves when a franchise relocates or the league expands; how many times you play a
/// division opponent is a rule. Keeping them apart is what stops an expansion from being a ruleset
/// edit, and what lets one ruleset file be loaded by two leagues aligned differently.
/// </para>
/// <para>
/// The three opponent weights are stated as a group or left out entirely. Absence means this league
/// does not weight opponents by group at all, and the schedule generator balances a plain round
/// robin instead — reported as a note, because a schedule built by a rule the league never stated
/// is the same class of bug as a standings tie broken by one.
/// </para>
/// </summary>
public sealed record ScheduleRules
{
    private const string NegativePhaseLengthCode = "ruleset.negative_phase_length";
    private const string NonPositiveRegularSeasonCode = "ruleset.non_positive_regular_season_days";
    private const string PartialWeightingCode = "ruleset.partial_opponent_weighting";
    private const string NonPositiveWeightCode = "ruleset.non_positive_opponent_weight";

    private ScheduleRules(
        int preseasonDays,
        int regularSeasonDays,
        int offseasonDays,
        int? gamesVersusDivisionOpponent,
        int? gamesVersusConferenceOpponent,
        int? gamesVersusOtherConferenceOpponent)
    {
        PreseasonDays = preseasonDays;
        RegularSeasonDays = regularSeasonDays;
        OffseasonDays = offseasonDays;
        GamesVersusDivisionOpponent = gamesVersusDivisionOpponent;
        GamesVersusConferenceOpponent = gamesVersusConferenceOpponent;
        GamesVersusOtherConferenceOpponent = gamesVersusOtherConferenceOpponent;
    }

    /// <summary>
    /// The shortest calendar that can still be played: a regular season and nothing either side of
    /// it. What a ruleset with no schedule section loads to.
    /// </summary>
    public static ScheduleRules Minimal { get; } = new(0, 1, 0, null, null, null);

    /// <summary>Days before the regular season. Zero means this league has no preseason phase at all.</summary>
    public int PreseasonDays { get; }

    /// <summary>Days the regular season runs for. Always at least one — a season with no days plays no games.</summary>
    public int RegularSeasonDays { get; }

    /// <summary>Days after the season ends, before the next one opens. Zero means the calendar stops at the end.</summary>
    public int OffseasonDays { get; }

    /// <summary>Games against each opponent in the same division. Absent where the league states no weighting.</summary>
    public int? GamesVersusDivisionOpponent { get; }

    /// <summary>Games against each same-conference opponent outside the division.</summary>
    public int? GamesVersusConferenceOpponent { get; }

    /// <summary>Games against each opponent in the other conference.</summary>
    public int? GamesVersusOtherConferenceOpponent { get; }

    /// <summary>Whether this league states how often each kind of opponent is played.</summary>
    public bool HasOpponentWeighting =>
        GamesVersusDivisionOpponent is not null &&
        GamesVersusConferenceOpponent is not null &&
        GamesVersusOtherConferenceOpponent is not null;

    public static DomainOperationResult<ScheduleRules> Create(
        int preseasonDays,
        int regularSeasonDays,
        int offseasonDays,
        int? gamesVersusDivisionOpponent = null,
        int? gamesVersusConferenceOpponent = null,
        int? gamesVersusOtherConferenceOpponent = null)
    {
        var errors = new List<DomainError>();

        if (preseasonDays < 0 || offseasonDays < 0)
        {
            errors.Add(new DomainError(
                NegativePhaseLengthCode,
                $"A season phase cannot run for a negative number of days (preseason {preseasonDays}, offseason {offseasonDays})."));
        }

        if (regularSeasonDays <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveRegularSeasonCode,
                $"The regular season is configured to run for {regularSeasonDays} days. A league whose regular season covers no days can play no games."));
        }

        var stated = new[] { gamesVersusDivisionOpponent, gamesVersusConferenceOpponent, gamesVersusOtherConferenceOpponent };
        var statedCount = stated.Count(weight => weight is not null);

        if (statedCount is > 0 and < 3)
        {
            errors.Add(new DomainError(
                PartialWeightingCode,
                $"This ruleset states {statedCount} of the three opponent weightings. State all three, or leave all three out — a partial weighting leaves the generator to invent the rest, which is a schedule the league never described."));
        }

        foreach (var weight in stated.Where(weight => weight is <= 0))
        {
            errors.Add(new DomainError(
                NonPositiveWeightCode,
                $"An opponent weighting of {weight} games is not a weighting. Leave the group out if this league does not weight opponents; a league that genuinely never plays one group is a league whose alignment says so."));
        }

        return errors.Count > 0
            ? DomainOperationResult<ScheduleRules>.Failure(errors.ToArray())
            : DomainOperationResult<ScheduleRules>.Success(new ScheduleRules(
                preseasonDays,
                regularSeasonDays,
                offseasonDays,
                gamesVersusDivisionOpponent,
                gamesVersusConferenceOpponent,
                gamesVersusOtherConferenceOpponent));
    }
}
