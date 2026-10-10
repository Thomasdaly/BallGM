using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Rulesets;
using BallGM.Rules.Draft;
using BallGM.Rules.Players;

namespace BallGM.Infrastructure.Fixtures;

/// <summary>
/// A fictional league of any size divisible into two conferences of three divisions, generated from
/// a seed rather than authored: the yardstick the league-shape calibration (sim-audit battery 8)
/// measures against, because the six-team fixture cannot show a thirty-team league's tails. It is
/// built as a league pack and loaded through <see cref="LeaguePackDataSource"/>, so it is validated
/// exactly as authored content is. The same seed and size always give the same league.
/// </summary>
public sealed class GeneratedLeagueDataSource : ILeagueDataSource
{
    private const int Season = 2031;
    private const int RosterSize = 15;

    private static readonly string[] Cities =
    [
        "Ashford", "Brightwater", "Calder Bay", "Dunmore", "Eastvale", "Fairhaven", "Glenrock", "Harrowgate",
        "Ironbridge", "Juniper Falls", "Kestrel Point", "Lakemont", "Marrow Creek", "Northcliff", "Oakhurst",
        "Pinecrest", "Queensport", "Redmarsh", "Silverlake", "Thornbury", "Upton Vale", "Valecross",
        "Westmere", "Yarrowby", "Zephyr Heights", "Amberley", "Blackwater", "Coldharbour", "Driftwood", "Emberton",
    ];

    private static readonly string[] Nicknames =
    [
        "Foxes", "Comets", "Mariners", "Stags", "Herons", "Lanterns", "Owls", "Rams", "Forgers", "Pines",
        "Kestrels", "Tides", "Bison", "Ravens", "Oaks", "Lynx", "Captains", "Marsh Hawks", "Otters", "Wolves",
        "Vipers", "Knights", "Gulls", "Badgers", "Zephyrs", "Hornets", "Pilots", "Mammoths", "Drifters", "Sparks",
    ];

    /// <summary>A roster's talent from its star down to its fifteenth man, before team quality and noise.</summary>
    private static readonly int[] SlotTalent = [86, 81, 77, 74, 71, 68, 66, 64, 62, 60, 58, 56, 54, 52, 50];

    /// <summary>Positions down a roster: a starting five, then the bench covering each spot again.</summary>
    private static readonly Position[] SlotPosition =
    [
        Position.PointGuard, Position.ShootingGuard, Position.SmallForward, Position.PowerForward, Position.Center,
        Position.PointGuard, Position.ShootingGuard, Position.SmallForward, Position.PowerForward, Position.Center,
        Position.PointGuard, Position.ShootingGuard, Position.SmallForward, Position.PowerForward, Position.Center,
    ];

    private readonly string _rulesetFilePath;
    private readonly int _seed;
    private readonly int _teamCount;

    public GeneratedLeagueDataSource(string rulesetFilePath, int seed, int teamCount = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetFilePath);
        if (teamCount < 6 || teamCount % 6 != 0 || teamCount > Cities.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(teamCount), teamCount, $"A generated league has a multiple of six teams, at most {Cities.Length}.");
        }

        _rulesetFilePath = rulesetFilePath;
        _seed = seed;
        _teamCount = teamCount;
    }

    /// <summary>The shipped 30-team, 82-game ruleset the calibration league plays under.</summary>
    public static string CalibrationRulesetFilePath =>
        Path.Combine(Path.GetDirectoryName(FixtureLeagueDataSource.DefaultRulesetFilePath) ?? string.Empty, "calibration-league.json");

    public DomainOperationResult<LeagueSnapshot> Load()
    {
        if (!File.Exists(_rulesetFilePath))
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(new DomainError(
                "generated_league.ruleset_file_missing",
                $"No ruleset file at '{_rulesetFilePath}'."));
        }

        var ruleset = new LeagueRulesetSerializer().Deserialize(File.ReadAllText(_rulesetFilePath));
        return ruleset.IsFailure
            ? DomainOperationResult<LeagueSnapshot>.Failure(ruleset.Errors.ToArray())
            : LeaguePackDataSource.FromEnvelope(Envelope(_seed, _teamCount), ruleset.Value);
    }

    /// <summary>The league as pack content: deterministic in the seed and the size.</summary>
    public static LeaguePackEnvelope Envelope(int seed, int teamCount)
    {
        var random = new SeededRandomSource(SeedMixer.Mix(seed, "generated-league"));
        var teams = new List<LeaguePackTeamEnvelope>();
        var players = new List<LeaguePackPlayerEnvelope>();

        for (var team = 0; team < teamCount; team++)
        {
            var key = $"G{team:D2}";
            var name = $"{Cities[team]} {Nicknames[team]}";
            teams.Add(new LeaguePackTeamEnvelope(key, $"{Cities[team]} Basketball Club", name));

            // Team quality: a triangular draw over ±8, so most teams are middling and a few are not.
            var quality = random.NextInt32(0, 9) + random.NextInt32(0, 9) - 8;
            for (var slot = 0; slot < RosterSize; slot++)
            {
                var talent = Math.Clamp(SlotTalent[slot] + quality + random.NextInt32(-3, 4), 35, 99);
                var rating = RatingProfileGenerator.Generate(talent, 30, PlayerRating.MaximumOverall, random);
                var age = 20 + random.NextInt32(0, 15);
                players.Add(new LeaguePackPlayerEnvelope(
                    ProspectNameBank.NextName(random),
                    SlotPosition[slot].ToString(),
                    new DateOnly(Season - age, 1 + random.NextInt32(0, 12), 1 + random.NextInt32(0, 28)),
                    Math.Max(0, age - 20),
                    key,
                    new LeaguePackRatingEnvelope(rating.Height, rating.Speed, rating.Strength, rating.Passing, rating.LateralQuickness),
                    null,
                    new LeaguePackContractEnvelope(Salaries(rating.Overall, 1 + random.NextInt32(0, 4)), null)));
            }
        }

        var conferences = new[] { "East", "West" }
            .Select((conference, c) => new LeaguePackConferenceEnvelope(
                $"{conference} Conference",
                Enumerable.Range(0, 3)
                    .Select(d =>
                    {
                        var perDivision = teamCount / 6;
                        var first = ((c * 3) + d) * perDivision;
                        return new LeaguePackDivisionEnvelope(
                            $"{conference} Division {d + 1}",
                            Enumerable.Range(first, perDivision).Select(team => $"G{team:D2}").ToList());
                    })
                    .ToList()))
            .ToList();

        return new LeaguePackEnvelope(LeaguePackDataSource.CurrentSchemaVersion, "Generated League", Season, null, conferences, teams, players);
    }

    /// <summary>A salary that rises with the square of talent above fifty, so a roster stays under a modern cap.</summary>
    private static long[] Salaries(int overall, int seasons)
    {
        var above = Math.Max(0, overall - 50);
        var salary = 1_150_000L + (above * above * 24_000L);
        return Enumerable.Repeat(salary, seasons).ToArray();
    }
}
