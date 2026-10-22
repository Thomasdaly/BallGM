using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Players;
using BallGM.Infrastructure.Rulesets;
using BallGM.Rules.Configuration;
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

    /// <summary>How many of a roster's top slots draw their talent from the wide spread.</summary>
    private const int TopSlots = 3;

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
    public static LeaguePackEnvelope Envelope(int seed, int teamCount) =>
        Envelope(seed, GeneratedLeagueShape.Standard(teamCount), salaryFit: null);

    /// <summary>
    /// A league of any <paramref name="shape"/> as pack content, deterministic in the seed and the
    /// shape. With <paramref name="salaryFit"/>, salaries are put on that ruleset's scale — scaled to its
    /// soft cap (or hard cap) and held inside its floor and ceiling for each player's service — so a
    /// generated league fits whichever league settings it is generated for. Without it, the salaries
    /// are the calibration league's, unchanged.
    /// </summary>
    public static LeaguePackEnvelope Envelope(int seed, GeneratedLeagueShape shape, LeagueRuleset? salaryFit)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var random = new SeededRandomSource(SeedMixer.Mix(seed, "generated-league"));
        var teams = new List<LeaguePackTeamEnvelope>();
        var players = new List<LeaguePackPlayerEnvelope>();

        for (var team = 0; team < shape.TeamCount; team++)
        {
            var key = $"G{team:D2}";
            var name = $"{Cities[team]} {Nicknames[team]}";
            teams.Add(new LeaguePackTeamEnvelope(key, $"{Cities[team]} Basketball Club", name));

            // Team quality: a triangular draw over ±8, so most teams are middling and a few are not.
            var quality = random.NextInt32(0, 9) + random.NextInt32(0, 9) - 8;
            for (var slot = 0; slot < shape.RosterSize; slot++)
            {
                // The top of a roster varies far more than its middle: one team has two stars, another
                // none, which is where a league's leaders come from (a triangular draw over ±9 for the
                // first three, ±3 below them).
                var spread = slot < TopSlots ? random.NextInt32(0, 10) + random.NextInt32(0, 10) - 9 : random.NextInt32(-3, 4);
                var talent = Math.Clamp(SlotTalent[slot] + quality + spread, 35, 99);
                var age = 20 + random.NextInt32(0, 15);
                var rating = RatingProfileGenerator.Generate(talent, SlotPosition[slot], age, 30, PlayerRating.MaximumOverall, random);
                var service = Math.Max(0, age - 20);

                // Argument order is draw order: name, birth month and day, then contract length, exactly
                // as before shapes existed, so a standard shape still generates the calibration league.
                players.Add(new LeaguePackPlayerEnvelope(
                    ProspectNameBank.NextName(random),
                    SlotPosition[slot].ToString(),
                    new DateOnly(Season - age, 1 + random.NextInt32(0, 12), 1 + random.NextInt32(0, 28)),
                    service,
                    key,
                    PlayerSheetCodec.VisibleMap(rating),
                    null,
                    new LeaguePackContractEnvelope(Fit(Salaries(rating.Overall, 1 + random.NextInt32(0, 4)), service, salaryFit), null),
                    Hidden: PlayerSheetCodec.HiddenMap(rating),
                    Potential: rating.PotentialAbility,
                    Body: PlayerSheetCodec.BodyOf(rating),
                    Positions: PlayerSheetCodec.PositionMap(rating),
                    Traits: PlayerSheetCodec.TraitList(rating),
                    Tendencies: PlayerSheetCodec.TendencyMap(rating)));
            }

            if (salaryFit?.CapThresholds.HardCap is { } hardCap)
            {
                FitUnderHardCap(players, players.Count - shape.RosterSize, hardCap.SmallestUnits, salaryFit);
            }
        }

        var conferenceNames = shape.Conferences switch
        {
            0 => [],
            1 => ["League"],
            2 => new[] { "East", "West" },
            _ => Enumerable.Range(1, shape.Conferences).Select(number => $"Group {number}").ToArray(),
        };

        var perDivision = shape.Conferences == 0 ? 0 : shape.TeamCount / (shape.Conferences * shape.DivisionsPerConference);
        var conferences = conferenceNames
            .Select((conference, c) => new LeaguePackConferenceEnvelope(
                $"{conference} Conference",
                Enumerable.Range(0, shape.DivisionsPerConference)
                    .Select(d =>
                    {
                        var first = ((c * shape.DivisionsPerConference) + d) * perDivision;
                        return new LeaguePackDivisionEnvelope(
                            $"{conference} Division {d + 1}",
                            Enumerable.Range(first, perDivision).Select(team => $"G{team:D2}").ToList());
                    })
                    .ToList()))
            .ToList();

        return new LeaguePackEnvelope(LeaguePackDataSource.CurrentSchemaVersion, "Generated League", Season, null, conferences, teams, players);
    }

    /// <summary>The calibration league's salary scale is anchored to this soft cap.</summary>
    private const long CalibrationSoftCap = 141_000_000L;

    /// <summary>
    /// Scales one team's salaries down until its first-season payroll sits inside the hard cap, with
    /// a little room to sign someone — a generated team that starts over a hard cap could sign nobody,
    /// which is a league nobody asked for. Minimum salaries stay at the minimum.
    /// </summary>
    private static void FitUnderHardCap(List<LeaguePackPlayerEnvelope> players, int first, long hardCap, LeagueRuleset ruleset)
    {
        var target = hardCap * 97 / 100;

        // Minimum salaries cannot be scaled, so a pass can land short of the target; a few passes
        // converge, and a roster of minimums that is still over is a ruleset the checker reports.
        for (var pass = 0; pass < 8; pass++)
        {
            var payroll = players.Skip(first).Sum(player => player.Contract!.Salaries![0]);
            if (payroll <= target)
            {
                return;
            }

            for (var index = first; index < players.Count; index++)
            {
                var player = players[index];
                var floor = ruleset.NegotiationRules.CompensationFloor.FloorFor(player.SeasonsOfService ?? 0)?.SmallestUnits ?? 0;
                var salaries = player.Contract!.Salaries!.Select(salary => Math.Max(floor, (long)((decimal)salary * target / payroll))).ToArray();
                players[index] = player with { Contract = player.Contract with { Salaries = salaries } };
            }
        }
    }

    private static long[] Fit(long[] salaries, int service, LeagueRuleset? ruleset)
    {
        if (ruleset is null)
        {
            return salaries;
        }

        var anchor = ruleset.CapThresholds.SoftCap ?? ruleset.CapThresholds.HardCap;
        var floor = ruleset.NegotiationRules.CompensationFloor.FloorFor(service)?.SmallestUnits;
        var ceiling = ruleset.NegotiationRules.CompensationCeiling.CeilingFor(service, ruleset.CapThresholds.SoftCap)?.SmallestUnits;

        return salaries
            .Select(salary =>
            {
                var scaled = anchor is null ? salary : (long)((decimal)salary * anchor.SmallestUnits / CalibrationSoftCap);
                scaled = Math.Max(scaled, floor ?? scaled);
                return Math.Min(scaled, ceiling ?? scaled);
            })
            .ToArray();
    }

    /// <summary>A salary that rises with the square of talent above fifty, so a roster stays under a modern cap.</summary>
    private static long[] Salaries(int overall, int seasons)
    {
        var above = Math.Max(0, overall - 50);
        var salary = 1_150_000L + (above * above * 24_000L);
        return Enumerable.Repeat(salary, seasons).ToArray();
    }
}

