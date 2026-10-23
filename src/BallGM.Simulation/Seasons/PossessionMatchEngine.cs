using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Simulation.Seasons;

/// <summary>
/// Decides a game by playing its possessions.
/// <para>
/// <b>Possession-based rather than score-based, on purpose.</b> The alternative — draw a total for
/// each side from a distribution around their strengths — is a third of the code and produces
/// plausible final scores, but it cannot produce a box score: there is nothing underneath the total
/// to attribute to anybody, so the player lines have to be invented afterwards and reconciled back
/// to a number that was decided without them. Simulating possessions means the box score
/// <em>is</em> the game, the totals are a sum rather than a target, and pace becomes a real
/// property a fast league can differ from a slow one on.
/// </para>
/// <para>
/// <b>Every term is bounded and named in <see cref="MatchModelBounds"/></b>, which is the whole
/// point of that type: <c>docs/competitive-feature-review.md</c> §7 records a competitor shipping an
/// outcome probability one input could dominate without a cap. Strength, home advantage, fatigue and
/// usage each have a stated ceiling here, so no single term can decide a game on its own.
/// </para>
/// <para>
/// <b>Integer arithmetic throughout.</b> Efficiencies are points per ten thousand possessions and
/// probabilities are basis points, so nothing about a result depends on floating-point rounding and
/// the same seed gives the same game on every platform. That guarantee is not decorative: the whole
/// season's reproducibility rests on it, and <c>SeedMixer</c> derives this game's seed precisely so
/// that it does not matter what order the season's games were simulated in.
/// </para>
/// <para>
/// <b>Strength is relative, never absolute.</b> Only the difference between the two sides enters the
/// efficiency, so a league of 45-rated players and a league of 90-rated players both produce
/// recognisable scorelines. A model keyed on absolute rating would send one league to nothing and
/// the other off the top of the scoreboard, and a modder shipping a data pack on a different rating
/// scale would find the sport had stopped working.
/// </para>
/// </summary>
public sealed class PossessionMatchEngine : IMatchEngine
{
    public const string EmptyRotationCode = "match.team_has_no_rotation";

    /// <summary>
    /// The knocks a game can produce. Fictional and generic by design — the safety boundary in
    /// <c>CLAUDE.md</c> is about names and branding, and an injury description is player-facing text
    /// that should read like a team report rather than a medical record.
    /// </summary>
    private static readonly string[] InjuryDescriptions =
    [
        "ankle sprain",
        "knee soreness",
        "hamstring strain",
        "lower back spasms",
        "shoulder strain",
        "wrist sprain",
        "hip pointer",
        "calf tightness",
    ];

    public bool CanPlay => true;

    public DomainOperationResult<MatchOutcome> Play(MatchSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var rules = setup.Rules;
        var homeResult = BuildSide(setup.Home, rules.PersonalFoulLimit);
        if (homeResult.IsFailure)
        {
            return DomainOperationResult<MatchOutcome>.Failure(homeResult.Errors.ToArray());
        }

        var awayResult = BuildSide(setup.Away, rules.PersonalFoulLimit);
        if (awayResult.IsFailure)
        {
            return DomainOperationResult<MatchOutcome>.Failure(awayResult.Errors.ToArray());
        }

        var home = homeResult.Value;
        var away = awayResult.Value;
        IRandomSource random = new SeededRandomSource(setup.Seed);

        var homeEfficiency = EfficiencyFor(home, away, isHome: true);
        var awayEfficiency = EfficiencyFor(away, home, isHome: false);

        var possessions = MatchModelBounds.BasePossessionsPerGame +
            random.NextInt32(-MatchModelBounds.PossessionSpread, MatchModelBounds.PossessionSpread + 1);

        home.PlanRegulation(possessions);
        away.PlanRegulation(possessions);
        PlayPeriod(home, away, homeEfficiency, awayEfficiency, possessions, MatchModelBounds.RegulationPeriods, rules.BonusTeamFoulsPerPeriod, random);

        // A drawn game is refused by GameResult, every standings tie-break assumes a winner, and a
        // postseason series that could not be won would never end. So the tie is played out rather
        // than nudged: overtime is more possessions on the same terms.
        var overtimePeriods = 0;

        while (home.Points == away.Points && overtimePeriods < MatchModelBounds.MaximumOvertimePeriods)
        {
            overtimePeriods++;
            home.SendInClosers();
            away.SendInClosers();
            PlayPeriod(home, away, homeEfficiency, awayEfficiency, MatchModelBounds.OvertimePossessions, periods: 1, rules.BonusTeamFoulsPerOvertime, random);
        }

        if (home.Points == away.Points)
        {
            // Six overtimes past anything a real game has needed. The loop has to terminate, so the
            // terminal rule is stated rather than left to a seventh draw: the more efficient side
            // converts one last possession, and the home team holds an exact tie.
            var awayWins = awayEfficiency > homeEfficiency;
            AwardBasket(awayWins ? away : home, awayWins ? home : away, random);
        }

        home.CreditMinutes(possessions, overtimePeriods);
        away.CreditMinutes(possessions, overtimePeriods);

        var lines = home.ToStatLines().Concat(away.ToStatLines()).ToList();

        var boxScoreResult = BoxScore.Create(
            setup.Fixture.Id,
            home.Points,
            away.Points,
            lines,
            home.ToTeamLine(),
            away.ToTeamLine(),
            overtimePeriods,
            rules.PersonalFoulLimit);

        if (boxScoreResult.IsFailure)
        {
            return DomainOperationResult<MatchOutcome>.Failure(boxScoreResult.Errors.ToArray());
        }

        var resultResult = GameResult.Create(setup.Fixture, home.Points, away.Points, boxScoreResult.Value);
        if (resultResult.IsFailure)
        {
            return DomainOperationResult<MatchOutcome>.Failure(resultResult.Errors.ToArray());
        }

        var injuries = Injuries(home, random).Concat(Injuries(away, random)).ToList();

        return DomainOperationResult<MatchOutcome>.Success(new MatchOutcome(resultResult.Value, injuries));
    }

    /// <summary>
    /// What this team scores per hundred possessions against this opponent, in
    /// <see cref="MatchModelBounds.EfficiencyScale"/> units. Every term is clamped before it is
    /// added, so the bound on each one holds independently of the others rather than only in total.
    /// </summary>
    private static int EfficiencyFor(Side side, Side opponent, bool isHome)
    {
        var strengthTerm = Math.Clamp(
            (side.Strength - opponent.Strength) * MatchModelBounds.EfficiencyPerRatingPoint / 2,
            -MatchModelBounds.MaximumStrengthEfficiencySwing,
            MatchModelBounds.MaximumStrengthEfficiencySwing);

        var missingRest = Math.Max(0, MatchModelBounds.FullyRestedDays - side.RestDays);

        var fatigueTerm = Math.Min(
            missingRest * MatchModelBounds.EfficiencyPerMissingRestDay,
            MatchModelBounds.MaximumFatiguePenalty);

        var homeTerm = isHome ? MatchModelBounds.HomeCourtEfficiencyBonus : 0;

        return MatchModelBounds.BaseOffensiveEfficiency + strengthTerm + homeTerm - fatigueTerm;
    }

