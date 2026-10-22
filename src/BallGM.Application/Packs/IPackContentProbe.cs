using BallGM.Application.Leagues;

namespace BallGM.Application.Packs;

/// <summary>
/// What the pack checker needs to know about content it cannot read itself: whether a ruleset file
/// loads, whether a league loads under a given ruleset and how well it fits it, and whether a season
/// can actually be played with the result. The checker lives in <c>BallGM.Mods</c>, which sees only
/// Application and Domain; the ruleset format, the league-pack format and the rules live below
/// Infrastructure, so this port is how the one asks the other without either reaching across.
/// <para>
/// Every member reports through <see cref="PackFinding"/>s rather than throwing: probing untrusted
/// content is the whole job, and a probe that crashed on a bad pack would be the failure the checker
/// exists to prevent.
/// </para>
/// </summary>
public interface IPackContentProbe
{
    /// <summary>Whether <paramref name="rulesetJson"/> is a ruleset this build can run, and the few figures the checker reads from it.</summary>
    PackProbeResult<RulesetSummary> ProbeRuleset(string rulesetJson);

    /// <summary>
    /// Loads <paramref name="source"/> under <paramref name="rulesetJson"/> — not under whatever
    /// ruleset a pack itself names — and reports how the content fits those settings: rosters,
    /// salaries against the scales, payrolls against the lines.
    /// </summary>
    PackProbeResult<LeagueSnapshot> ProbeLeague(LeagueSource source, string rulesetJson);

    /// <summary>
    /// Loads <paramref name="source"/> afresh, plays a whole season of it from a fixed seed, concludes
    /// it, and opens the next one: the stability check proper. A combination of settings and content
    /// that cannot start, finish or roll over a season fails here, on the checker's screen, rather than
    /// on day one of a career. A fresh load because playing a season changes the aggregates it plays
    /// with, and the league the player is about to start must not be one the checker has already
    /// played.
    /// </summary>
    IReadOnlyList<PackFinding> SmokeTestSeason(LeagueSource source, string rulesetJson, int seed);
}

/// <summary>A probe's outcome: the value when the content loaded, and everything worth saying either way.</summary>
public sealed record PackProbeResult<T>(T? Value, IReadOnlyList<PackFinding> Findings)
    where T : class
{
    public bool Loaded => Value is not null;
}

/// <summary>The figures of a loaded ruleset the checker reads directly.</summary>
public sealed record RulesetSummary(
    string Name,
    int MinimumRosterPlayers,
    int MaximumRosterPlayers,
    int RegularSeasonGameCount);

/// <summary>Where a league's teams and players come from.</summary>
public abstract record LeagueSource;

/// <summary>A league pack file on disk.</summary>
public sealed record LeaguePackSource(string PackFilePath) : LeagueSource;

/// <summary>
/// A fictional league generated from a seed in the given shape, with salaries put on the chosen
/// ruleset's scale. <see cref="RosterSize"/> absent means the ruleset's roster maximum (at most the
/// generator's fifteen).
/// </summary>
public sealed record GeneratedLeagueSource(
    int Seed,
    int TeamCount,
    int Conferences,
    int DivisionsPerConference,
    int? RosterSize = null) : LeagueSource;
