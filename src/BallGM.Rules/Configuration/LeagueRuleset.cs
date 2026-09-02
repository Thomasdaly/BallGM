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
    /// <remarks>
    /// Version 6 added everything a calendar makes expressible: the schedule section (phase lengths
    /// and the per-group opponent weighting), the standings tie-break sequence, the postseason
    /// format, the in-season signing window, the playoff eligibility cutoff, and short-term
    /// contracts. Optional by absence exactly as version 4 established — a file stating none of it
    /// is a league with a bare regular season, no stated tie-break, and no postseason, all of which
    /// are real leagues — so nothing about absence changes meaning. The version moved for the same
    /// reason it moved at 5: a version 5 reader handed a version 6 file would run a league with no
    /// tie-break sequence in a league that stated one, and settle every tie by a rule the ruleset
    /// never mentioned. That is the exact failure this gate exists to refuse.
    /// </remarks>
    /// <remarks>
    /// Version 7 is Milestone 8's whole ruleset surface: the draft-class generator (class size, the
    /// true-rating spread, and the age prospects enter at), the scouting model (base confidence, the
    /// zero-confidence range width, and the investment-to-confidence table), and the draft lottery's
    /// weighting table. Optional by absence exactly as every earlier version — a league stating none
    /// of it generates no classes of its own, models no scouting uncertainty, and runs a lottery in
    /// plain reverse-standings order if it enables one with no odds stated at all, which is refused
    /// rather than defaulted (see <c>LeagueConfigurationMapper</c>). The version moved because a
    /// version 6 reader handed a version 7 file would ignore a stated rating spread or lottery
    /// weighting and either generate nothing or draw uniformly, in a league that had described
    /// something more specific — the same class of silent gap every version bump here exists to close.
    /// </remarks>
    /// <remarks>
    /// Version 8 added the rest of Milestone 8's rule-driven mechanics: the development/ageing curve
    /// (peak age range, growth and decline tables, variance) and retirement (minimum voluntary age,
    /// mandatory age, voluntary odds by age). Optional by absence exactly as every earlier version — a
    /// league stating neither section models no ageing and no retirement, which is a real league (a
    /// roster frozen exactly as drafted) rather than an omission. The version moved because a version
    /// 7 reader handed a version 8 file would ignore a stated development curve or retirement age and
    /// run a league where nobody ages or retires, in a league that had described otherwise.
    /// </remarks>
    /// <remarks>
    /// Version 9 added the award set: which awards this league hands out, and which stat each is
    /// decided by. Optional by absence exactly as every earlier version — a league stating none hands
    /// out no awards at all, a real league shape rather than a missing rule. The version moved because
    /// a version 8 reader handed a version 9 file would ignore a stated award list and run a league
    /// with none, in a league that had described one.
    /// </remarks>
    public const int CurrentSchemaVersion = 9;

    public LeagueRuleset(
        int schemaVersion,
        string name,
        int regularSeasonGameCount,
        RosterSizeLimits rosterLimits,
        CapThresholds capThresholds,
        DraftRules draftRules,
        TradeRules tradeRules,
        NegotiationRules negotiationRules,
        ScheduleRules? scheduleRules = null,
        StandingsRules? standingsRules = null,
        PostseasonRules? postseasonRules = null,
        DraftClassRules? draftClassRules = null,
        ScoutingRules? scoutingRules = null,
        DraftLotteryRules? draftLotteryRules = null,
        DevelopmentRules? developmentRules = null,
        RetirementRules? retirementRules = null,
        AwardRules? awardRules = null)
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
        ScheduleRules = scheduleRules ?? ScheduleRules.Minimal;
        StandingsRules = standingsRules ?? StandingsRules.None;
        PostseasonRules = postseasonRules ?? PostseasonRules.None;
        DraftClassRules = draftClassRules ?? DraftClassRules.None;
        ScoutingRules = scoutingRules ?? ScoutingRules.None;
        DraftLotteryRules = draftLotteryRules ?? DraftLotteryRules.None;
        DevelopmentRules = developmentRules ?? DevelopmentRules.None;
        RetirementRules = retirementRules ?? RetirementRules.None;
        AwardRules = awardRules ?? AwardRules.None;
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

    /// <summary>
    /// How long each phase of a season runs, and how often each kind of opponent is played. Note
    /// that <em>who</em> is in which conference and division is not here: alignment is league
    /// content on the <c>League</c> aggregate, so one ruleset can serve two differently aligned
    /// leagues and an expansion is not a ruleset edit.
    /// </summary>
    public ScheduleRules ScheduleRules { get; }

    /// <summary>The ordered tie-break sequence, or <see cref="Configuration.StandingsRules.None"/> where the league states none.</summary>
    public StandingsRules StandingsRules { get; }

    /// <summary>The postseason format, or <see cref="Configuration.PostseasonRules.None"/> in a league that holds no postseason.</summary>
    public PostseasonRules PostseasonRules { get; }

    /// <summary>Whether this league plays a postseason at all.</summary>
    public bool HasPostseason => PostseasonRules.IsConfigured;

    /// <summary>How this league's own draft classes are procedurally generated, or <see cref="Configuration.DraftClassRules.None"/>.</summary>
    public DraftClassRules DraftClassRules { get; }

    /// <summary>How much of a prospect's true rating scouting reveals, or <see cref="Configuration.ScoutingRules.None"/>.</summary>
    public ScoutingRules ScoutingRules { get; }

    /// <summary>The draft lottery's weighting table, or <see cref="Configuration.DraftLotteryRules.None"/>.</summary>
    public DraftLotteryRules DraftLotteryRules { get; }

    /// <summary>How a player's rating moves with age, or <see cref="Configuration.DevelopmentRules.None"/>.</summary>
    public DevelopmentRules DevelopmentRules { get; }

    /// <summary>When a player's career ends, or <see cref="Configuration.RetirementRules.None"/>.</summary>
    public RetirementRules RetirementRules { get; }

    /// <summary>The award set, or <see cref="Configuration.AwardRules.None"/> in a league that hands out none.</summary>
    public AwardRules AwardRules { get; }
}