    /// <summary>
    /// Plays a run of possessions, home and away alternating. A possession can be turned over before
    /// it reaches a shot (charged to the ball handler, sometimes a steal; see
    /// <see cref="MatchModelBounds.BaseTurnoverRate"/>), and one that reaches a shot is a real
    /// field-goal attempt at a real percentage, not a single blended score/miss draw.
    /// </summary>
    private static void PlayPeriod(
        Side home, Side away, int homeEfficiency, int awayEfficiency, int possessions, int periods, int bonusAfter, IRandomSource random)
    {
        // Each side's hot or cold night is drawn once for the whole period, home first, before a
        // single possession: a night moves every shot together (see GameShootingVarianceRange).
        var homeShooting = NightlyShooting(homeEfficiency, random);
        var awayShooting = NightlyShooting(awayEfficiency, random);

        // Possessions alternate, as they do on a court, so that whoever is on the floor at any moment
        // is on the floor for both ends of it.
        var period = -1;
        for (var possession = 0; possession < possessions; possession++)
        {
            // Team fouls count afresh each period, for the bonus.
            var current = possession * periods / possessions;
            if (current != period)
            {
                period = current;
                home.ResetTeamFouls();
                away.ResetTeamFouls();
            }

            // One possession at each end, with the same ten players on the floor for both.
            home.Enter(possession);
            away.Enter(possession);
            PlayPossession(home, away, homeShooting, bonusAfter, random);
            PlayPossession(away, home, awayShooting, bonusAfter, random);
        }
    }

    /// <summary>
    /// One side's shooting for one period: the efficiency-adjusted base percentages, moved together
    /// by one shared draw rather than per shot, which is what gives games the score-to-score variance
    /// a real season has (see <see cref="MatchModelBounds.GameShootingVarianceRange"/>).
    /// </summary>
    private static (int Fg2Percent, int Fg3Percent) NightlyShooting(int efficiency, IRandomSource random)
    {
        var (baseFg2Percent, baseFg3Percent) = FieldGoalPercentagesFor(efficiency);
        var nightlyVariance = random.NextInt32(-MatchModelBounds.GameShootingVarianceRange, MatchModelBounds.GameShootingVarianceRange + 1);

        return (
            Math.Clamp(baseFg2Percent + nightlyVariance, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage),
            Math.Clamp(baseFg3Percent + nightlyVariance, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage));
    }

    /// <summary>
    /// One possession, every event recorded where it happens. It may end in a turnover before any
    /// shot — charged to the ball handler, and a steal credited to a defender when the ball was taken
    /// live; once a shot goes up, the possession lasts as long as the offence keeps the ball — every
    /// offensive rebound is a second chance on the same possession.
    /// </summary>
    private static void PlayPossession(Side offence, Side defence, (int Fg2Percent, int Fg3Percent) shooting, int bonusAfter, IRandomSource random)
    {
        // The ball handler is whoever runs the offence most (see HandlerFactors): the creator, not
        // necessarily the scorer.
        var handler = offence.PickOnFloor(offence.HandlerFactors, random);

        if (random.NextBasisPoint() < TurnoverRate(offence, defence, handler))
        {
            // Mostly the handler's; otherwise whoever he was getting it to (picked by usage).
            var loser = random.NextBasisPoint() < MatchModelBounds.HandlerTurnoverShare
                ? handler
                : offence.PickOnFloor(offence.UsageFactors, random);
            offence.TurnoversBy[loser]++;

            if (random.NextBasisPoint() < MatchModelBounds.StealShareOfTurnovers)
            {
                defence.StealsBy[defence.PickOnFloor(defence.StealFactors, random)]++;
            }
            else if (random.NextBasisPoint() < MatchModelBounds.OffensiveFoulShareOfLostBalls)
            {
                offence.ChargeFoul(loser);
            }

            return;
        }

        // A foul away from the shot: a team foul either way, and in the bonus a trip to the line for
        // the ball handler; otherwise the offence keeps the ball and plays on.
        var firstShooter = handler;
        if (random.NextBasisPoint() < MatchModelBounds.NonShootingFoulRate && defence.DefensiveFoul(random) > bonusAfter)
        {
            var keptTheBall = ResolveFreeThrowTrip(offence, defence, handler, MatchModelBounds.BonusFreeThrows, random) && Rebound(offence, defence, random);
            if (!keptTheBall)
            {
                return;
            }

            // A second chance off his own missed free throw: whoever the usage factors pick shoots it.
            firstShooter = -1;
        }

        // The handler either shoots it himself or passes it to a team-mate usage picks — and a make off
        // his pass may be his assist. A second chance after an offensive rebound is a put-back by the
        // rebounder, or a reset to whoever usage picks then.
        for (var shot = 0; shot < MatchModelBounds.MaximumShotsPerPossession; shot++)
        {
            int shooter;
            var passer = -1;
            if (shot == 0 && firstShooter >= 0)
            {
                if (random.NextBasisPoint() < offence.SelfShotShares[handler])
                {
                    shooter = handler;
                }
                else
                {
                    shooter = offence.PickTeammateOnFloor(offence.UsageFactors, handler, random);
                    passer = shooter == handler ? -1 : handler;
                }
            }
            else if (offence.LastRebounder >= 0 && random.NextBasisPoint() < MatchModelBounds.PutBackChance)
            {
                shooter = offence.LastRebounder;
            }
            else
            {
                shooter = offence.PickOnFloor(offence.UsageFactors, random);
            }

            if (!TakeShot(offence, defence, shooter, passer, shooting, random))
            {
                return;
            }
        }
    }

    /// <summary>
    /// The chance this possession is turned over: the base rate, moved by the handler's passing shape
    /// and by the two fives' quickness shapes against each other. Each term is capped on its own, so
    /// the rate stays within the base plus or minus both caps (asserted in <c>MatchModelBoundsTests</c>).
    /// </summary>
    private static int TurnoverRate(Side offence, Side defence, int handler)
    {
        var passing = Swing(-offence.PassingShape[handler], MatchModelBounds.TurnoverPerPassingPoint, MatchModelBounds.MaximumTurnoverPassingSwing);
        var quickness = Swing(defence.OnFloorQuickness - offence.OnFloorQuickness, MatchModelBounds.TurnoverPerQuicknessPoint, MatchModelBounds.MaximumTurnoverQuicknessSwing);

        return MatchModelBounds.BaseTurnoverRate + passing + quickness;
    }

    /// <summary>A shape difference turned into basis points, capped either way: the form every attribute coupling takes.</summary>
    private static int Swing(int difference, int perPoint, int cap) => Math.Clamp(difference * perPoint, -cap, cap);

    /// <summary>A per-player pick weight from a shape: 100 is neutral, moved per point and clamped.</summary>
    private static int ShapeWeight(int shape, int perPoint, int minimum, int maximum) => Math.Clamp(100 + (shape * perPoint), minimum, maximum);

    /// <summary>
    /// One shot and everything it leads to. Returns whether the offence still has the ball — only an
    /// offensive rebound (a player's or the team's) keeps the possession alive.
    /// <para>
    /// The attempt is charged only once its outcome is known: a shooting foul on a miss is a trip to
    /// the line, not a field-goal attempt, by the scoring convention every box score follows. Charging
    /// it first, as this engine once did, put a phantom miss in the box score that no rebound could
    /// ever account for.
    /// </para>
    /// </summary>
    private static bool TakeShot(Side offence, Side defence, int shooter, int passer, (int Fg2Percent, int Fg3Percent) shooting, IRandomSource random)
    {
        // Where he shoots from: his own diet, which his shot-location tendencies shape; how well, the
        // period's percentage moved by his height and by his skill from that zone.
        var zone = offence.ZoneFor(shooter, passer >= 0, random.NextBasisPoint());
        var isThree = ShotZones.IsThree(zone);
        var made = random.NextBasisPoint() <
            MakePercentage(ZonePercentage(zone, shooting, offence.HeightShape[shooter]) + offence.ShootingSkill[(int)zone][shooter], offence, defence);

        if (made)
        {
            ChargeAttempt(offence, shooter, zone);
            AwardMake(offence, defence, shooter, zone);

            if (passer >= 0)
            {
                if (random.NextBasisPoint() < offence.PassAssistChances[passer])
                {
                    offence.AssistsBy[passer]++;
                }
            }
            else if (random.NextBasisPoint() < MatchModelBounds.AssistChanceOnOwnShot)
            {
                offence.AssistsBy[offence.PickTeammateOnFloor(offence.AssistFactors, shooter, random)]++;
            }

            // An and-one: a make that also draws a single free throw. Missed, it is a live ball.
            if (random.NextBasisPoint() >= offence.AndOneChances[(int)zone][shooter])
            {
                return false;
            }

            defence.DefensiveFoul(random);
            return ResolveFreeThrowTrip(offence, defence, shooter, shots: 1, random) && Rebound(offence, defence, random);
        }

        if (random.NextBasisPoint() < offence.ShootingFoulChances[(int)zone][shooter])
        {
            defence.DefensiveFoul(random);
            return ResolveFreeThrowTrip(offence, defence, shooter, isThree ? 3 : 2, random) && Rebound(offence, defence, random);
        }

        ChargeAttempt(offence, shooter, zone);

        // An unfouled miss may have been blocked: credited to a defender on the floor, and sometimes
        // knocked out of bounds, which hands the ball back to the offence as a team rebound.
        if (random.NextBasisPoint() < BlockRate(offence, defence, isThree))
        {
            defence.BlocksBy[defence.PickOnFloor(defence.BlockFactors, random)]++;

            if (random.NextBasisPoint() < MatchModelBounds.BlockedOutOfBoundsShare)
            {
                offence.TeamOffensiveRebounds++;
                return true;
            }
        }

        return Rebound(offence, defence, random);
    }

