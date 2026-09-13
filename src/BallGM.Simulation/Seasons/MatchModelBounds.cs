namespace BallGM.Simulation.Seasons;

/// <summary>
/// Every term that enters a game's outcome, as a named constant with a stated bound.
/// <para>
/// This exists because of the failure recorded in <c>docs/competitive-feature-review.md</c> §7:
/// <b>one simulation input dominating an outcome probability without a cap</b>. Every term below is
/// bounded, every bound is named here rather than buried in the model, and the relationships between
/// them are asserted in <c>MatchModelBoundsTests</c>. A constant nobody checks is a magic number
/// with a better name.
/// </para>
/// <para>
/// <b>Everything is integer, in fixed-point units.</b> Efficiencies are points per ten thousand
/// possessions and probabilities are basis points, so no outcome anywhere depends on floating-point
/// rounding. That is the same reason <c>TeamRecord</c> compares by cross-multiplication and money is
/// integer smallest-units: a game that rounded differently on another platform would break the
/// determinism guarantee the whole season rests on.
/// </para>
/// <para>
/// These are deliberately <b>not</b> ruleset fields. A league may configure how many games it plays
/// and how its postseason is drawn; how efficiently a basketball team scores is a property of the
/// sport. Making it configuration would let a data pack state an efficiency at which no bound below
/// still holds. The same line <c>MinutesAllocationBounds</c> draws.
/// </para>
/// </summary>
public static class MatchModelBounds
{
    /// <summary>The denominator efficiencies are expressed against: points per ten thousand possessions.</summary>
    public const int EfficiencyScale = 10_000;

    /// <summary>The denominator probabilities are expressed against.</summary>
    public const int ProbabilityScale = 10_000;

    /// <summary>
    /// What a team scores per hundred possessions before anything about either side is considered,
    /// in <see cref="EfficiencyScale"/> units — so 11,500 is 115.0 points per hundred possessions.
    /// Retuned from 10,800 once real shot attempts and free throws replaced a single blended
    /// scoring-rate draw (see <see cref="FieldGoalAttemptRate"/> and the field-goal percentages
    /// below) — the sim audit's own target for this figure, unreachable by the old mechanism no
    /// matter how its one dial was turned.
    /// </summary>
    public const int BaseOffensiveEfficiency = 11_500;

    /// <summary>
    /// How much one rating point of advantage is worth, in efficiency units. Applied to the
    /// <em>difference</em> between the two teams and split between them, so a ten-point edge moves
    /// the margin by roughly six points a game rather than moving one side's total in isolation.
    /// <para>
    /// Relative rather than absolute on purpose: basketball scores do not depend on how good the
    /// league is, only on how the two teams compare. A league of 40-rated players and a league of
    /// 90-rated players should both produce recognisable scorelines, and a model keyed on absolute
    /// rating would send one to nothing and the other off the top of the scoreboard.
    /// </para>
    /// </summary>
    public const int EfficiencyPerRatingPoint = 60;

    /// <summary>
    /// The most the strength difference may move a team's efficiency, in either direction. This is
    /// the cap §7 is about: without it, a mismatch between a 95-rated squad and a 45-rated one
    /// produces a scoreline no basketball game has ever finished.
    /// </summary>
    public const int MaximumStrengthEfficiencySwing = 1_200;

    /// <summary>
    /// What playing at home is worth to the home side's efficiency. Roughly three points a game
    /// across a normal number of possessions, which is about what home advantage is worth.
    /// </summary>
    public const int HomeCourtEfficiencyBonus = 200;

    /// <summary>Days since a team's previous game at which it is considered fully rested.</summary>
    public const int FullyRestedDays = 3;

    /// <summary>Efficiency lost for each day of rest a team is short of <see cref="FullyRestedDays"/>.</summary>
    public const int EfficiencyPerMissingRestDay = 90;

    /// <summary>
    /// The most fatigue may cost a team. A side on the second night of a back-to-back is worse, not
    /// beaten before it starts — an uncapped fatigue term would decide games by the schedule.
    /// </summary>
    public const int MaximumFatiguePenalty = 250;

    /// <summary>Possessions each side gets in a regulation game before variance.</summary>
    public const int BasePossessionsPerGame = 98;

    /// <summary>
    /// How far a game's pace may drift from <see cref="BasePossessionsPerGame"/>, in either
    /// direction. Bounded so that a fast game is a fast game rather than a different sport.
    /// </summary>
    public const int PossessionSpread = 9;

    /// <summary>Possessions each side gets in one overtime period.</summary>
    public const int OvertimePossessions = 11;

    /// <summary>Minutes one overtime period adds.</summary>
    public const int OvertimeMinutes = 5;

    /// <summary>
    /// How many overtime periods are played before the terminal rule below is applied. Six is far
    /// past anything a real game has needed; it exists so the loop is provably finite rather than
    /// relying on a draw eventually breaking.
    /// </summary>
    public const int MaximumOvertimePeriods = 6;

    /// <summary>
    /// What share of possessions produce a field-goal attempt at all, in <see cref="ProbabilityScale"/>
    /// units. The rest score nothing and touch no player's line — the unmodeled stand-in for a
    /// turnover, kept deliberately nameless rather than exposed as a tracked category: nothing in this
    /// build's frozen calibration suite needs a turnover rate, only a way for "possessions" (pace) and
    /// "shots" (the categories below) to be two different numbers, the way they are in real
    /// basketball.
    /// </summary>
    public const int FieldGoalAttemptRate = 9_200;

