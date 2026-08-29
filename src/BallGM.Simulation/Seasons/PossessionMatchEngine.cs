using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

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

        var homeResult = BuildSide(setup.Home);
        if (homeResult.IsFailure)
        {
            return DomainOperationResult<MatchOutcome>.Failure(homeResult.Errors.ToArray());
        }

        var awayResult = BuildSide(setup.Away);
        if (awayResult.IsFailure)
        {
            return DomainOperationResult<MatchOutcome>.Failure(awayResult.Errors.ToArray());
        }

        var home = homeResult.Value;
        var away = awayResult.Value;
        var random = new SeededRandomSource(setup.Seed);

        var homeEfficiency = EfficiencyFor(home, away, isHome: true);
        var awayEfficiency = EfficiencyFor(away, home, isHome: false);

        var possessions = MatchModelBounds.BasePossessionsPerGame +
            random.NextInt32(-MatchModelBounds.PossessionSpread, MatchModelBounds.PossessionSpread + 1);

        PlayPeriod(home, homeEfficiency, possessions, random);
        PlayPeriod(away, awayEfficiency, possessions, random);

        // A drawn game is refused by GameResult, every standings tie-break assumes a winner, and a
        // postseason series that could not be won would never end. So the tie is played out rather
        // than nudged: overtime is more possessions on the same terms.
        var overtimePeriods = 0;

        while (home.Points == away.Points && overtimePeriods < MatchModelBounds.MaximumOvertimePeriods)
        {
            overtimePeriods++;
            PlayPeriod(home, homeEfficiency, MatchModelBounds.OvertimePossessions, random);
            PlayPeriod(away, awayEfficiency, MatchModelBounds.OvertimePossessions, random);
        }

        if (home.Points == away.Points)
        {
            // Six overtimes past anything a real game has needed. The loop has to terminate, so the
            // terminal rule is stated rather than left to a seventh draw: the more efficient side
            // converts one last possession, and the home team holds an exact tie.
            var winner = awayEfficiency > homeEfficiency ? away : home;
            AwardBasket(winner, points: 2, random);
        }

        AwardOvertimeMinutes(home, overtimePeriods);
        AwardOvertimeMinutes(away, overtimePeriods);

        FinishSide(home, away, random);
        FinishSide(away, home, random);

        var lines = home.ToStatLines().Concat(away.ToStatLines()).ToList();

        var boxScoreResult = BoxScore.Create(
            setup.Fixture.Id,
            setup.Fixture.HomeTeamId,
            setup.Fixture.AwayTeamId,
            home.Points,
            away.Points,
            lines);

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
    /// Plays a run of possessions for one side. Each possession either produces points or does not,
    /// and a possession that does not is a miss somebody will rebound.
    /// </summary>
    private static void PlayPeriod(Side side, int efficiency, int possessions, IRandomSource random)
    {
        // Points per possession is the scoring rate times the points a score is worth, and a score is
        // worth two or three. Inverting that gives the rate the efficiency implies, which is then
        // clamped: the bound is on how often a team scores, not on how much it is allowed to want to.
        var scoringRate = Math.Clamp(
            efficiency * MatchModelBounds.ProbabilityScale /
                ((2 * MatchModelBounds.ProbabilityScale) + MatchModelBounds.ThreePointShareOfScores),
            MatchModelBounds.MinimumScoringRate,
            MatchModelBounds.MaximumScoringRate);

        for (var possession = 0; possession < possessions; possession++)
        {
            if (random.NextInt32(0, MatchModelBounds.ProbabilityScale) >= scoringRate)
            {
                side.Misses++;
                continue;
            }

            var isThree = random.NextInt32(0, MatchModelBounds.ProbabilityScale) < MatchModelBounds.ThreePointShareOfScores;
            AwardBasket(side, isThree ? 3 : 2, random);
        }
    }

    private static void AwardBasket(Side side, int points, IRandomSource random)
    {
        var scorer = side.PickBy(side.UsageWeights, random);

        side.Points += points;
        side.MadeFieldGoals++;
        side.PointsBy[scorer] += points;
    }

    /// <summary>
    /// Rebounds and assists, counted off the possessions that actually happened rather than drawn
    /// independently. A box score whose rebounds bore no relation to its misses would be two accounts
    /// of one game.
    /// </summary>
    private static void FinishSide(Side side, Side opponent, IRandomSource random)
    {
        var rebounds =
            (opponent.Misses * MatchModelBounds.DefensiveReboundShare / MatchModelBounds.ProbabilityScale) +
            (side.Misses * MatchModelBounds.OffensiveReboundShare / MatchModelBounds.ProbabilityScale);

        for (var index = 0; index < rebounds; index++)
        {
            side.ReboundsBy[side.PickBy(side.ReboundWeights, random)]++;
        }

        var assists = side.MadeFieldGoals * MatchModelBounds.AssistShareOfMadeFieldGoals / MatchModelBounds.ProbabilityScale;

        for (var index = 0; index < assists; index++)
        {
            side.AssistsBy[side.PickBy(side.AssistWeights, random)]++;
        }
    }

    /// <summary>
    /// Overtime minutes go to the five who were already playing most. Crude, and deliberately so:
    /// nothing downstream reads a minutes total for anything but display, and inventing a second
    /// rotation rule for the extra five minutes would be a rule nobody stated.
    /// </summary>
    private static void AwardOvertimeMinutes(Side side, int periods)
    {
        if (periods == 0)
        {
            return;
        }

        var closers = Enumerable.Range(0, side.Players.Count)
            .OrderByDescending(index => side.Minutes[index])
            .ThenBy(index => side.Players[index].PlayerId.Value, StringComparer.Ordinal)
            .Take(MinutesOnFloor)
            .ToArray();

        foreach (var index in closers)
        {
            side.Minutes[index] += MatchModelBounds.OvertimeMinutes * periods;
        }
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
                Rules.Seasons.MinutesAllocationBounds.MaximumMinutesPerPlayer;

            if (random.NextInt32(0, MatchModelBounds.ProbabilityScale) >= chance)
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

    private static DomainOperationResult<Side> BuildSide(MatchTeam team)
    {
        ArgumentNullException.ThrowIfNull(team);

        if (team.Rotation.IsEmpty)
        {
            return DomainOperationResult<Side>.Failure(new DomainError(
                EmptyRotationCode,
                $"Team '{team.TeamId.Value}' has nobody available, so it cannot be put on the floor for this game."));
        }

        return DomainOperationResult<Side>.Success(new Side(team));
    }

    private const int MinutesOnFloor = 5;

    /// <summary>
    /// One team as the game is played against it: the rotation flattened into arrays, the weights
    /// each draw uses, and the running totals. Mutable and private, because a game is a loop over a
    /// scoreboard and threading an immutable record through two hundred possessions would allocate
    /// two hundred of them for no gain in clarity.
    /// </summary>
    private sealed class Side
    {
        public Side(MatchTeam team)
        {
            TeamId = team.TeamId;
            RestDays = team.RestDays;
            Players = team.Rotation.Slots;

            var count = Players.Count;
            Minutes = Players.Select(slot => slot.Minutes).ToArray();
            Overalls = Players.Select(slot => team.OverallOf(slot.PlayerId) ?? 0).ToArray();
            PointsBy = new int[count];
            ReboundsBy = new int[count];
            AssistsBy = new int[count];

            var playedMinutes = Minutes.Sum();

            // Minutes-weighted, because the rotation already decided who matters. A mean over the
            // roster would rate a team by its twelfth man as much as by its best player.
            Strength = playedMinutes == 0
                ? 0
                : Math.Clamp(
                    Enumerable.Range(0, count).Sum(index => Overalls[index] * Minutes[index]) / playedMinutes,
                    PlayerRating.MinimumOverall,
                    PlayerRating.MaximumOverall);

            var meanOverall = Math.Max(1, Overalls.Length == 0 ? 1 : Overalls.Sum() / Overalls.Length);

            // Usage concentrates faster than talent does. A player twenty per cent better than his
            // team's average takes rather more than twenty per cent more of its shots, so the
            // relative rating is squared before it becomes a weight — and then clamped, which is
            // where the bound earns its keep: an outlier is a first option, not the whole offence.
            UsageWeights = Enumerable.Range(0, count)
                .Select(index => Minutes[index] * Math.Clamp(
                    Overalls[index] * Overalls[index] * 100 / (meanOverall * meanOverall),
                    MatchModelBounds.MinimumUsageFactor,
                    MatchModelBounds.MaximumUsageFactor))
                .ToArray();

            ReboundWeights = Enumerable.Range(0, count)
                .Select(index => Minutes[index] * ReboundWeightFor(Players[index].Position))
                .ToArray();

            AssistWeights = Enumerable.Range(0, count)
                .Select(index => Minutes[index] * AssistWeightFor(Players[index].Position))
                .ToArray();
        }

        public TeamId TeamId { get; }

        public int RestDays { get; }

        public IReadOnlyList<DepthChartSlot> Players { get; }

        public int[] Minutes { get; }

        public int[] Overalls { get; }

        public int[] PointsBy { get; }

        public int[] ReboundsBy { get; }

        public int[] AssistsBy { get; }

        public int[] UsageWeights { get; }

        public int[] ReboundWeights { get; }

        public int[] AssistWeights { get; }

        public int Strength { get; }

        public int Points { get; set; }

        public int Misses { get; set; }

        public int MadeFieldGoals { get; set; }

        /// <summary>
        /// One player, drawn in proportion to the supplied weights. Walks the weights in a fixed
        /// order and takes exactly one draw, so the same seed always picks the same player.
        /// </summary>
        public int PickBy(IReadOnlyList<int> weights, IRandomSource random)
        {
            var total = 0;
            foreach (var weight in weights)
            {
                total += weight;
            }

            if (total <= 0)
            {
                return 0;
            }

            var roll = random.NextInt32(0, total);

            for (var index = 0; index < weights.Count; index++)
            {
                roll -= weights[index];
                if (roll < 0)
                {
                    return index;
                }
            }

            return weights.Count - 1;
        }

        public IEnumerable<PlayerStatLine> ToStatLines() =>
            Enumerable.Range(0, Players.Count).Select(index => new PlayerStatLine(
                Players[index].PlayerId,
                TeamId,
                Minutes[index],
                PointsBy[index],
                ReboundsBy[index],
                AssistsBy[index],
                Players[index].IsStarter));

        /// <summary>
        /// Who gets the ball off the rim. Positional rather than rating-driven: a great point guard
        /// does not out-rebound a mediocre centre, and a box score that said otherwise would read as
        /// obviously wrong to anybody who follows the sport.
        /// </summary>
        private static int ReboundWeightFor(Position position) => position switch
        {
            Position.Center => 130,
            Position.PowerForward => 120,
            Position.SmallForward => 95,
            Position.ShootingGuard => 80,
            _ => 75,
        };

        private static int AssistWeightFor(Position position) => position switch
        {
            Position.PointGuard => 150,
            Position.ShootingGuard => 110,
            Position.SmallForward => 95,
            Position.PowerForward => 80,
            _ => 65,
        };
    }
}
