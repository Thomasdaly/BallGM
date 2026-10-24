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
    /// height shape (<see cref="HeightShapeFor"/>: centimetres from average) exceeds the offence's — a difference between the sides.
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
    public const int FoulWeightPerQuicknessPoint = 2;

    /// <summary>
    /// A defender within this many fouls of the limit is in foul trouble and defends more carefully:
    /// his weight for further fouls drops to <see cref="FoulTroubleWeightPercent"/>. Without it, the
    /// bigs who foul most foul out far more often than real ones, who play the trouble.
    /// </summary>
    public const int FoulTroubleMargin = 2;

    /// <summary>A defender in foul trouble's weight for further fouls, as a percentage of his usual one.</summary>
    public const int FoulTroubleWeightPercent = 40;

    /// <summary>The floor and ceiling on a defender's quickness weight for fouls (100 is neutral).</summary>
    public const int MinimumFoulWeight = 60;

    /// <inheritdoc cref="MinimumFoulWeight"/>
    public const int MaximumFoulWeight = 180;

    /// <summary>
    /// Where a neutral shooter's attempts come from, in <see cref="ProbabilityScale"/> units, summing
    /// to the whole: the two three-point zones together are exactly <see cref="ThreePointAttemptShare"/>,
    /// so the zones redistribute the two-and-three split rather than change it.
    /// </summary>
    public const int RimShare = 2_900;

    /// <inheritdoc cref="RimShare"/>
    public const int PaintShare = 1_800;

    /// <inheritdoc cref="RimShare"/>
    public const int MidRangeShare = 1_600;

    /// <inheritdoc cref="RimShare"/>
    public const int Corner3Share = 925;

    /// <inheritdoc cref="RimShare"/>
    public const int AboveBreak3Share = 2_775;

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
    public const int PaintPercentageOffset = -939;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int MidRangePercentageOffset = -1_300;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int Corner3PercentageOffset = 300;

    /// <inheritdoc cref="RimPercentageOffset"/>
    public const int AboveBreak3PercentageOffset = -100;

    /// <summary>The cap on a height shape either way, so a giant is tall, not a different sport.</summary>
    public const int MaximumHeightShape = 60;

    /// <summary>
    /// The height shape every size coupling reads (zone diet, block chance and credit): how much taller
    /// than average a player is (<see cref="PlayerBody.HeightShape"/>), capped. A sheet with no body (a
    /// flat test fixture) is average size, so its couplings stay silent.
    /// </summary>
    public static int HeightShapeFor(PlayerBody? body) => body is null
        ? 0
        : Math.Clamp(body.HeightShape, -MaximumHeightShape, MaximumHeightShape);

    /// <summary>
    /// The floor and ceiling on how far a shot-location tendency scales its zones' share of a player's
    /// attempts, as a percentage of the neutral share (100 at the league's middle tendency of 50; a
    /// tendency of 25 halves it, 75 makes it half again). A big who never shoots threes still takes the
    /// odd one; a shooter still gets to the rim.
    /// </summary>
    public const int MinimumDietMultiplier = 20;

    /// <inheritdoc cref="MinimumDietMultiplier"/>
    public const int MaximumDietMultiplier = 160;

    /// <summary>A shot-location tendency's multiplier on its zones' neutral share, capped by the two bounds above.</summary>
    public static int DietMultiplierFor(int tendency) =>
        Math.Clamp(tendency * 100 / PlayerRating.NeutralTendency, MinimumDietMultiplier, MaximumDietMultiplier);

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
    /// with its high-usage players' shot-location tendencies (<see cref="DietMultiplierFor"/>), so it is
    /// this figure only for a league of neutral shooters — tune the zone shares, not this sum.
    /// </summary>
    public const int ThreePointAttemptShare = Corner3Share + AboveBreak3Share;

    /// <summary>
    /// League-average two-point field-goal percentage at a neutral efficiency (no strength edge, no
    /// home court, no fatigue), in <see cref="ProbabilityScale"/> units. Shifted up or down from here
    /// by <see cref="FieldGoalPercentSwingAtMaximumStrength"/> the same way the old scoring rate used
    /// to move with efficiency — a better team, not a team that shoots more often.
    /// </summary>
    public const int BaseTwoPointPercentage = 4_800;

    /// <summary>
    /// League-average three-point percentage at a neutral efficiency. Lower than
    /// <see cref="BaseTwoPointPercentage"/> by a fixed gap that the efficiency swing preserves rather
    /// than closes — a team having a good night gets better at both shots together, not selectively
    /// better from three.
    /// </summary>
    public const int BaseThreePointPercentage = 3_420;

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
    public const int ShootingFoulChanceOnMiss = 850;

    /// <summary>Chance a made field goal draws an additional single free throw — an "and-one."</summary>
    public const int AndOneChanceOnMake = 250;

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
    public const int InjuryChancePerFullGame = 180;

    /// <summary>
    /// How far a player's injury proneness (hidden, 50 the league's middle) moves his injury chance:
    /// percentage points per point from the middle, within
    /// <see cref="MinimumInjuryPronenessFactor"/>–<see cref="MaximumInjuryPronenessFactor"/> per cent.
    /// The fragile miss a month a season; the iron men play every night.
    /// </summary>
    public const int InjuryPronenessPerPoint = 3;

    /// <inheritdoc cref="InjuryPronenessPerPoint"/>
    public const int MinimumInjuryPronenessFactor = 40;

    /// <inheritdoc cref="InjuryPronenessPerPoint"/>
    public const int MaximumInjuryPronenessFactor = 220;

    /// <summary>The shortest spell an injury can cost.</summary>
    public const int MinimumInjuryDays = 2;

    /// <summary>
    /// The longest spell an ordinary injury can cost: most knocks are days to a few weeks.
    /// </summary>
    /// <remarks>
    /// This bound used to be the whole model — "one draw cannot end a season" — on the reasoning that a
    /// model which can delete a franchise's best player for the year on one roll has an outcome nobody
    /// chose. Reversed on the owner's request (<c>docs/franchise-realism.md</c> → F9): real leagues have
    /// season-ending and career-altering injuries, and the trades, buyouts and gambles that follow them
    /// are part of running a franchise. They now come from the separate, rare severe tier below.
    /// </remarks>
    public const int MaximumInjuryDays = 28;

    /// <summary>
    /// The share of injuries, in basis points, that are severe — a torn ligament, a ruptured tendon, a
    /// broken bone — before injury proneness scales it (which also rises with age). Around one injury in
    /// twenty-five: a league sees a couple of dozen long injuries a season, a handful of them
    /// season-ending.
    /// </summary>
    public const int SevereInjuryShare = 400;

    /// <summary>The shortest spell a severe injury costs: six weeks and more.</summary>
    public const int MinimumSevereInjuryDays = 45;

    /// <summary>
    /// The longest spell a severe injury costs: under a year, the length of the worst real recoveries.
    /// Drawn as the shorter of two draws, so most severe injuries are months, not the year.
    /// </summary>
    public const int MaximumSevereInjuryDays = 330;

    // Player couplings (attributes A3–A6). Each reads a player's sheet against his team-mates' (a
    // difference from the rotation's mean, through RelativeWeight, so a pack on its own scale still plays
    // basketball) or against his own level (a shape), and each is capped on its own. A "doubling points"
    // constant is how many points above the rotation's mean double a weight.

    /// <summary>
    /// Doubling points for a player's playmaking (handle, passing, vision) against his rotation's
    /// mean in his weight to bring the ball up (with his touch tendency and position): the best creator
    /// on a team runs most of its possessions, not merely a few more of them.
    /// </summary>
    public const int HandlerDoublingPoints = 18;

    /// <summary>The floor and ceiling on a player's handler weight, as a percentage of an even split.</summary>
    public const int MinimumHandlerFactor = 20;

    /// <inheritdoc cref="MinimumHandlerFactor"/>
    public const int MaximumHandlerFactor = 450;

    /// <summary>
    /// The chance the ball handler takes the possession's first shot himself rather than passing it to
    /// a team-mate (picked by usage), for a player of neutral shot and pass tendencies — moved by
    /// <see cref="SelfShotPerShotTendencyPoint"/> and <see cref="SelfShotPerPassTendencyPoint"/>, within
    /// <see cref="MinimumSelfShotShare"/>–<see cref="MaximumSelfShotShare"/>.
    /// </summary>
    public const int BaseSelfShotShare = 3_600;

    /// <inheritdoc cref="BaseSelfShotShare"/>
    public const int SelfShotPerShotTendencyPoint = 50;

    /// <inheritdoc cref="BaseSelfShotShare"/>
    public const int SelfShotPerPassTendencyPoint = 40;

    /// <inheritdoc cref="BaseSelfShotShare"/>
    public const int MinimumSelfShotShare = 1_200;

    /// <inheritdoc cref="BaseSelfShotShare"/>
    public const int MaximumSelfShotShare = 7_000;

    /// <summary>
    /// Chance a make off the handler's pass is credited to him as an assist, moved by his passing shape
    /// (<see cref="AssistPerPassingPoint"/>, capped at <see cref="MaximumAssistPassingSwing"/>): a
    /// pass that leads straight to a basket is an assist; one the shooter still had to beat a man with
    /// is not.
    /// </summary>
    public const int AssistChanceOnPass = 7_400;

    /// <summary>
    /// Chance a make the handler created for himself (or a second-chance shot) was still assisted —
    /// a hand-off, a cut, an outlet — credited to a team-mate by assist weight.
    /// </summary>
    public const int AssistChanceOnOwnShot = 3_900;

    /// <inheritdoc cref="AssistChanceOnPass"/>
    public const int AssistPerPassingPoint = 30;

    /// <inheritdoc cref="AssistChanceOnPass"/>
    public const int MaximumAssistPassingSwing = 900;

    /// <summary>
    /// Doubling points for a player's Current Ability and for his touch tendency, each against his
    /// rotation's mean, in his usage weight: the best player takes far more than his share of the shots,
    /// and a player who wants the ball gets it a little more often than one who does not.
    /// </summary>
    public const int UsageRatingDoublingPoints = 18;

    /// <inheritdoc cref="UsageRatingDoublingPoints"/>
    public const int UsageTouchesDoublingPoints = 10;

    /// <summary>
    /// Doubling points for how far a player's defensive discipline falls short of his rotation's mean
    /// in his foul weight, within <see cref="MinimumFoulDisciplineFactor"/>–<see cref="MaximumFoulDisciplineFactor"/> per cent.
    /// </summary>
    public const int FoulDisciplineDoublingPoints = 14;

    /// <inheritdoc cref="FoulDisciplineDoublingPoints"/>
    public const int MinimumFoulDisciplineFactor = 70;

    /// <inheritdoc cref="FoulDisciplineDoublingPoints"/>
    public const int MaximumFoulDisciplineFactor = 150;

    /// <summary>
    /// How much a player's usage weight (as a percentage of an even share) also weights his level in his
    /// team's strength, in per cent of its distance from even: at 50, a first option taking twice an even
    /// share counts one and a half times a role player's minutes. Bounded below 100 so strength stays a
    /// weighted mean of the rotation, never one player's rating.
    /// </summary>
    public const int StarStrengthWeightPercent = 50;

    /// <summary>
    /// How a player's level above his rotation's average counts toward team strength beyond its face
    /// value: the square of the gap over this divisor, capped at <see cref="MaximumStarBonus"/>. A star
    /// twenty-five points above his team-mates adds twenty-five more; a solid starter five above, one.
    /// </summary>
    public const int StarConvexityDivisor = 12;

    /// <inheritdoc cref="StarConvexityDivisor"/>
    public const int MaximumStarBonus = 30;

    /// <summary>
    /// Share of turnovers charged to the ball handler; the rest go to the team-mate he was getting it to
    /// (picked by usage) — the bad catch, the travel, the offensive foul off the ball.
    /// </summary>
    public const int HandlerTurnoverShare = 4_000;

    /// <summary>
    /// Doubling points for a player's shot-blocking against his rotation's mean in his positional
    /// block weight, within
    /// <see cref="MinimumBlockSkillFactor"/>–<see cref="MaximumBlockSkillFactor"/> per cent.
    /// </summary>
    public const int BlockDoublingPoints = 5;

    /// <inheritdoc cref="BlockDoublingPoints"/>
    public const int MinimumBlockSkillFactor = 60;

    /// <inheritdoc cref="BlockDoublingPoints"/>
    public const int MaximumBlockSkillFactor = 900;

    /// <summary>
    /// How the way a shot came moves a shooter's diet, as percentages of his own shares: one he made
    /// for himself (off the dribble, a put-back, a reset) leans to the rim and the pull-up two; one he
    /// caught off a pass leans to the spot-up three. This is what keeps a league's three-point makers
    /// spread across its role players rather than piled on its first options.
    /// </summary>
    public const int OwnCloseMultiplier = 110;

    /// <inheritdoc cref="OwnCloseMultiplier"/>
    public const int OwnMidMultiplier = 120;

    /// <inheritdoc cref="OwnCloseMultiplier"/>
    public const int OwnThreeMultiplier = 80;

    /// <inheritdoc cref="OwnCloseMultiplier"/>
    public const int CatchCloseMultiplier = 95;

    /// <inheritdoc cref="OwnCloseMultiplier"/>
    public const int CatchMidMultiplier = 85;

    /// <inheritdoc cref="OwnCloseMultiplier"/>
    public const int CatchThreeMultiplier = 112;

    /// <summary>
    /// Chance the player who grabbed an offensive rebound takes the second-chance shot himself — a
    /// put-back — rather than the offence resetting to whoever usage picks.
    /// </summary>
    public const int PutBackChance = 3_500;

    /// <summary>
    /// Doubling points for a player's defensive rebounding plus boxing out against his rotation's
    /// mean in his positional rebound weight:
    /// the best rebounder on the floor gets far more than his share, which is how a league has a
    /// thirteen-rebound leader.
    /// </summary>
    public const int ReboundDoublingPoints = 60;

    /// <summary>
    /// Doubling points for offensive rebounding: steeper than at the defensive end, where position and
    /// boxing out spread the boards; on the offensive glass a few players who crash it get most of them.
    /// </summary>
    public const int OffensiveReboundDoublingPoints = 14;

    /// <summary>The floor and ceiling on how far rebounding moves a positional rebound weight, as a percentage.</summary>
    public const int MinimumReboundSkillFactor = 30;

    /// <inheritdoc cref="MinimumReboundSkillFactor"/>
    public const int MaximumReboundSkillFactor = 400;

    /// <summary>
    /// Basis points a point of shooting shape (the zone's skill less the shooter's own level —
    /// finishing at the rim, close range in the paint, mid-range, three-point) adds to his make
    /// percentage from that zone, capped either way at <see cref="MaximumShootingSkillSwing"/>.
    /// Shape rather than level, so it redistributes makes among players without making a side better.
    /// </summary>
    public const int ShootingSkillPerPoint = 20;

    /// <inheritdoc cref="ShootingSkillPerPoint"/>
    public const int MaximumShootingSkillSwing = 600;

    /// <summary>
    /// Basis points a point of free-throw shape (free throw less level) moves a player's free-throw
    /// percentage from <see cref="FreeThrowPercentage"/>, inside
    /// <see cref="MinimumFreeThrowPercentage"/>–<see cref="MaximumFreeThrowPercentage"/>.
    /// </summary>
    public const int FreeThrowPerPoint = 70;

    /// <inheritdoc cref="FreeThrowPerPoint"/>
    public const int MinimumFreeThrowPercentage = 4_000;

    /// <inheritdoc cref="FreeThrowPerPoint"/>
    public const int MaximumFreeThrowPercentage = 9_400;

    /// <summary>
    /// How much more often a shot from each zone draws a shooting foul or an and-one than the stated
    /// base rates, as a percentage: attacking the rim draws contact, a jump shot rarely does.
    /// </summary>
    public static int FoulDrawWeightFor(ShotZone zone) => zone switch
    {
        ShotZone.Rim => 300,
        ShotZone.Paint => 160,
        ShotZone.MidRange => 55,
        _ => 25,
    };

    /// <summary>
    /// How much a player's attacking tendency (the larger of drive and post-up) moves his chance of
    /// drawing a shooting foul, as percentage points per tendency point from the middle, inside
    /// <see cref="MinimumFoulDrawFactor"/>–<see cref="MaximumFoulDrawFactor"/>.
    /// </summary>
    public const int FoulDrawPerTendencyPoint = 8;

    /// <summary>Doubling points for a player's Current Ability against his rotation's mean in his foul-draw factor.</summary>
    public const int FoulDrawRatingDoublingPoints = 21;

    /// <inheritdoc cref="FoulDrawPerTendencyPoint"/>
    public const int MinimumFoulDrawFactor = 35;

    /// <inheritdoc cref="FoulDrawPerTendencyPoint"/>
    public const int MaximumFoulDrawFactor = 420;

    /// <summary>
    /// A weight from how far a value stands above (or below) its rotation's mean: 100 at the mean,
    /// doubling every <paramref name="doublingPoints"/> above it and halving every as many below.
    /// A difference rather than a ratio, so a pack rated twenty points higher across the board plays
    /// exactly the same game (<c>RunningTheSameRatingsAtADifferentScaleDoesNotChangeTheSport</c>).
    /// Integer throughout: whole doublings are shifts, the fraction between them is interpolated.
    /// </summary>
    public static int RelativeWeight(int value, int mean, int doublingPoints)
    {
        var steps = (long)(value - mean) * 1_000 / Math.Max(1, doublingPoints);
        var whole = (int)Math.Clamp(steps >= 0 ? steps / 1_000 : -((-steps + 999) / 1_000), -20, 20);
        var fraction = steps - (whole * 1_000L);
        var weight = whole >= 0 ? 100_000L << whole : 100_000L >> -whole;
        return (int)Math.Min(int.MaxValue, weight * (1_000 + Math.Clamp(fraction, 0, 1_000)) / 1_000 / 1_000);
    }

    // Positional pick weights: who is on hand for a board, a pass or a block. Named here with every
    // other term so a bounds test can read them.

    /// <summary>
    /// Who gets the ball off the rim. Positional rather than rating-driven: a great point guard
    /// does not out-rebound a mediocre centre, and a box score that said otherwise would read as
    /// obviously wrong to anybody who follows the sport.
    /// </summary>
    public static int ReboundWeightFor(Position position) => position switch
    {
        Position.Center => 145,
        Position.PowerForward => 105,
        Position.SmallForward => 85,
        Position.ShootingGuard => 75,
        _ => 70,
    };

    /// <summary>Who is at the rim to block a shot: far more positional than rebounding.</summary>
    public static int BlockWeightFor(Position position) => position switch
    {
        Position.Center => 130,
        Position.PowerForward => 115,
        Position.SmallForward => 100,
        Position.ShootingGuard => 85,
        _ => 75,
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
        Position.Center => 125,
        Position.PowerForward => 115,
        Position.SmallForward => 100,
        Position.ShootingGuard => 92,
        _ => 88,
    };
}