    /// <summary>
    /// The chance an unfouled miss was blocked. A three is rarely blocked and its rate is fixed; a two
    /// moves with the two fives' height shapes against each other, capped (see
    /// <see cref="MatchModelBounds.MaximumBlockHeightSwing"/>).
    /// </summary>
    private static int BlockRate(Side offence, Side defence, bool isThree)
    {
        if (isThree)
        {
            return MatchModelBounds.BlockChanceOnThreePointMiss;
        }

        return MatchModelBounds.BlockChanceOnTwoPointMiss +
            Swing(defence.OnFloorHeight - offence.OnFloorHeight, MatchModelBounds.BlockPerHeightPoint, MatchModelBounds.MaximumBlockHeightSwing);
    }

    /// <summary>The period's percentage for this shot, pushed by the score at this moment (see <see cref="MatchModelBounds.LeadPressurePerPoint"/>).</summary>
    private static int MakePercentage(int periodPercentage, Side offence, Side defence)
    {
        var pressure = Math.Clamp(
            (defence.Points - offence.Points) * MatchModelBounds.LeadPressurePerPoint,
            -MatchModelBounds.MaximumLeadPressure,
            MatchModelBounds.MaximumLeadPressure);

        return Math.Clamp(periodPercentage + pressure, MatchModelBounds.MinimumFieldGoalPercentage, MatchModelBounds.MaximumFieldGoalPercentage);
    }

    /// <summary>
    /// A zone's make percentage: the period's two- or three-point percentage, the zone's zero-mean
    /// offset, and the shooter's height shape — up at the rim, down from three, capped.
    /// </summary>
    private static int ZonePercentage(ShotZone zone, (int Fg2Percent, int Fg3Percent) shooting, int heightShape)
    {
        var height = Swing(heightShape, MatchModelBounds.ZonePercentagePerHeightPoint, MatchModelBounds.MaximumZonePercentageSwing);
        return zone switch
        {
            ShotZone.Rim => shooting.Fg2Percent + MatchModelBounds.RimPercentageOffset + height,
            ShotZone.Paint => shooting.Fg2Percent + MatchModelBounds.PaintPercentageOffset,
            ShotZone.MidRange => shooting.Fg2Percent + MatchModelBounds.MidRangePercentageOffset,
            ShotZone.Corner3 => shooting.Fg3Percent + MatchModelBounds.Corner3PercentageOffset - height,
            _ => shooting.Fg3Percent + MatchModelBounds.AboveBreak3PercentageOffset - height,
        };
    }

    private static void ChargeAttempt(Side side, int shooter, ShotZone zone)
    {
        side.FieldGoalsAttempted[shooter]++;
        side.ZoneAttempts[(int)zone][shooter]++;
        if (ShotZones.IsThree(zone))
        {
            side.ThreePointsAttempted[shooter]++;
        }
    }

    /// <summary>
    /// A live miss, resolved to exactly one rebound — one draw decides which end and whether a player
    /// or the team secured it. Returns whether the offence kept the ball.
    /// </summary>
    private static bool Rebound(Side offence, Side defence, IRandomSource random)
    {
        const int offensiveTeam = MatchModelBounds.OffensiveReboundRate * MatchModelBounds.TeamReboundShare / MatchModelBounds.ProbabilityScale;
        const int defensiveTeam = (MatchModelBounds.ProbabilityScale - MatchModelBounds.OffensiveReboundRate) * MatchModelBounds.TeamReboundShare / MatchModelBounds.ProbabilityScale;

        var roll = random.NextBasisPoint();
        offence.LastRebounder = -1;

        if (roll < offensiveTeam)
        {
            offence.TeamOffensiveRebounds++;
            return true;
        }

        if (roll < MatchModelBounds.OffensiveReboundRate)
        {
            var rebounder = offence.PickOnFloor(offence.OffensiveReboundFactors, random);
            offence.OffensiveReboundsBy[rebounder]++;
            offence.LastRebounder = rebounder;
            return true;
        }

        if (roll < MatchModelBounds.OffensiveReboundRate + defensiveTeam)
        {
            defence.TeamDefensiveRebounds++;
            return false;
        }

        defence.DefensiveReboundsBy[defence.PickOnFloor(defence.DefensiveReboundFactors, random)]++;
        return false;
    }

    /// <summary>
    /// Turns this possession's efficiency into a two-point and a three-point percentage: both move
    /// together, by the same amount, off the same strength/home/fatigue term that used to move a
    /// single blended scoring rate — a team having a good night gets better at both shots, not
    /// selectively better from one spot on the floor.
    /// </summary>
    private static (int Fg2Percent, int Fg3Percent) FieldGoalPercentagesFor(int efficiency)
    {
        var delta = efficiency - MatchModelBounds.BaseOffensiveEfficiency;
        var swing = delta * MatchModelBounds.FieldGoalPercentSwingAtMaximumStrength /
            MatchModelBounds.MaximumStrengthEfficiencySwing;

        var fg2Percent = Math.Clamp(
            MatchModelBounds.BaseTwoPointPercentage + swing,
            MatchModelBounds.MinimumFieldGoalPercentage,
            MatchModelBounds.MaximumFieldGoalPercentage);

        var fg3Percent = Math.Clamp(
            MatchModelBounds.BaseThreePointPercentage + swing,
            MatchModelBounds.MinimumFieldGoalPercentage,
            MatchModelBounds.MaximumFieldGoalPercentage);

        return (fg2Percent, fg3Percent);
    }

    /// <summary>
    /// The terminal basket after the last overtime: a layup at the rim by whichever player the usage
    /// factors pick, with no assist.
    /// </summary>
    private static void AwardBasket(Side side, Side opponent, IRandomSource random)
    {
        var shooter = side.PickOnFloor(side.UsageFactors, random);
        ChargeAttempt(side, shooter, ShotZone.Rim);
        AwardMake(side, opponent, shooter, ShotZone.Rim);
    }

    private static void AwardMake(Side side, Side opponent, int shooter, ShotZone zone)
    {
        var isThree = ShotZones.IsThree(zone);
        Score(side, opponent, shooter, isThree ? 3 : 2);
        side.FieldGoalsMade[shooter]++;
        side.ZoneMade[(int)zone][shooter]++;

        if (isThree)
        {
            side.ThreePointsMade[shooter]++;
        }
    }

