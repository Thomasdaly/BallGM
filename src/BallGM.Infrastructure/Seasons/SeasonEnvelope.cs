namespace BallGM.Infrastructure.Seasons;

/// <summary>One phase of the saved calendar.</summary>
public sealed record CalendarPhaseEnvelope(string Phase, int StartDay, int EndDayExclusive);

/// <summary>One saved fixture. The identifier is stored rather than recomputed, so a file that disagrees with its own coordinates fails the replay instead of being silently corrected.</summary>
public sealed record FixtureEnvelope(string GameId, int Day, string HomeTeamId, string AwayTeamId, string Phase);

/// <summary>One player's saved line.</summary>
public sealed record PlayerStatLineEnvelope(
    string PlayerId,
    string TeamId,
    int Minutes,
    int Points,
    int Rebounds,
    int Assists,
    bool Started);

/// <summary>One saved result. <c>BoxScore</c> is absent for a result recorded without player lines.</summary>
public sealed record GameResultEnvelope(
    string GameId,
    int HomePoints,
    int AwayPoints,
    IReadOnlyList<PlayerStatLineEnvelope>? BoxScore);

/// <summary>One saved injury spell.</summary>
public sealed record InjurySpellEnvelope(string PlayerId, string Description, int FromDay, int UntilDayExclusive);

/// <summary>
/// Serialization shape for one season in progress: its seed, its calendar, its fixtures, how far it
/// has got, and everything that has happened.
/// <para>
/// <b>Its own schema version, independent of the ruleset's and of <c>LeagueSaveEnvelope</c>'s</b> —
/// the same decision Milestone 6b took for <c>NegotiationEnvelope</c>. A season and a ruleset change
/// for different reasons, and one version covering both would force a migration on everybody
/// whenever either moved.
/// </para>
/// <para>
/// The <b>seed</b> is the field that matters most here. Nothing else in the file would let a
/// resumed league play the rest of its schedule the way an uninterrupted one would: the games are
/// derived from this number and each fixture's identifier, so saving the number is what makes a
/// mid-season save reproducible rather than merely resumable.
/// </para>
/// </summary>
public sealed record SeasonEnvelope
{
    /// <summary>
    /// Version 1 is the first shape. It already carries results, box scores, and injury spells even
    /// though the build that introduced it plays no games, precisely so that the half of Milestone 7
    /// which does play them adds no version at all — a save format that changed the moment the
    /// simulation arrived would have been a format designed for the wrong thing.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public SeasonEnvelope(
        int schemaVersion,
        int seasonYear,
        int seed,
        string seasonStartDate,
        int currentDay,
        IReadOnlyList<CalendarPhaseEnvelope> phases,
        IReadOnlyList<FixtureEnvelope> fixtures,
        IReadOnlyList<GameResultEnvelope> results,
        IReadOnlyList<InjurySpellEnvelope> injuries)
    {
        SchemaVersion = schemaVersion;
        SeasonYear = seasonYear;
        Seed = seed;
        SeasonStartDate = seasonStartDate;
        CurrentDay = currentDay;
        Phases = phases;
        Fixtures = fixtures;
        Results = results;
        Injuries = injuries;
    }

    public int SchemaVersion { get; }

    public int SeasonYear { get; }

    /// <summary>The one number every game in this season is derived from.</summary>
    public int Seed { get; }

    /// <summary>The date season day 0 falls on, as <c>yyyy-MM-dd</c>.</summary>
    public string SeasonStartDate { get; } = string.Empty;

    public int CurrentDay { get; }

    public IReadOnlyList<CalendarPhaseEnvelope> Phases { get; } = [];

    public IReadOnlyList<FixtureEnvelope> Fixtures { get; } = [];

    public IReadOnlyList<GameResultEnvelope> Results { get; } = [];

    public IReadOnlyList<InjurySpellEnvelope> Injuries { get; } = [];
}