/// <summary>
/// The shape of a generated league: how many teams, split into how many conferences of how many
/// divisions, each carrying how many players. Teams must divide evenly into the divisions; the name
/// pool caps a league at thirty teams and the talent ladder a roster at fifteen. Zero conferences is
/// a flat league — one table, and a postseason bracket seeded across the whole league with no final
/// between conference winners — the usual shape outside North America.
/// </summary>
public sealed record GeneratedLeagueShape
{
    public const int MaximumTeams = 30;
    public const int MaximumRosterSize = 15;

    private GeneratedLeagueShape(int teamCount, int conferences, int divisionsPerConference, int rosterSize)
    {
        TeamCount = teamCount;
        Conferences = conferences;
        DivisionsPerConference = divisionsPerConference;
        RosterSize = rosterSize;
    }

    public int TeamCount { get; }

    public int Conferences { get; }

    public int DivisionsPerConference { get; }

    public int RosterSize { get; }

    /// <summary>Two conferences of three divisions, fifteen to a roster: the calibration league's shape.</summary>
    public static GeneratedLeagueShape Standard(int teamCount) =>
        Create(teamCount, 2, 3, MaximumRosterSize).Value;

    public static DomainOperationResult<GeneratedLeagueShape> Create(int teamCount, int conferences, int divisionsPerConference, int rosterSize)
    {
        var groups = Math.Max(1, conferences * divisionsPerConference);
        if (conferences < 0 || (conferences > 0 && divisionsPerConference < 1) || teamCount < 2 || teamCount > MaximumTeams || teamCount % groups != 0)
        {
            return DomainOperationResult<GeneratedLeagueShape>.Failure(new DomainError(
                "generated_league.invalid_shape",
                $"A generated league of {teamCount} teams cannot be split into {ExplanationText.Count(conferences, "conference")} of {ExplanationText.Count(divisionsPerConference, "division")}: it needs between 2 and {MaximumTeams} teams, dividing evenly (or 0 conferences, for one flat table)."));
        }

        if (rosterSize is < 5 or > MaximumRosterSize)
        {
            return DomainOperationResult<GeneratedLeagueShape>.Failure(new DomainError(
                "generated_league.invalid_roster_size",
                $"A generated roster carries between 5 and {MaximumRosterSize} players, not {rosterSize}."));
        }

        return DomainOperationResult<GeneratedLeagueShape>.Success(new GeneratedLeagueShape(teamCount, conferences, divisionsPerConference, rosterSize));
    }
}