    /// <summary>
    /// What share of field-goal <em>attempts</em> are three-pointers, in <see cref="ProbabilityScale"/>
    /// units — applied to every attempt, not just makes, unlike the single scoring-rate draw this
    /// replaced. This is the entire answer to <c>fg3a_per_fga</c>: the ratio is stated, not derived.
    /// </summary>
    public const int ThreePointAttemptShare = 4_000;

    /// <summary>
    /// League-average two-point field-goal percentage at a neutral efficiency (no strength edge, no
    /// home court, no fatigue), in <see cref="ProbabilityScale"/> units. Shifted up or down from here
    /// by <see cref="FieldGoalPercentSwingAtMaximumStrength"/> the same way the old scoring rate used
    /// to move with efficiency — a better team, not a team that shoots more often.
    /// </summary>
    public const int BaseTwoPointPercentage = 5_300;

    /// <summary>
    /// League-average three-point percentage at a neutral efficiency. Lower than
    /// <see cref="BaseTwoPointPercentage"/> by a fixed gap that the efficiency swing preserves rather
    /// than closes — a team having a good night gets better at both shots together, not selectively
    /// better from three.
    /// </summary>
    public const int BaseThreePointPercentage = 3_700;

    /// <summary>
    /// How many percentage points a field-goal percentage may move, at the maximum possible strength
    /// edge (<see cref="MaximumStrengthEfficiencySwing"/>) — the swing scales linearly between zero
    /// and this bound as the efficiency term does. Applied identically to both
    /// <see cref="BaseTwoPointPercentage"/> and <see cref="BaseThreePointPercentage"/>, which is what
    /// keeps their gap fixed.
    /// </summary>
    public const int FieldGoalPercentSwingAtMaximumStrength = 800;

    /// <summary>The floor a field-goal percentage may be pushed to, whatever the efficiency terms say.</summary>
    public const int MinimumFieldGoalPercentage = 2_000;

    /// <summary>The ceiling a field-goal percentage may be pushed to.</summary>
    public const int MaximumFieldGoalPercentage = 7_500;

    /// <summary>
    /// Chance a missed field-goal attempt is instead a shooting foul, sending the shooter to the free
    /// throw line for two (a two-point attempt) or three (a three-point attempt) shots rather than
    /// leaving the shot a clean miss.
    /// </summary>
    public const int ShootingFoulChanceOnMiss = 1_500;

    /// <summary>Chance a made field goal draws an additional single free throw — an "and-one."</summary>
    public const int AndOneChanceOnMake = 500;

    /// <summary>
    /// Chance any single free throw is made. Not adjusted by efficiency: a team's shooting touch at
    /// the unguarded line does not vary with how the possession that sent it there went.
    /// </summary>
    public const int FreeThrowPercentage = 7_700;

    /// <summary>
    /// How far a team's field-goal percentage may drift for a single game, in either direction, from
    /// one shared draw applied to every shot that game — a genuinely hot or cold shooting night,
    /// correlated across a team's own possessions rather than each shot rolling independently.
    /// Real shooting nights vary together; a model where every shot is its own independent coin flip
    /// under-produces the game-to-game scoring swings a real season has, which is exactly what
    /// splitting one blended scoring draw into several smaller, independent per-shot draws was
    /// measured to do here. This is the deliberate source of that missing correlation, not a residual
    /// noise term — see <c>docs/architecture.md</c> for the measurement that motivated it.
    /// </summary>
    public const int GameShootingVarianceRange = 850;

    /// <summary>Share of an opponent's missed possessions a team rebounds, in probability units.</summary>
    public const int DefensiveReboundShare = 7_400;

    /// <summary>Share of its own missed possessions a team rebounds, in probability units.</summary>
    public const int OffensiveReboundShare = 2_400;

    /// <summary>Share of made field goals credited to an assist, in probability units.</summary>
    public const int AssistShareOfMadeFieldGoals = 5_800;

    /// <summary>
    /// The floor and ceiling on how far a player's share of their team's scoring may sit from an
    /// even split, as a percentage where 100 is the team's average. Bounded for the §7 reason again:
    /// uncapped, one star on a weak roster takes every possession on the floor.
    /// </summary>
    public const int MinimumUsageFactor = 55;

    /// <inheritdoc cref="MinimumUsageFactor"/>
    public const int MaximumUsageFactor = 210;

    /// <summary>
    /// A player's chance of picking up an injury in a game they play every available minute of, in
    /// probability units. Scaled down by the minutes they actually played, so the risk sits with the
    /// people carrying the load.
    /// </summary>
    public const int InjuryChancePerFullGame = 120;

    /// <summary>The shortest spell an injury can cost.</summary>
    public const int MinimumInjuryDays = 2;

    /// <summary>
    /// The longest spell an injury can cost. Bounded so that one draw cannot end a season: a model
    /// that can delete a franchise's best player for the year on a single roll is a model whose
    /// worst outcome nobody chose.
    /// </summary>
    public const int MaximumInjuryDays = 28;
}
