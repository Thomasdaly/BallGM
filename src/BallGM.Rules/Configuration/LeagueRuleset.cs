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
    /// round, retention interval), and to 3 when Milestone 5 added the trade rules (salary matching,
    /// injured-player eligibility, the second-apron restriction). An older file cannot describe
    /// those rules at all, so it is rejected rather than silently defaulted — a league quietly
    /// running restrictions its ruleset never stated is worse than one that refuses to load.
    /// <para>
    /// Version 4 made the cap system, the draft, and salary matching <em>optional by absence</em>,
    /// and added the payroll floor. Nothing was renamed and no key was added, so a version 3 file's
    /// contents are still valid — but the version still had to move, because a version 3 reader
    /// handed a version 4 file would read every omitted field as a zero and run a cap system the
    /// ruleset never stated. That is the exact failure this whole scheme exists to refuse, so the
    /// gate refuses version 3 and says what to change.
    /// </para>
    /// <para>
    /// Version 5 added the negotiation rules: term and escalation limits, the compensation ceiling
    /// and floor tables, and the standard over-cap allowance. Every one of those fields is optional
    /// by absence exactly as version 4 established — a league configuring none of them is an open
    /// market, not a league where nobody may sign — so nothing about version 4's reading of absence
    /// is retracted here. The version moved because the <em>reader</em> changed, not because absence
    /// changed meaning: a version 4 reader handed a version 5 file would ignore a stated term limit
    /// and run an unrestricted market in a league that configured limits. That is gap 1 with the
    /// sign flipped — rules stated in the file and not run by the build — and it is the same failure
    /// the version gate exists to refuse. The serializer additionally refuses fields it does not
    /// know, so a file from a later build fails structurally rather than silently dropping rules.
    /// </para>
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    public LeagueRuleset(
        int schemaVersion,
        string name,
        int regularSeasonGameCount,
        RosterSizeLimits rosterLimits,
        CapThresholds capThresholds,
        DraftRules draftRules,
        TradeRules tradeRules,
        NegotiationRules negotiationRules)
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
        ArgumentNullException.ThrowIfNull(tradeRules);
        ArgumentNullException.ThrowIfNull(negotiationRules);

        SchemaVersion = schemaVersion;
        Name = name;
        RegularSeasonGameCount = regularSeasonGameCount;
        RosterLimits = rosterLimits;
        CapThresholds = capThresholds;
        DraftRules = draftRules;
        TradeRules = tradeRules;
        NegotiationRules = negotiationRules;
    }

    public int SchemaVersion { get; }

    public string Name { get; }

    public int RegularSeasonGameCount { get; }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public DraftRules DraftRules { get; }

    public TradeRules TradeRules { get; }

    /// <summary>
    /// What this league permits in a contract offer, and how a team may pay for it. A league that
    /// configures none of it is an open market — see <see cref="Configuration.NegotiationRules"/>.
    /// </summary>
    public NegotiationRules NegotiationRules { get; }
}
