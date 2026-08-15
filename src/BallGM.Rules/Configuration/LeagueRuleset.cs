using BallGM.Domain.Common;
using BallGM.Domain.Teams;

namespace BallGM.Rules.Configuration;

/// <summary>
/// The full set of configurable rules for one league: schedule length, roster limits, cap
/// thresholds, and draft structure. This is the concrete <c>Ruleset</c> described in
/// <c>docs/domain-language.md</c> — versioned configuration a league loads from a file
/// (see <c>BallGM.Infrastructure.Rulesets</c>) rather than a value baked into the build, so a
/// rule change ships as a new ruleset file instead of waiting on a new release.
/// </summary>
public sealed record LeagueRuleset
{
    /// <summary>
    /// Bumped to 2 when Milestone 4 added the draft-asset restrictions (tradable horizon, retained
    /// round, retention interval). A version-1 file cannot describe those rules at all, so it is
    /// rejected rather than silently defaulted — a league quietly running restrictions its ruleset
    /// never stated is worse than one that refuses to load.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public LeagueRuleset(
        int schemaVersion,
        string name,
        int regularSeasonGameCount,
        RosterSizeLimits rosterLimits,
        CapThresholds capThresholds,
        DraftRules draftRules)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (regularSeasonGameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regularSeasonGameCount),
                regularSeasonGameCount,
                "Regular-season game count must be positive.");
        }

        ArgumentNullException.ThrowIfNull(rosterLimits);
        ArgumentNullException.ThrowIfNull(capThresholds);
        ArgumentNullException.ThrowIfNull(draftRules);

        SchemaVersion = schemaVersion;
        Name = name;
        RegularSeasonGameCount = regularSeasonGameCount;
        RosterLimits = rosterLimits;
        CapThresholds = capThresholds;
        DraftRules = draftRules;
    }

    public int SchemaVersion { get; }

    public string Name { get; }

    public int RegularSeasonGameCount { get; }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public DraftRules DraftRules { get; }
}