    /// <summary>
    /// Points on the board: the scorer's, the team's, and plus-minus for all ten players on the floor
    /// — every point is +1 to each of the five who scored it and −1 to each of the five who allowed it,
    /// which is why a team's plus-minus always sums to five times its margin.
    /// </summary>
    private static void Score(Side side, Side opponent, int scorer, int points)
    {
        side.Points += points;
        side.PointsBy[scorer] += points;

        foreach (var index in side.OnFloor)
        {
            side.PlusMinusBy[index] += points;
        }

        foreach (var index in opponent.OnFloor)
        {
            opponent.PlusMinusBy[index] -= points;
        }
    }

    /// <summary>
    /// Draws every shot of one free-throw trip. Returns whether the trip ended on a miss — a live
    /// ball, counted so the box score can account for the rebound that follows it.
    /// </summary>
    private static bool ResolveFreeThrowTrip(Side side, Side opponent, int shooter, int shots, IRandomSource random)
    {
        var madeLastShot = false;

        for (var shot = 0; shot < shots; shot++)
        {
            side.FreeThrowsAttempted[shooter]++;
            madeLastShot = random.NextBasisPoint() < side.FreeThrowPercentages[shooter];

            if (madeLastShot)
            {
                side.FreeThrowsMade[shooter]++;
                Score(side, opponent, shooter, 1);
            }
        }

        if (!madeLastShot)
        {
            side.LiveFreeThrowMisses++;
        }

        return !madeLastShot;
    }

    /// <summary>
    /// Who got hurt. Risk scales with the minutes actually played, so it sits with the people
    /// carrying the load rather than falling evenly on a bench nobody used.
    /// </summary>
    private static List<MatchInjury> Injuries(Side side, IRandomSource random)
    {
        var injuries = new List<MatchInjury>();

        for (var index = 0; index < side.Players.Count; index++)
        {
            var chance = MatchModelBounds.InjuryChancePerFullGame * side.Minutes[index] /
                MinutesAllocationBounds.MaximumMinutesPerPlayer * side.InjuryFactors[index] / 100;

            if (random.NextBasisPoint() >= chance)
            {
                continue;
            }

            // Two draws and the shorter one, so most knocks are short and a long one is rare
            // without needing a second distribution to state it.
            var first = random.NextInt32(MatchModelBounds.MinimumInjuryDays, MatchModelBounds.MaximumInjuryDays + 1);
            var second = random.NextInt32(MatchModelBounds.MinimumInjuryDays, MatchModelBounds.MaximumInjuryDays + 1);

            var description = InjuryDescriptions[random.NextInt32(0, InjuryDescriptions.Length)];

            injuries.Add(new MatchInjury(
                side.Players[index].PlayerId,
                side.TeamId,
                description,
                Math.Min(first, second)));
        }

        return injuries;
    }

