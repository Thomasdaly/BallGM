using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;
using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>Builds the two-sided setups the match engine tests are run against.</summary>
internal static class MatchTestFixtures
{
    public static readonly Season Season = new(2031);

    public static readonly SeasonDay GameDay = new(1);

    public static MatchSetup Setup(
        int seed,
        int homeRating = 72,
        int awayRating = 72,
        int homeRestDays = MatchModelBounds.FullyRestedDays,
        int awayRestDays = MatchModelBounds.FullyRestedDays,
        int homeSquadSize = 10,
        int awaySquadSize = 10)
    {
        var home = new TeamId("TEAM-HOME");
        var away = new TeamId("TEAM-AWAY");

        var fixture = new Fixture(
            GameId.For(Season, GameDay, Math.Abs(seed) % 900),
            GameDay,
            home,
            away,
            SeasonPhase.RegularSeason);

        return new MatchSetup(
            fixture,
            Team(home, homeRating, homeRestDays, homeSquadSize),
            Team(away, awayRating, awayRestDays, awaySquadSize),
            seed);
    }

    /// <summary>
    /// A squad whose best player is <paramref name="topRating"/> and which falls away three points a
    /// place, covering all five positions before it doubles up.
    /// </summary>
    public static MatchTeam Team(TeamId teamId, int topRating, int restDays, int squadSize = 10)
    {
        var positions = Enum.GetValues<Position>();

        var players = Enumerable.Range(0, squadSize)
            .Select(index => new AvailablePlayer(
                new PlayerId($"{teamId.Value}-P{index:D2}"),
                positions[index % positions.Length],
                Math.Clamp(topRating - (index * 3), PlayerRating.MinimumOverall, PlayerRating.MaximumOverall)))
            .ToList();

        var build = new DepthChartBuilder().Build(teamId, players, new RosterSizeLimits(5, 15), players.Count);
        Assert.True(build.IsSuccess);

        return new MatchTeam(teamId, build.Value.Chart, players, restDays);
    }

    /// <summary>Plays a run of games and hands back every outcome, for the distribution assertions.</summary>
    public static IReadOnlyList<MatchOutcome> PlayMany(
        int games,
        int homeRating = 72,
        int awayRating = 72,
        int homeRestDays = MatchModelBounds.FullyRestedDays,
        int awayRestDays = MatchModelBounds.FullyRestedDays,
        int seedOffset = 0)
    {
        var engine = new PossessionMatchEngine();
        var outcomes = new List<MatchOutcome>(games);

        for (var game = 0; game < games; game++)
        {
            var played = engine.Play(Setup(seedOffset + game, homeRating, awayRating, homeRestDays, awayRestDays));
            Assert.True(played.IsSuccess, string.Join("; ", played.Errors.Select(error => error.Message)));
            outcomes.Add(played.Value);
        }

        return outcomes;
    }
}
