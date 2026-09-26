using System.Globalization;
using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;
using BallGM.Rules.Seasons;
using BallGM.Simulation.Seasons;

var outDir = Path.Combine(AppContext.BaseDirectory, "out");
Directory.CreateDirectory(outDir);
var mode = args.Length > 0 ? args[0] : "all";

if (mode is "all" or "prospects") RunProspects();
if (mode is "all" or "seasons") RunSeasons(seasonCount: 1000);
if (mode is "all" or "calibration") RunCalibration();
if (mode is "all" or "dynasty") RunDynasty(seasons: 50);
if (mode is "debugstrength") DebugStrength();

Console.WriteLine("done: " + outDir);

static LeagueSession NewSession()
{
    var session = new LeagueSession(
        new FixtureLeagueDataSource(),
        new RulesCapLedger(),
        new RulesDraftAssetLedger(),
        new RulesTradeEngine(),
        new RulesSigningEngine(),
        new RulesFreeAgencyMarket(),
        new RulesSeasonEngine(),
        new SaveGameSerializer());

    var result = session.Load();
    if (result.IsFailure)
    {
        throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    return session;
}

void RunProspects()
{
    // ProspectGenerator is the shipped talent-generation mechanism (BallGM.Rules.Draft), tested
    // directly against its own rule bounds so the distribution shape is independent of whether the
    // shipping ruleset happens to turn draft-class generation on.
    var rules = DraftClassRules.Create(classSize: 1, minimumRating: 40, maximumRating: 99, prospectAgeYears: 19).Value;

    using var writer = new StreamWriter(Path.Combine(outDir, "prospects.csv"));
    writer.WriteLine("seed,overall");

    for (var seed = 1; seed <= 50_000; seed++)
    {
        var random = new SeededRandomSource(seed);
        var result = ProspectGenerator.Generate(new DraftClassId($"DC-{seed}"), new Season(2031), rules, random);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        var overall = result.Value.Prospects[0].TrueRating.Overall;
        writer.WriteLine(FormattableString.Invariant($"{seed},{overall}"));
    }
}

void RunSeasons(int seasonCount)
{
    using var teamSeasonWriter = new StreamWriter(Path.Combine(outDir, "team_seasons.csv"));
    teamSeasonWriter.WriteLine("season,teamId,wins,losses,gamesPlayed,pointsFor,pointsAgainst");

    using var gameWriter = new StreamWriter(Path.Combine(outDir, "games.csv"));
    gameWriter.WriteLine("season,gameId,day,homeTeamId,awayTeamId,homePoints,awayPoints");

    using var playerLineWriter = new StreamWriter(Path.Combine(outDir, "player_lines.csv"));
    playerLineWriter.WriteLine("season,gameId,teamId,playerId,minutes,points,rebounds,assists,started");

    using var rosterWriter = new StreamWriter(Path.Combine(outDir, "rosters.csv"));
    rosterWriter.WriteLine("season,teamId,playerId,overall");

    using var championWriter = new StreamWriter(Path.Combine(outDir, "champions.csv"));
    championWriter.WriteLine("season,bestRegularSeasonTeamId,championTeamId,bestTeamWonTitle");

    for (var seasonIndex = 1; seasonIndex <= seasonCount; seasonIndex++)
    {
        var session = NewSession();

        var overview = session.Overview().Value;
        foreach (var team in overview.Teams)
        {
            foreach (var spot in team.Roster)
            {
                rosterWriter.WriteLine(FormattableString.Invariant(
                    $"{seasonIndex},{team.TeamId},{spot.PlayerId},{spot.Overall}"));
            }
        }

        var started = session.StartSeason(seed: seasonIndex);
        if (started.IsFailure)
        {
            throw new InvalidOperationException(string.Join("; ", started.Errors.Select(e => e.Message)));
        }

        var advanced = session.AdvanceToEndOfSeason();
        if (advanced.IsFailure)
        {
            throw new InvalidOperationException(string.Join("; ", advanced.Errors.Select(e => e.Message)));
        }

        var standings = session.Standings().Value;
        foreach (var row in standings.Rows)
        {
            teamSeasonWriter.WriteLine(FormattableString.Invariant(
                $"{seasonIndex},{row.TeamId},{row.Wins},{row.Losses},{row.GamesPlayed},{row.PointsFor},{row.PointsAgainst}"));
        }

        var calendar = session.Season().Value.Calendar;

        for (var day = 0; day < calendar.LengthInDays; day++)
        {
            var boxScores = session.BoxScoresOn(day).Value;
            foreach (var game in boxScores)
            {
                if (!game.HasBoxScore) continue;

                gameWriter.WriteLine(FormattableString.Invariant(
                    $"{seasonIndex},{game.GameId},{day},{game.HomeTeamId},{game.AwayTeamId},{game.HomePoints},{game.AwayPoints}"));

                foreach (var line in game.HomeLines)
                {
                    playerLineWriter.WriteLine(FormattableString.Invariant(
                        $"{seasonIndex},{game.GameId},{game.HomeTeamId},{line.PlayerId},{line.Minutes},{line.Points},{line.Rebounds},{line.Assists},{(line.Started ? 1 : 0)}"));
                }

                foreach (var line in game.AwayLines)
                {
                    playerLineWriter.WriteLine(FormattableString.Invariant(
                        $"{seasonIndex},{game.GameId},{game.AwayTeamId},{line.PlayerId},{line.Minutes},{line.Points},{line.Rebounds},{line.Assists},{(line.Started ? 1 : 0)}"));
                }
            }
        }

        var bestRegularSeasonTeam = standings.Rows.Count > 0 ? standings.Rows[0].TeamId : "";

        var conclusion = session.ConcludeSeason();
        if (conclusion.IsSuccess)
        {
            var championTeamId = conclusion.Value.ChampionTeamId ?? "";
            championWriter.WriteLine(FormattableString.Invariant(
                $"{seasonIndex},{bestRegularSeasonTeam},{championTeamId},{(bestRegularSeasonTeam == championTeamId ? 1 : 0)}"));
        }

        if (seasonIndex % 50 == 0)
        {
            Console.WriteLine($"seasons: {seasonIndex}/{seasonCount}");
        }
    }
}

void RunCalibration()
{
    // Controlled two-team matchups played directly against PossessionMatchEngine, bypassing the
    // season/roster machinery so the only thing varying is the strength gap, home advantage, and the
    // seed. This is how win-probability-vs-rating-gap, home-court points, and margin SD are measured
    // without a stats field the engine does not expose (there is no possession count on the output,
    // so pace/ortg are not measured here — see the report's Untestable section).
    using var writer = new StreamWriter(Path.Combine(outDir, "calibration.csv"));
    writer.WriteLine("delta,gamesPerSide,seed,homeRating,awayRating,homeSwapped,homePoints,awayPoints,homeWon,possessions");

    var engine = new PossessionMatchEngine();
    const int gamesPerCell = 4000;

    for (var delta = -40; delta <= 40; delta += 4)
    {
        var baseRating = 72;
        var high = Math.Clamp(baseRating + (delta / 2), 0, 100);
        var low = Math.Clamp(baseRating - (delta / 2), 0, 100);

        for (var i = 0; i < gamesPerCell; i++)
        {
            // Half the draws play the stronger side at home, half on the road, so the strength
            // calibration curve is not confounded with home-court advantage.
            var strongAtHome = i % 2 == 0;
            var homeRating = strongAtHome ? high : low;
            var awayRating = strongAtHome ? low : high;
            var seed = HashSeed(delta, i);

            var setup = Setup(seed, homeRating, awayRating);
            var played = engine.Play(setup);
            if (played.IsFailure)
            {
                throw new InvalidOperationException(string.Join("; ", played.Errors.Select(e => e.Message)));
            }

            var result = played.Value.Result;
            // Recovers the possession count the engine drew, by replaying the identical first draw
            // of a freshly seeded SeededRandomSource — PossessionMatchEngine's own first random call
            // is exactly `BasePossessionsPerGame + NextInt32(-spread, spread+1)`, and the engine does
            // not expose possessions on its output. Not a code change: a second, independent RNG
            // instance constructed with the same seed, which the determinism guarantee says must
            // reproduce the same draw.
            var possessionsRandom = new SeededRandomSource(seed);
            var possessions = MatchModelBounds.BasePossessionsPerGame +
                possessionsRandom.NextInt32(-MatchModelBounds.PossessionSpread, MatchModelBounds.PossessionSpread + 1);

            writer.WriteLine(FormattableString.Invariant(
                $"{delta},{gamesPerCell},{seed},{homeRating},{awayRating},{(strongAtHome ? 1 : 0)},{result.HomePoints},{result.AwayPoints},{(result.HomePoints > result.AwayPoints ? 1 : 0)},{possessions}"));
        }
    }

    // Home-court isolation: identical ratings both sides, only home/away differs.
    using var hcaWriter = new StreamWriter(Path.Combine(outDir, "hca.csv"));
    hcaWriter.WriteLine("seed,homePoints,awayPoints,margin,possessions");

    for (var i = 0; i < 20_000; i++)
    {
        var seed = HashSeed(999_000, i);
        var setup = Setup(seed, 72, 72);
        var played = engine.Play(setup);
        if (played.IsFailure) throw new InvalidOperationException();

        var result = played.Value.Result;
        var possessionsRandom = new SeededRandomSource(seed);
        var possessions = MatchModelBounds.BasePossessionsPerGame +
            possessionsRandom.NextInt32(-MatchModelBounds.PossessionSpread, MatchModelBounds.PossessionSpread + 1);

        hcaWriter.WriteLine(FormattableString.Invariant($"{i},{result.HomePoints},{result.AwayPoints},{result.HomePoints - result.AwayPoints},{possessions}"));
    }
}

void RunDynasty(int seasons)
{
    // Chains real seasons through one LeagueSession (StartSeason -> AdvanceToEndOfSeason ->
    // ConcludeSeason -> StartSeason again) to see what actually happens to the player pool and the
    // standings across time in the wired game, as opposed to what the Rules-layer development/
    // retirement/draft-class code could do if something called it.
    using var writer = new StreamWriter(Path.Combine(outDir, "dynasty.csv"));
    writer.WriteLine("season,teamId,wins,losses,pointsFor,pointsAgainst,rosterCount,meanOverall,champion");

    var session = NewSession();

    for (var seasonIndex = 1; seasonIndex <= seasons; seasonIndex++)
    {
        var started = session.StartSeason(seed: 4_000_000 + seasonIndex);
        if (started.IsFailure)
        {
            writer.WriteLine(FormattableString.Invariant($"# season {seasonIndex} failed to start: {string.Join("; ", started.Errors.Select(e => e.Message))}"));
            break;
        }

        var advanced = session.AdvanceToEndOfSeason();
        if (advanced.IsFailure)
        {
            writer.WriteLine(FormattableString.Invariant($"# season {seasonIndex} failed to advance: {string.Join("; ", advanced.Errors.Select(e => e.Message))}"));
            break;
        }

        var standings = session.Standings().Value;
        var overview = session.Overview().Value;
        var champion = standings.Rows.Count > 0 ? standings.Rows[0].TeamId : "";

        foreach (var row in standings.Rows)
        {
            var team = overview.Teams.First(t => t.TeamId == row.TeamId);
            var meanOverall = team.Roster.Count > 0 ? team.Roster.Average(r => r.Overall) : 0.0;

            writer.WriteLine(FormattableString.Invariant(
                $"{seasonIndex},{row.TeamId},{row.Wins},{row.Losses},{row.PointsFor},{row.PointsAgainst},{team.RosterCount},{meanOverall:F2},{(row.TeamId == champion ? 1 : 0)}"));
        }

        var concluded = session.ConcludeSeason();
        if (concluded.IsFailure)
        {
            writer.WriteLine(FormattableString.Invariant($"# season {seasonIndex} failed to conclude: {string.Join("; ", concluded.Errors.Select(e => e.Message))}"));
            break;
        }
    }
}

void DebugStrength()
{
    // Reproduces PossessionMatchEngine.Side's strength formula (minutes-weighted mean Overall of the
    // built rotation) using only public APIs, to check what strength gap a given topRating gap
    // actually produces once DepthChartBuilder has allocated minutes.
    foreach (var topRating in new[] { 52, 72, 92 })
    {
        var teamId = new TeamId("TEAM-DEBUG");
        var positions = Enum.GetValues<Position>();
        var players = Enumerable.Range(0, 10)
            .Select(index => new AvailablePlayer(
                new PlayerId($"P{index:D2}"),
                positions[index % positions.Length],
                Math.Clamp(topRating - (index * 3), PlayerRating.MinimumOverall, PlayerRating.MaximumOverall)))
            .ToList();

        var build = new DepthChartBuilder().Build(teamId, players, new RosterSizeLimits(5, 15), players.Count);
        var slots = build.Value.Chart.Slots;

        var overallByPlayer = players.ToDictionary(p => p.PlayerId, p => p.Overall);
        long weightedSum = 0;
        long minuteSum = 0;
        foreach (var slot in slots)
        {
            var overall = overallByPlayer[slot.PlayerId];
            weightedSum += (long)overall * slot.Minutes;
            minuteSum += slot.Minutes;
            Console.WriteLine($"topRating={topRating} player={slot.PlayerId.Value} overall={overall} minutes={slot.Minutes} starter={slot.IsStarter}");
        }

        var strength = minuteSum == 0 ? 0 : weightedSum / minuteSum;
        Console.WriteLine($"topRating={topRating} => rosterCount={players.Count} totalMinutes={minuteSum} weightedStrength={strength}");
        Console.WriteLine();
    }
}

static int HashSeed(int a, int b)
{
    unchecked
    {
        var h = 17;
        h = (h * 31) + a;
        h = (h * 31) + b;
        return h;
    }
}

static MatchSetup Setup(int seed, int homeRating, int awayRating)
{
    var season = new Season(2031);
    var day = new SeasonDay(1);
    var home = new TeamId("TEAM-HOME");
    var away = new TeamId("TEAM-AWAY");

    var fixture = new Fixture(
        GameId.For(season, day, Math.Abs(seed) % 900),
        day,
        home,
        away,
        SeasonPhase.RegularSeason);

    return new MatchSetup(
        fixture,
        Team(home, homeRating),
        Team(away, awayRating),
        seed);
}

static MatchTeam Team(TeamId teamId, int topRating)
{
    var positions = Enum.GetValues<Position>();

    var players = Enumerable.Range(0, 10)
        .Select(index => new AvailablePlayer(
            new PlayerId($"{teamId.Value}-P{index:D2}"),
            positions[index % positions.Length],
            Math.Clamp(topRating - (index * 3), PlayerRating.MinimumOverall, PlayerRating.MaximumOverall)))
        .ToList();

    var build = new DepthChartBuilder().Build(teamId, players, new RosterSizeLimits(5, 15), players.Count);
    if (build.IsFailure)
    {
        throw new InvalidOperationException(string.Join("; ", build.Errors.Select(e => e.Message)));
    }

    return new MatchTeam(teamId, build.Value.Chart, players, MatchModelBounds.FullyRestedDays);
}
