using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;

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
    public const int ProbabilityScale = IRandomSource.BasisPointScale;

    /// <summary>
    /// What a team scores per hundred possessions before anything about either side is considered,
    /// in <see cref="EfficiencyScale"/> units — so 11,500 is 115.0 points per hundred possessions.
    /// Retuned from 10,800 once real shot attempts and free throws replaced a single blended
    /// scoring-rate draw (see the field-goal percentages below) — the sim audit's own target for this figure, unreachable by the old mechanism no
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
    public const int OvertimeMinutes = BoxScore.OvertimeMinutes;

    /// <summary>
    /// How many overtime periods are played before the terminal rule below is applied. Six is far
    /// past anything a real game has needed; it exists so the loop is provably finite rather than
    /// relying on a draw eventually breaking.
    /// </summary>
    public const int MaximumOvertimePeriods = 6;

    /// <summary>
    /// Chance a possession ends in a turnover before any shot, in <see cref="ProbabilityScale"/> units,
    /// for a neutral ball handler against a neutral defence. Charged to the handler — the player on
    /// the floor the usage factors pick — so a turnover is a credited event, not an unexplained empty
    /// possession. Tuned with the scoring terms: turnovers and shooting share the same possessions.
    /// </summary>
    public const int BaseTurnoverRate = 1_480;

    /// <summary>
    /// How much a ball handler's passing shape moves the turnover chance, per point their Passing sits
    /// above or below their own Overall — a better passer for their level turns it over less. Shape,
    /// not level: level already enters through the strength term, and reading it again here would count
    /// one player's quality twice.
    /// </summary>
    public const int TurnoverPerPassingPoint = 15;

    /// <summary>The most the handler's passing may move the turnover chance, either way.</summary>
    public const int MaximumTurnoverPassingSwing = 300;

    /// <summary>
    /// How much the two fives' quickness moves the turnover chance, per point the defence's mean
    /// quickness shape ((Speed + Lateral quickness) / 2 − Overall) exceeds the offence's — a difference
    /// between the sides, so a league on any rating scale is unaffected.
    /// </summary>
    public const int TurnoverPerQuicknessPoint = 10;

    /// <summary>The most the quickness difference may move the turnover chance, either way.</summary>
    public const int MaximumTurnoverQuicknessSwing = 250;

    /// <summary>Share of turnovers that are steals — a live ball taken by a defender, who is credited.</summary>
    public const int StealShareOfTurnovers = 5_100;

    /// <summary>
    /// How strongly a defender's quickness shape decides who gets the steal: a weight of 100 plus this
    /// per point of shape, clamped to <see cref="MinimumStealWeight"/>–<see cref="MaximumStealWeight"/>.
    /// </summary>
    public const int StealWeightPerQuicknessPoint = 6;

    /// <summary>The floor and ceiling on a defender's steal weight (100 is neutral).</summary>
    public const int MinimumStealWeight = 25;

    /// <inheritdoc cref="MinimumStealWeight"/>
    public const int MaximumStealWeight = 300;

    /// <summary>
    /// Chance an unfouled two-point miss was blocked, in <see cref="ProbabilityScale"/> units, between
    /// two fives of the same height shape. A block is a miss the defence caused, so it relabels a miss
    /// rather than adding one: field-goal percentage is untouched.
    /// </summary>
    public const int BlockChanceOnTwoPointMiss = 1_650;

    /// <summary>Chance an unfouled three-point miss was blocked: rare, and not moved by height.</summary>
    public const int BlockChanceOnThreePointMiss = 250;

    /// <summary>
    /// How much the two fives' height moves the block chance on a two, per point the defence's mean
    /// height shape (Height − Overall) exceeds the offence's — a difference between the sides.
    /// </summary>
    public const int BlockPerHeightPoint = 25;

    /// <summary>The most the height difference may move the block chance on a two, either way.</summary>
    public const int MaximumBlockHeightSwing = 500;

    /// <summary>
    /// How strongly a defender's height shape decides who gets the block, on top of position: a
    /// weight of 100 plus this per point of shape, clamped to <see cref="MinimumBlockWeight"/>–<see cref="MaximumBlockWeight"/>.
    /// </summary>
    public const int BlockWeightPerHeightPoint = 6;

    /// <summary>The floor and ceiling on a defender's height weight for blocks (100 is neutral).</summary>
    public const int MinimumBlockWeight = 25;

    /// <inheritdoc cref="MinimumBlockWeight"/>
    public const int MaximumBlockWeight = 300;

    /// <summary>
    /// Share of blocks knocked out of bounds: the offence keeps the ball, booked as a team offensive
    /// rebound so every miss still ends in exactly one rebound. The rest stay live and are rebounded.
    /// </summary>
    public const int BlockedOutOfBoundsShare = 3_500;

    /// <summary>
    /// Chance a possession that is not turned over draws a defensive foul away from the shot, in
    /// <see cref="ProbabilityScale"/> units. It counts toward the team's fouls for the period; in the
    /// bonus it is <see cref="BonusFreeThrows"/> free throws for the ball handler, otherwise the
    /// offence simply keeps the ball. Shooting fouls are drawn separately, with the shot.
    /// </summary>
    public const int NonShootingFoulRate = 1_120;

    /// <summary>
    /// Share of the turnovers that are not steals which are offensive fouls, charged to the ball
    /// handler as a personal foul. An offensive foul is not a team foul, so it never puts the other
    /// side in the bonus.
    /// </summary>
    public const int OffensiveFoulShareOfLostBalls = 1_800;

    /// <summary>Free throws for a foul committed in the bonus: a property of the sport, not a tuning term.</summary>
    public const int BonusFreeThrows = 2;

    /// <summary>The periods regulation is played in, each with its own team-foul count for the bonus.</summary>
    public const int RegulationPeriods = 4;

    /// <summary>
    /// How strongly a defender's quickness shape decides who commits a foul: a weight of 100 plus this
    /// per point the defender is <em>slower</em> than his level, clamped to
    /// <see cref="MinimumFoulWeight"/>–<see cref="MaximumFoulWeight"/>, on top of position.
    /// </summary>
    public const int FoulWeightPerQuicknessPoint = 5;

    /// <summary>
    /// A defender within this many fouls of the limit is in foul trouble and defends more carefully:
    /// his weight for further fouls drops to <see cref="FoulTroubleWeightPercent"/>. Without it, the
    /// bigs who foul most foul out far more often than real ones, who play the trouble.
    /// </summary>
    public const int FoulTroubleMargin = 2;

    /// <summary>A defender in foul trouble's weight for further fouls, as a percentage of his usual one.</summary>
    public const int FoulTroubleWeightPercent = 40;

    /// <summary>The floor and ceiling on a defender's quickness weight for fouls (100 is neutral).</summary>
    public const int MinimumFoulWeight = 40;

    /// <inheritdoc cref="MinimumFoulWeight"/>
    public const int MaximumFoulWeight = 250;

    /// <summary>
    /// Where a neutral shooter's attempts come from, in <see cref="ProbabilityScale"/> units, summing
    /// to the whole: the two three-point zones together are exactly <see cref="ThreePointAttemptShare"/>,
    /// so the zones redistribute the two-and-three split rather than change it.
    /// </summary>
    public const int RimShare = 2_800;

    /// <inheritdoc cref="RimShare"/>
    public const int PaintShare = 1_700;

    /// <inheritdoc cref="RimShare"/>
    public const int MidRangeShare = 1_500;

    /// <inheritdoc cref="RimShare"/>
    public const int Corner3Share = 1_000;

    /// <inheritdoc cref="RimShare"/>
    public const int AboveBreak3Share = 3_000;

    /// <summary>
    /// How far a zone's make percentage sits from the period's two- or three-point percentage, in
    /// <see cref="ProbabilityScale"/> units. Zero-mean (to within a tenth of a basis point an attempt)
    /// over each kind's neutral shares, so the zones redistribute makes without moving the overall
    /// percentages the scoring terms are tuned to. In the most lopsided matchups a zone can reach
    /// <see cref="MinimumFieldGoalPercentage"/> or <see cref="MaximumFieldGoalPercentage"/>, and those
    /// named clamps then bind; a neutral one sits well inside them (asserted in the bounds tests).
    /// </summary>
    public const int RimPercentageOffset = 1_300;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int PaintPercentageOffset = -994;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int MidRangePercentageOffset = -1_300;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int Corner3PercentageOffset = 300;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int AboveBreak3PercentageOffset = -100;

    /// <summary>
    /// How a shooter's height shape (Height − Overall) moves his shot diet: this many basis points a
    /// point from above-the-break threes to the rim (or back, for a short player), capped either way.
    /// </summary>
    public const int ZoneMixPerHeightPoint = 60;

    /// <inheritdoc cref="ZoneMixPerHeightPoint"/>
    public const int MaximumZoneMixShift = 1_200;

    /// <summary>
    /// How a shooter's height shape moves his make percentage: up at the rim, down from three, this
    /// many basis points a point, capped either way. Shape, so it redistributes rather than adds.
    /// </summary>
    public const int ZonePercentagePerHeightPoint = 15;

    /// <inheritdoc cref="ZonePercentagePerHeightPoint"/>
    public const int MaximumZonePercentageSwing = 300;

    /// <summary>
    /// What share of a neutral shooter's attempts are three-pointers, in <see cref="ProbabilityScale"/>
    /// units: the two three-point zones of his diet. A league's actual <c>fg3a_per_fga</c> also moves
    /// with its high-usage players' height shapes (<see cref="ZoneMixPerHeightPoint"/>), so it is
    /// this figure only for a league of neutral shooters — tune the zone shares, not this sum.
    /// </summary>
    public const int ThreePointAttemptShare = Corner3Share + AboveBreak3Share;

    /// <summary>
    /// League-average two-point field-goal percentage at a neutral efficiency (no strength edge, no
    /// home court, no fatigue), in <see cref="ProbabilityScale"/> units. Shifted up or down from here
    /// by <see cref="FieldGoalPercentSwingAtMaximumStrength"/> the same way the old scoring rate used
    /// to move with efficiency — a better team, not a team that shoots more often.
    /// </summary>
    public const int BaseTwoPointPercentage = 4_720;

    /// <summary>
    /// League-average three-point percentage at a neutral efficiency. Lower than
    /// <see cref="BaseTwoPointPercentage"/> by a fixed gap that the efficiency swing preserves rather
    /// than closes — a team having a good night gets better at both shots together, not selectively
    /// better from three.
    /// </summary>
    public const int BaseThreePointPercentage = 3_360;

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
    public const int ShootingFoulChanceOnMiss = 1_200;

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
    public const int GameShootingVarianceRange = 250;

    /// <summary>
    /// How much the scoreboard pushes back on a lead, per point of it, in <see cref="ProbabilityScale"/>
    /// units of field-goal percentage: the trailing side shoots this much better per point it is
    /// behind, the leading side this much worse per point it is ahead, up to
    /// <see cref="MaximumLeadPressure"/>. Real games regress within themselves — a side well ahead
    /// eases off and plays its bench, a side well behind presses — and without that, independent
    /// possessions alone produce margins far wider than the sport's (measured: a signed margin SD of
    /// about 17 between two equal teams against a target of about 13.5 across a whole league).
    /// Possessions alternate, so the score this reads is the score at that moment of the game.
    /// </summary>
    public const int LeadPressurePerPoint = 25;

    /// <summary>The most the score can move a field-goal percentage, either way — a bounded rubber band, never a guarantee.</summary>
    public const int MaximumLeadPressure = 500;

    /// <summary>
    /// Chance a live miss is rebounded by the shooting side, in <see cref="ProbabilityScale"/> units —
    /// drawn once per live miss, as it happens, so an offensive rebound genuinely extends the
    /// possession (a second chance, scored or not) rather than being dealt out after the game. Of
    /// player rebounds this is exactly the offensive-rebound share (<c>orb_pct</c>), because
    /// <see cref="TeamReboundShare"/> is taken from both ends in the same proportion.
    /// </summary>
    public const int OffensiveReboundRate = 2_650;

    /// <summary>
    /// Share of rebounds, at either end, credited to the team rather than a player — the ball out of
    /// bounds, the loose ball nobody secured. Without it every live miss would need a player's name
    /// on it; with it, every live miss still ends in exactly one rebound, which is the identity
    /// <c>BoxScore.Create</c> enforces.
    /// </summary>
    public const int TeamReboundShare = 500;

    /// <summary>
    /// A possession ends after this many shots even if every one of them was rebounded by the
    /// offence. Reachable only on a run of offensive rebounds whose probability is far below one in a
    /// million, but stated so the loop provably terminates rather than probably does.
    /// </summary>
    public const int MaximumShotsPerPossession = 8;

    /// <summary>
    /// Chance a made field goal was assisted, in <see cref="ProbabilityScale"/> units — decided per
    /// make, with the assist credited to a teammate on the floor, never the shooter. Drawn per make
    /// rather than dealt out of a total afterwards, so assists vary game to game the way they do.
    /// </summary>
    public const int AssistChance = 6_100;

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

    // Positional pick weights: who is on hand for a board, a pass or a block. Named here with every
    // other term so a bounds test can read them.

    /// <summary>
    /// Who gets the ball off the rim. Positional rather than rating-driven: a great point guard
    /// does not out-rebound a mediocre centre, and a box score that said otherwise would read as
    /// obviously wrong to anybody who follows the sport.
    /// </summary>
    public static int ReboundWeightFor(Position position) => position switch
    {
        Position.Center => 130,
        Position.PowerForward => 120,
        Position.SmallForward => 95,
        Position.ShootingGuard => 80,
        _ => 75,
    };

    /// <summary>Who is at the rim to block a shot: far more positional than rebounding.</summary>
    public static int BlockWeightFor(Position position) => position switch
    {
        Position.Center => 200,
        Position.PowerForward => 140,
        Position.SmallForward => 85,
        Position.ShootingGuard => 50,
        _ => 35,
    };

    public static int AssistWeightFor(Position position) => position switch
    {
        Position.PointGuard => 150,
        Position.ShootingGuard => 110,
        Position.SmallForward => 95,
        Position.PowerForward => 80,
        _ => 65,
    };

    /// <summary>Who commits a foul: the big men at the rim more than the guards on the perimeter.</summary>
    public static int FoulWeightFor(Position position) => position switch
    {
        Position.Center => 140,
        Position.PowerForward => 125,
        Position.SmallForward => 100,
        Position.ShootingGuard => 90,
        _ => 85,
    };
}