    /// <summary>
    /// The side profile for this team's rotation: reused while the same depth chart plays with the same
    /// fit players (a season builds a chart once and plays it until someone is hurt or returns), built
    /// afresh otherwise. Keyed weakly on the chart, so a chart nobody holds any more is let go.
    /// </summary>
    private SideProfile ProfileFor(MatchTeam team)
    {
        if (_profiles.TryGetValue(team.Rotation, out var cached) && ReferenceEquals(cached.Available, team.Available))
        {
            return cached;
        }

        var profile = new SideProfile(team);
        _profiles.AddOrUpdate(team.Rotation, profile);
        return profile;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DepthChart, SideProfile> _profiles = new();

    private DomainOperationResult<Side> BuildSide(MatchTeam team, int personalFoulLimit)
    {
        ArgumentNullException.ThrowIfNull(team);

        if (team.Rotation.IsEmpty)
        {
            return DomainOperationResult<Side>.Failure(new DomainError(
                EmptyRotationCode,
                $"Team '{team.TeamId.Value}' has nobody available, so it cannot be put on the floor for this game."));
        }

        return DomainOperationResult<Side>.Success(new Side(team, ProfileFor(team), personalFoulLimit));
    }

    /// <summary>
    /// Everything about a side that follows from its rotation and its players' sheets alone — every
    /// pick weight, shot diet, rate and the stint plan for a given number of possessions — worked out
    /// once per depth chart and reused for every game the chart plays (see <see cref="ProfileFor"/>). A
    /// pure function of its inputs, so reuse cannot change a result; it only stops a season rebuilding
    /// the same arrays two hundred times.
    /// </summary>
    private sealed class SideProfile
    {
        /// <summary>The sheet of a player the team cannot rate: nobody, at zero. One instance, not one per game.</summary>
        private static readonly PlayerRating NoOne = new(0);

        private readonly Dictionary<int, int[][]> _plans = [];

        public SideProfile(MatchTeam team)
        {
            Players = team.Rotation.Slots;
            Available = team.Available;

            var count = Players.Count;
            PlannedMinutes = Players.Select(slot => slot.Minutes).ToArray();
            Overalls = new int[count];
            PassingShape = new int[count];
            QuicknessShape = new int[count];
            HeightShape = new int[count];
            StealFactors = new int[count];
            BlockFactors = new int[count];
            FoulFactors = new int[count];
            var touches = new int[count];
            var shotClose = new int[count];
            var shotMid = new int[count];
            var shotThree = new int[count];

            var ratings = new PlayerRating[count];
            for (var index = 0; index < count; index++)
            {
                ratings[index] = team.RatingOf(Players[index].PlayerId) ?? NoOne;
            }

            // Shape, not level: each attribute measured against the player's own Overall, so these
            // terms redistribute what happens without adding to how good a side is. A player missing
            // from the available list rates zero everywhere.
            for (var index = 0; index < count; index++)
            {
                var rating = ratings[index];
                var overall = rating.Overall;
                Overalls[index] = overall;
                PassingShape[index] = rating.Playmaking - overall;
                QuicknessShape[index] = rating.Quickness - overall;
                HeightShape[index] = MatchModelBounds.HeightShapeFor(rating.Body);
                touches[index] = Math.Max(1, rating.Tendency(PlayerTendency.Touches));
                shotClose[index] = rating.Tendency(PlayerTendency.ShotClose);
                shotMid[index] = rating.Tendency(PlayerTendency.ShotMid);
                shotThree[index] = rating.Tendency(PlayerTendency.ShotThree);
                StealFactors[index] = ShapeWeight(
                    QuicknessShape[index], MatchModelBounds.StealWeightPerQuicknessPoint, MatchModelBounds.MinimumStealWeight, MatchModelBounds.MaximumStealWeight);

                // Who blocks is mostly where they play — a centre is at the rim, a point guard is not —
                // and then how tall they are for their level.
                BlockFactors[index] = MatchModelBounds.BlockWeightFor(Players[index].Position) * ShapeWeight(
                    HeightShape[index], MatchModelBounds.BlockWeightPerHeightPoint, MatchModelBounds.MinimumBlockWeight, MatchModelBounds.MaximumBlockWeight) / 100;

                // Who fouls is where they play, then slow feet for their level: a defender beaten off
                // the dribble reaches.
                FoulFactors[index] = MatchModelBounds.FoulWeightFor(Players[index].Position) * ShapeWeight(
                    -QuicknessShape[index], MatchModelBounds.FoulWeightPerQuicknessPoint, MatchModelBounds.MinimumFoulWeight, MatchModelBounds.MaximumFoulWeight) / 100;
            }


            // Each player's shot diet as cumulative weights over the five zones: the neutral shares,
            // each kind scaled by his shot-location tendency against the league's middle (close: rim and
            // paint; mid; three: corner and above the break), within the named diet caps — and then by
            // how the shot came: one he made for himself leans to the rim and the pull-up, one he caught
            // off a pass to the spot-up three (see MatchModelBounds.CatchThreeMultiplier).
            ZoneDiet = new int[2][][];
            for (var kind = 0; kind < 2; kind++)
            {
                var caught = kind == CaughtShot;
                ZoneDiet[kind] = new int[count][];
                for (var index = 0; index < count; index++)
                {
                    var close = MatchModelBounds.DietMultiplierFor(shotClose[index]) * (caught ? MatchModelBounds.CatchCloseMultiplier : MatchModelBounds.OwnCloseMultiplier) / 100;
                    var mid = MatchModelBounds.DietMultiplierFor(shotMid[index]) * (caught ? MatchModelBounds.CatchMidMultiplier : MatchModelBounds.OwnMidMultiplier) / 100;
                    var three = MatchModelBounds.DietMultiplierFor(shotThree[index]) * (caught ? MatchModelBounds.CatchThreeMultiplier : MatchModelBounds.OwnThreeMultiplier) / 100;
                    int[] shares =
                    [
                        MatchModelBounds.RimShare * close / 100,
                        MatchModelBounds.PaintShare * close / 100,
                        MatchModelBounds.MidRangeShare * mid / 100,
                        MatchModelBounds.Corner3Share * three / 100,
                        MatchModelBounds.AboveBreak3Share * three / 100,
                    ];

                    for (var zone = 1; zone < shares.Length; zone++)
                    {
                        shares[zone] += shares[zone - 1];
                    }

                    ZoneDiet[kind][index] = shares;
                }
            }


            var playedMinutes = PlannedMinutes.Sum();

            // Minutes-weighted, because the rotation already decided who matters. A mean over the
            // roster would rate a team by its twelfth man as much as by its best player.

            var meanOverall = Math.Max(1, Overalls.Length == 0 ? 1 : Overalls.Sum() / Overalls.Length);
            var meanTouches = Math.Max(1, touches.Length == 0 ? 1 : touches.Sum() / touches.Length);

            // Usage concentrates faster than talent does. A player twenty per cent better than his
            // team's average takes rather more than twenty per cent more of its shots, so the
            // relative rating is squared before it becomes a weight — and then clamped, which is
            // where the bound earns its keep: an outlier is a first option, not the whole offence.
            // Per player, not per minute: a pick is only ever among the five on the floor, so how
            // long someone plays already decides how often they can be picked.
            // Then by his touch tendency against his team-mates', cubed: how often the ball finds him is
            // a choice the offence makes, and it concentrates on the players who want it. A team of
            // neutral tendencies (a flat test fixture) uses rating alone, as before.
            UsageFactors = Enumerable.Range(0, count)
                .Select(index => (int)Math.Clamp(
                    (long)MatchModelBounds.RelativeWeight(Overalls[index], meanOverall, MatchModelBounds.UsageRatingDoublingPoints)
                        * MatchModelBounds.RelativeWeight(touches[index], meanTouches, MatchModelBounds.UsageTouchesDoublingPoints) / 100,
                    MatchModelBounds.MinimumUsageFactor,
                    MatchModelBounds.MaximumUsageFactor))
                .ToArray();

            AssistFactors = Players.Select(slot => MatchModelBounds.AssistWeightFor(slot.Position)).ToArray();

            // Minutes-weighted, because the rotation already decided who matters — and weighted again by
            // how much of the offence runs through each player (see MatchModelBounds.StarStrengthWeightPercent):
            // a star's level shapes his team's results more than a role player's, which is how one great
            // player turns a team into a contender. A mean over the roster would rate a team by its twelfth
            // man as much as by its best player.
            // Then convex: points above the rotation's own average count extra (see
            // MatchModelBounds.StarConvexityDivisor), so a great player lifts a team more than the same
            // points spread across several — and a league whose teams differ only by a uniform shift
            // plays exactly as before.
            long weightedLevel = 0;
            long weights = 0;
            var rotationMean = playedMinutes == 0 ? 0 : Enumerable.Range(0, count).Sum(index => Overalls[index] * PlannedMinutes[index]) / playedMinutes;
            for (var index = 0; index < count; index++)
            {
                var weight = (long)PlannedMinutes[index] * (100 + ((UsageFactors[index] - 100) * MatchModelBounds.StarStrengthWeightPercent / 100));
                var above = Math.Max(0, Overalls[index] - rotationMean);
                weightedLevel += (Overalls[index] + Math.Min(MatchModelBounds.MaximumStarBonus, above * above / MatchModelBounds.StarConvexityDivisor)) * weight;
                weights += weight;
            }

            Strength = playedMinutes == 0 || weights <= 0
                ? 0
                : Math.Clamp((int)(weightedLevel / weights), PlayerRating.MinimumOverall, PlayerRating.MaximumOverall);

            // The couplings that read the rest of the sheet (attributes A3–A6): who brings the ball up
            // and whether he looks to shoot, who rebounds, how well each shoots from each zone and from
            // the line, and who draws contact.
            int Mean(Func<PlayerRating, int> pick) => Math.Max(1, count == 0 ? 1 : ratings.Sum(pick) / count);
            var meanPlaymaking = Mean(rating => rating.Playmaking);
            var meanOffensiveRebounding = Mean(rating => rating[PlayerAttribute.OffensiveRebounding]);
            var meanDefensiveRebounding = Mean(rating => rating[PlayerAttribute.DefensiveRebounding] + rating[PlayerAttribute.BoxingOut]);
            var meanBlocking = Mean(rating => rating[PlayerAttribute.Blocking]);
            var meanDiscipline = Mean(rating => rating[PlayerAttribute.DefensiveDiscipline]);
            var attacks = ratings.Select(rating => Math.Max(rating.Tendency(PlayerTendency.Drive), rating.Tendency(PlayerTendency.PostUp))).ToArray();
            var meanAttack = count == 0 ? 0 : attacks.Sum() / count;

            HandlerFactors = new int[count];
            SelfShotShares = new int[count];
            PassAssistChances = new int[count];
            OffensiveReboundFactors = new int[count];
            DefensiveReboundFactors = new int[count];
            FreeThrowPercentages = new int[count];
            FoulDrawFactors = new int[count];
            ShootingSkill = ShotZones.All.Select(_ => new int[count]).ToArray();
            for (var index = 0; index < count; index++)
            {
                var rating = ratings[index];
                var level = rating.Overall;
                var position = Players[index].Position;

                HandlerFactors[index] = MatchModelBounds.AssistWeightFor(position) * Math.Clamp(
                    MatchModelBounds.RelativeWeight(rating.Playmaking, meanPlaymaking, MatchModelBounds.HandlerDoublingPoints) * MatchModelBounds.RelativeWeight(touches[index], meanTouches, MatchModelBounds.UsageTouchesDoublingPoints) / 100,
                    MatchModelBounds.MinimumHandlerFactor,
                    MatchModelBounds.MaximumHandlerFactor) / 100;

                SelfShotShares[index] = Math.Clamp(
                    MatchModelBounds.BaseSelfShotShare
                        + ((rating.Tendency(PlayerTendency.Shot) - PlayerRating.NeutralTendency) * MatchModelBounds.SelfShotPerShotTendencyPoint)
                        - ((rating.Tendency(PlayerTendency.Pass) - PlayerRating.NeutralTendency) * MatchModelBounds.SelfShotPerPassTendencyPoint),
                    MatchModelBounds.MinimumSelfShotShare,
                    MatchModelBounds.MaximumSelfShotShare);

                PassAssistChances[index] = MatchModelBounds.AssistChanceOnPass +
                    Swing(PassingShape[index], MatchModelBounds.AssistPerPassingPoint, MatchModelBounds.MaximumAssistPassingSwing);

                OffensiveReboundFactors[index] = MatchModelBounds.ReboundWeightFor(position) * Math.Clamp(
                    MatchModelBounds.RelativeWeight(rating[PlayerAttribute.OffensiveRebounding], meanOffensiveRebounding, MatchModelBounds.OffensiveReboundDoublingPoints),
                    MatchModelBounds.MinimumReboundSkillFactor,
                    MatchModelBounds.MaximumReboundSkillFactor) / 100;

                DefensiveReboundFactors[index] = MatchModelBounds.ReboundWeightFor(position) * Math.Clamp(
                    MatchModelBounds.RelativeWeight(rating[PlayerAttribute.DefensiveRebounding] + rating[PlayerAttribute.BoxingOut], meanDefensiveRebounding, MatchModelBounds.ReboundDoublingPoints),
                    MatchModelBounds.MinimumReboundSkillFactor,
                    MatchModelBounds.MaximumReboundSkillFactor) / 100;

                FreeThrowPercentages[index] = Math.Clamp(
                    MatchModelBounds.FreeThrowPercentage + ((rating[PlayerAttribute.FreeThrow] - level) * MatchModelBounds.FreeThrowPerPoint),
                    MatchModelBounds.MinimumFreeThrowPercentage,
                    MatchModelBounds.MaximumFreeThrowPercentage);

                // Attackers draw contact, and so do the best players: the defence fouls the man it
                // cannot stay in front of.
                FoulDrawFactors[index] = Math.Clamp(
                    (100 + ((attacks[index] - meanAttack) * MatchModelBounds.FoulDrawPerTendencyPoint))
                        * MatchModelBounds.RelativeWeight(level, meanOverall, MatchModelBounds.FoulDrawRatingDoublingPoints) / 100,
                    MatchModelBounds.MinimumFoulDrawFactor,
                    MatchModelBounds.MaximumFoulDrawFactor);

                BlockFactors[index] = BlockFactors[index] * Math.Clamp(
                    MatchModelBounds.RelativeWeight(rating[PlayerAttribute.Blocking], meanBlocking, MatchModelBounds.BlockDoublingPoints),
                    MatchModelBounds.MinimumBlockSkillFactor,
                    MatchModelBounds.MaximumBlockSkillFactor) / 100;

                // Undisciplined defenders foul more a minute, and the end of a bench is less disciplined
                // than the starters it backs up.
                FoulFactors[index] = FoulFactors[index] * Math.Clamp(
                    MatchModelBounds.RelativeWeight(meanDiscipline, Math.Max(1, rating[PlayerAttribute.DefensiveDiscipline]), MatchModelBounds.FoulDisciplineDoublingPoints),
                    MatchModelBounds.MinimumFoulDisciplineFactor,
                    MatchModelBounds.MaximumFoulDisciplineFactor) / 100;

                int Skill(PlayerAttribute attribute) => Swing(rating[attribute] - level, MatchModelBounds.ShootingSkillPerPoint, MatchModelBounds.MaximumShootingSkillSwing);
                ShootingSkill[(int)ShotZone.Rim][index] = Skill(PlayerAttribute.Finishing);
                ShootingSkill[(int)ShotZone.Paint][index] = Skill(PlayerAttribute.CloseRange);
                ShootingSkill[(int)ShotZone.MidRange][index] = Skill(PlayerAttribute.MidRange);
                ShootingSkill[(int)ShotZone.Corner3][index] = Skill(PlayerAttribute.ThreePoint);
                ShootingSkill[(int)ShotZone.AboveBreak3][index] = Skill(PlayerAttribute.ThreePoint);
            }


            // Each player's chance of drawing a shooting foul on a miss, and an and-one on a make, from
            // each zone: the base rates moved by the zone and by how much contact he draws.
            ShootingFoulChances = ShotZones.All.Select(zone => FoulDrawFactors
                .Select(factor => (int)((long)MatchModelBounds.ShootingFoulChanceOnMiss * MatchModelBounds.FoulDrawWeightFor(zone) * factor / 10_000))
                .ToArray()).ToArray();
            AndOneChances = ShotZones.All.Select(zone => FoulDrawFactors
                .Select(factor => (int)((long)MatchModelBounds.AndOneChanceOnMake * MatchModelBounds.FoulDrawWeightFor(zone) * factor / 10_000))
                .ToArray()).ToArray();
            InjuryFactors = ratings
                .Select(rating => Math.Clamp(
                    100 + ((rating.Hidden(HiddenAttribute.InjuryProneness) - PlayerRating.NeutralHidden) * MatchModelBounds.InjuryPronenessPerPoint),
                    MatchModelBounds.MinimumInjuryPronenessFactor,
                    MatchModelBounds.MaximumInjuryPronenessFactor))
                .ToArray();

            Rotation = Players
                .Select(slot => new RotationPlayer(slot.PlayerId.Value, slot.Minutes, slot.IsStarter, slot.DepthRank))
                .ToArray();
        }

        /// <summary>The fit players the profile was built from, so a cached profile is only reused for the same ones.</summary>
        public IReadOnlyList<AvailablePlayer> Available { get; }

        public IReadOnlyList<DepthChartSlot> Players { get; }

        public RotationPlayer[] Rotation { get; }

        /// <summary>The diet index of a shot caught off a pass; 0 is one the shooter made for himself.</summary>
        public const int CaughtShot = 1;

        public int[][][] ZoneDiet { get; }

        public int Strength { get; }

        public int[] PlannedMinutes { get; }

        public int[] Overalls { get; }

        public int[] PassingShape { get; }

        public int[] QuicknessShape { get; }

        public int[] HeightShape { get; }

        public int[] StealFactors { get; }

        public int[] BlockFactors { get; }

        public int[] FoulFactors { get; }

        public int[] UsageFactors { get; }

        public int[] AssistFactors { get; }

        public int[] HandlerFactors { get; }

        public int[] SelfShotShares { get; }

        public int[] PassAssistChances { get; }

        public int[] OffensiveReboundFactors { get; }

        public int[] DefensiveReboundFactors { get; }

        public int[] FreeThrowPercentages { get; }

        public int[] FoulDrawFactors { get; }

        public int[] InjuryFactors { get; }

        public int[][] ShootingSkill { get; }

        public int[][] ShootingFoulChances { get; }

        public int[][] AndOneChances { get; }

        /// <summary>
        /// Regulation's lineups for this many possessions (see <see cref="StintPlanner"/>), planned once
        /// and handed out as a fresh outer array each time — a foul-out re-plans by replacing entries, never
        /// by writing into a lineup — so one game's re-plan never leaks into the next.
        /// </summary>
        public int[][] PlanFor(int possessions)
        {
            if (!_plans.TryGetValue(possessions, out var plan))
            {
                plan = StintPlanner.Plan(Rotation, possessions);
                _plans[possessions] = plan;
            }

            return (int[][])plan.Clone();
        }
    }

    /// <summary>
    /// One team as the game is played against it: the rotation flattened into arrays, the factors
    /// each draw uses, and the running totals. Mutable and private, because a game is a loop over a
    /// scoreboard and threading an immutable record through two hundred possessions would allocate
    /// two hundred of them for no gain in clarity.
    /// </summary>
    private sealed class Side
    {
        public Side(MatchTeam team, SideProfile profile, int personalFoulLimit)
        {
            TeamId = team.TeamId;
            _foulLimit = personalFoulLimit;
            RestDays = team.RestDays;
            _profile = profile;
            Players = profile.Players;

            var count = Players.Count;
            PlannedMinutes = profile.PlannedMinutes;
            Overalls = profile.Overalls;
            PassingShape = profile.PassingShape;
            QuicknessShape = profile.QuicknessShape;
            HeightShape = profile.HeightShape;
            StealFactors = profile.StealFactors;
            BlockFactors = profile.BlockFactors;
            FoulFactors = profile.FoulFactors;
            UsageFactors = profile.UsageFactors;
            AssistFactors = profile.AssistFactors;
            HandlerFactors = profile.HandlerFactors;
            SelfShotShares = profile.SelfShotShares;
            PassAssistChances = profile.PassAssistChances;
            OffensiveReboundFactors = profile.OffensiveReboundFactors;
            DefensiveReboundFactors = profile.DefensiveReboundFactors;
            FreeThrowPercentages = profile.FreeThrowPercentages;
            FoulDrawFactors = profile.FoulDrawFactors;
            InjuryFactors = profile.InjuryFactors;
            ShootingSkill = profile.ShootingSkill;
            ShootingFoulChances = profile.ShootingFoulChances;
            AndOneChances = profile.AndOneChances;
            Strength = profile.Strength;
            _rotation = profile.Rotation;
            _zoneDiet = profile.ZoneDiet;
            _foulWeights = (int[])profile.FoulFactors.Clone();

            Minutes = new int[count];
            PlusMinusBy = new int[count];
            StealsBy = new int[count];
            TurnoversBy = new int[count];
            BlocksBy = new int[count];
            PersonalFoulsBy = new int[count];
            _regulationOn = new int[count];
            _overtimeOn = new int[count];
            OnFloor = [];
            ZoneAttempts = new int[ShotZones.All.Count][];
            ZoneMade = new int[ShotZones.All.Count][];
            for (var zone = 0; zone < ZoneAttempts.Length; zone++)
            {
                ZoneAttempts[zone] = new int[count];
                ZoneMade[zone] = new int[count];
            }

            PointsBy = new int[count];
            OffensiveReboundsBy = new int[count];
            DefensiveReboundsBy = new int[count];
            AssistsBy = new int[count];
            FieldGoalsAttempted = new int[count];
            FieldGoalsMade = new int[count];
            ThreePointsAttempted = new int[count];
            ThreePointsMade = new int[count];
            FreeThrowsAttempted = new int[count];
            FreeThrowsMade = new int[count];
        }

        private readonly SideProfile _profile;
        private readonly RotationPlayer[] _rotation;
        private readonly int _foulLimit;
        private readonly int[] _foulWeights;
        private const int CaughtShot = SideProfile.CaughtShot;

        private readonly int[][][] _zoneDiet;
        private int _possession;
        private readonly int[] _regulationOn;
        private readonly int[] _overtimeOn;
        private int[][] _plan = [];
        private int[]? _closers;

        /// <summary>The indices of the players on the floor for the possession being played.</summary>
        public int[] OnFloor { get; private set; }

        /// <summary>Plans regulation's lineups, possession by possession (see <see cref="StintPlanner"/>).</summary>
        public void PlanRegulation(int possessions) => _plan = _profile.PlanFor(possessions);

        /// <summary>
        /// Overtime belongs to the five the depth chart plays most, ties broken by player identifier:
        /// a closing five, not a continuation of the regulation rotation.
        /// </summary>
        public void SendInClosers() => _closers ??= Closers();

        /// <summary>The five the depth chart plays most among those still eligible (see <see cref="Eligible"/>).</summary>
        private int[] Closers() => InDepthOrder(Eligible()).Take(MinutesAllocationBounds.PlayersOnFloor).Order().ToArray();

        /// <summary>The depth chart's order: most planned minutes first, then by player identifier.</summary>
        private IEnumerable<int> InDepthOrder(IEnumerable<int> players) => players
            .OrderBy(player => FouledOut(player))
            .ThenByDescending(player => PlannedMinutes[player])
            .ThenBy(player => Players[player].PlayerId.Value, StringComparer.Ordinal);

        /// <summary>
        /// Who may still play: everyone not fouled out; short of five, the fouled-out players who have
        /// actually played most stay on, as the sport's rule allows.
        /// </summary>
        private int[] Eligible()
        {
            var stillIn = Enumerable.Range(0, Players.Count).Count(player => !FouledOut(player));
            return Enumerable.Range(0, Players.Count)
                .OrderBy(player => FouledOut(player))
                .ThenByDescending(player => _regulationOn[player] + _overtimeOn[player])
                .ThenBy(player => Players[player].PlayerId.Value, StringComparer.Ordinal)
                .Take(Math.Max(stillIn, MinutesAllocationBounds.PlayersOnFloor))
                .Order()
                .ToArray();
        }

        private bool FouledOut(int player) => PersonalFoulsBy[player] >= _foulLimit;

        /// <summary>
        /// A defensive foul by this side: a team foul, and a personal foul for a defender on the floor
        /// still under the limit (weighted by <see cref="FoulFactors"/>). Returns the team's fouls this
        /// period, for the bonus. If every defender on the floor is already at the limit — they stayed
        /// on only because nobody was left to replace them — the team foul stands with nobody charged.
        /// </summary>
        public int DefensiveFoul(IRandomSource random)
        {
            TeamFouls++;

            // A floor of defenders all at the limit charges nobody, and draws nothing.
            var total = 0;
            foreach (var index in OnFloor)
            {
                total += _foulWeights[index];
            }

            if (total > 0)
            {
                ChargeFoul(PickOnFloor(_foulWeights, random));
            }

            return TeamFouls;
        }

        /// <summary>Team fouls count afresh each period.</summary>
        public void ResetTeamFouls() => TeamFouls = 0;

        /// <summary>A defender's weight for the next foul: none at the limit, reduced in foul trouble.</summary>
        private int FoulWeight(int index) =>
            FouledOut(index) ? 0
            : PersonalFoulsBy[index] > 0 && PersonalFoulsBy[index] >= _foulLimit - MatchModelBounds.FoulTroubleMargin
                ? FoulFactors[index] * MatchModelBounds.FoulTroubleWeightPercent / 100
            : FoulFactors[index];

        /// <summary>A personal foul; the one that reaches the limit takes the player out of the game.</summary>
        public void ChargeFoul(int index)
        {
            if (FouledOut(index))
            {
                return;
            }

            PersonalFoulsBy[index]++;
            _foulWeights[index] = FoulWeight(index);
            if (FouledOut(index))
            {
                FoulOut(index);
            }
        }

        /// <summary>
        /// Takes a fouled-out player out of the game at once. In overtime the closers are re-picked; in
        /// regulation the rest is re-planned from what each player is still owed, with the fouled-out
        /// player's remaining share handed to those still eligible in proportion to their planned
        /// minutes. Either way he leaves the floor now, for the rest of this possession too.
        /// </summary>
        private void FoulOut(int index)
        {
            var eligible = Eligible();
            if (eligible.Contains(index))
            {
                return;
            }

            if (_closers is not null)
            {
                _closers = Closers();
                PutOnFloor(_closers);
                return;
            }

            var from = _possession + 1;
            var remaining = _plan.Length - from;
            StintPlanner.Replan(_rotation, _plan, from, eligible);

            // Somebody comes on for him now: the first of the next possession's five who is not on the
            // floor; at the very end of regulation, the eligible player the depth chart plays most.
            var substitute = remaining > 0
                ? _plan[from].FirstOrDefault(player => !OnFloor.Contains(player), -1)
                : InDepthOrder(eligible.Where(player => !OnFloor.Contains(player))).FirstOrDefault(-1);

            if (substitute >= 0)
            {
                PutOnFloor(OnFloor.Select(player => player == index ? substitute : player).Order().ToArray());
            }
        }

        /// <summary>Puts the right five on the floor for this possession and counts it toward their minutes.</summary>
        public void Enter(int possession)
        {
            _possession = possession;
            PutOnFloor(_closers ?? _plan[possession]);

            var counts = _closers is null ? _regulationOn : _overtimeOn;
            foreach (var index in OnFloor)
            {
                counts[index]++;
            }
        }

        /// <summary>Sets the five on the floor and the shape means the couplings read from them.</summary>
        private void PutOnFloor(int[] lineup)
        {
            OnFloor = lineup;
            var quickness = 0;
            var height = 0;
            foreach (var index in lineup)
            {
                quickness += QuicknessShape[index];
                height += HeightShape[index];
            }

            OnFloorQuickness = quickness / Math.Max(1, lineup.Length);
            OnFloorHeight = height / Math.Max(1, lineup.Length);
        }

        /// <summary>The mean quickness shape of the five on the floor for this possession.</summary>
        public int OnFloorQuickness { get; private set; }

        /// <summary>The mean height shape (<see cref="MatchModelBounds.HeightShapeFor"/>) of the five on the floor for this possession.</summary>
        public int OnFloorHeight { get; private set; }

        /// <summary>Turns the possessions each player was on the floor for into the minutes the box score credits.</summary>
        public void CreditMinutes(int regulationPossessions, int overtimePeriods)
        {
            var credited = StintPlanner.Minutes(_regulationOn, regulationPossessions, _overtimeOn, overtimePeriods);
            Array.Copy(credited, Minutes, credited.Length);
        }

        public TeamId TeamId { get; }

        public int RestDays { get; }

        public IReadOnlyList<DepthChartSlot> Players { get; }

        /// <summary>The depth chart's planned minutes: what the stint planner aims at, and what team strength is weighted by.</summary>
        public int[] PlannedMinutes { get; }

        /// <summary>The minutes actually credited, from the possessions each player was on the floor for.</summary>
        public int[] Minutes { get; }

        public int[] PlusMinusBy { get; }

        public int[] StealsBy { get; }

        public int[] TurnoversBy { get; }

        public int[] BlocksBy { get; }

        public int[] HeightShape { get; }

        public int[] BlockFactors { get; }

        public int[] PersonalFoulsBy { get; }

        /// <summary>Attempts and makes from each zone, indexed by <see cref="ShotZone"/> then player.</summary>
        public int[][] ZoneAttempts { get; }

        /// <inheritdoc cref="ZoneAttempts"/>
        public int[][] ZoneMade { get; }

        /// <summary>Where this shooter shoots from, given a draw in basis points: his own diet.</summary>
        public ShotZone ZoneFor(int shooter, bool caught, int roll)
        {
            // The roll is a basis point; a scaled diet need not total exactly that scale, so the roll is
            // stretched to the diet's own total (a neutral diet totals the scale and is read as is).
            var diet = _zoneDiet[caught ? CaughtShot : 0][shooter];
            roll = (int)((long)roll * diet[^1] / MatchModelBounds.ProbabilityScale);
            var zone = 0;
            while (zone < diet.Length - 1 && roll >= diet[zone])
            {
                zone++;
            }

            return (ShotZone)zone;
        }

        public int[] FoulFactors { get; }

        /// <summary>Defensive fouls this period, toward the bonus.</summary>
        public int TeamFouls { get; private set; }

        public int[] PassingShape { get; }

        public int[] QuicknessShape { get; }

        public int[] StealFactors { get; }

        public int[] Overalls { get; }

        public int[] PointsBy { get; }

        public int[] OffensiveReboundsBy { get; }

        public int[] DefensiveReboundsBy { get; }

        public int[] AssistsBy { get; }

        public int[] FieldGoalsAttempted { get; }

        public int[] FieldGoalsMade { get; }

        public int[] ThreePointsAttempted { get; }

        public int[] ThreePointsMade { get; }

        public int[] FreeThrowsAttempted { get; }

        public int[] FreeThrowsMade { get; }

        public int[] UsageFactors { get; }

        /// <summary>Who brings the ball up: playmaking against the rotation's, by position and touches.</summary>
        public int[] HandlerFactors { get; }

        /// <summary>The chance, in basis points, each player shoots it himself when he has brought the ball up.</summary>
        public int[] SelfShotShares { get; }

        /// <summary>The chance, in basis points, a make off each player's pass is his assist.</summary>
        public int[] PassAssistChances { get; }

        public int[] OffensiveReboundFactors { get; }

        public int[] DefensiveReboundFactors { get; }

        public int[] FreeThrowPercentages { get; }

        /// <summary>Each player's injury chance as a percentage of the base, from his proneness.</summary>
        public int[] InjuryFactors { get; }

        /// <summary>How often each player draws a shooting foul, as a percentage of the base rate.</summary>
        public int[] FoulDrawFactors { get; }

        /// <summary>Each player's chance, in basis points, of a shooting foul on a miss from each zone, indexed by <see cref="ShotZone"/> then player.</summary>
        public int[][] ShootingFoulChances { get; }

        /// <summary>Each player's chance, in basis points, of an and-one on a make from each zone, indexed like <see cref="ShootingFoulChances"/>.</summary>
        public int[][] AndOneChances { get; }

        /// <summary>Each player's make-percentage swing from each zone, indexed by <see cref="ShotZone"/> then player.</summary>
        public int[][] ShootingSkill { get; }

        /// <summary>The player who grabbed the last offensive rebound, or −1 if the team did: a put-back candidate.</summary>
        public int LastRebounder { get; set; } = -1;

        public int[] AssistFactors { get; }

        public int Strength { get; }

        public int Points { get; set; }

        public int TeamOffensiveRebounds { get; set; }

        public int TeamDefensiveRebounds { get; set; }

        public int LiveFreeThrowMisses { get; set; }

        public TeamStatLine ToTeamLine() => new(TeamId, TeamOffensiveRebounds, TeamDefensiveRebounds, LiveFreeThrowMisses);

        /// <summary>
        /// One of the five on the floor, drawn in proportion to the supplied per-player factors.
        /// Walks the lineup in a fixed order and takes exactly one draw, so the same seed always picks
        /// the same player.
        /// </summary>
        public int PickOnFloor(int[] factors, IRandomSource random) => PickTeammateOnFloor(factors, excluded: -1, random);

        /// <summary>
        /// One of the five on the floor other than <paramref name="excluded"/> — how an assist is
        /// credited, so nobody assists their own basket. Falls back to the excluded player only when
        /// nobody else is on the floor, where <c>BoxScore.Create</c> would refuse the assist; the
        /// engine never fields a team that small.
        /// </summary>
        public int PickTeammateOnFloor(int[] factors, int excluded, IRandomSource random)
        {
            var total = 0;
            foreach (var index in OnFloor)
            {
                total += index == excluded ? 0 : factors[index];
            }

            if (total <= 0)
            {
                return excluded >= 0 ? excluded : OnFloor[0];
            }

            var roll = random.NextInt32(0, total);

            foreach (var index in OnFloor)
            {
                if (index == excluded)
                {
                    continue;
                }

                roll -= factors[index];
                if (roll < 0)
                {
                    return index;
                }
            }

            return OnFloor[^1];
        }

        public IEnumerable<PlayerStatLine> ToStatLines()
        {
            return Enumerable.Range(0, Players.Count).Select(index => new PlayerStatLine(
                Players[index].PlayerId,
                TeamId,
                Minutes[index],
                PointsBy[index],
                OffensiveReboundsBy[index],
                DefensiveReboundsBy[index],
                AssistsBy[index],
                FieldGoalsAttempted[index],
                FieldGoalsMade[index],
                ThreePointsAttempted[index],
                ThreePointsMade[index],
                FreeThrowsAttempted[index],
                FreeThrowsMade[index],
                Players[index].IsStarter,
                PlusMinusBy[index],
                StealsBy[index],
                TurnoversBy[index],
                BlocksBy[index],
                PersonalFoulsBy[index],
                FouledOut(index),
                new ShotZones(
                    ZoneAttempts[0][index], ZoneMade[0][index],
                    ZoneAttempts[1][index], ZoneMade[1][index],
                    ZoneAttempts[2][index], ZoneMade[2][index],
                    ZoneAttempts[3][index], ZoneMade[3][index],
                    ZoneAttempts[4][index], ZoneMade[4][index])));
        }
    }
}
