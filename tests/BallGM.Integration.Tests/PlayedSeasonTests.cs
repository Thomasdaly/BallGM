using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// A whole season played through the real stack: the fixture league loaded from disk, its calendar
/// and schedule generated, every game decided by the match engine, the table built from the results,
/// and the postseason drawn from the table.
/// <para>
/// This is the test that would notice the season stopping halfway, the standings disagreeing with
/// the games, or a bracket that never finishes — none of which any single-game test can see.
/// </para>
/// </summary>
public sealed class PlayedSeasonTests
{
    [Fact]
    public void AWholeSeasonPlaysThroughToAChampion()
    {
        var session = NewSession();
        var start = session.StartSeason(seed: 2031);
        Assert.True(start.IsSuccess, string.Join("; ", start.Errors.Select(error => error.Message)));

        var advanced = session.AdvanceDays(start.Value.Calendar.LengthInDays);
        Assert.True(advanced.IsSuccess, string.Join("; ", advanced.Errors.Select(error => error.Message)));

        var season = session.Season().Value;

        Assert.True(season.Calendar.IsComplete);
        Assert.Equal(season.Calendar.ScheduledGames, season.Calendar.PlayedGames);
        Assert.True(season.Calendar.PlayedGames > 0, "A season that played no games has not been played.");

        // Every team's wins and losses account for every game it was scheduled in, so the table and
        // the schedule cannot have drifted apart.
        foreach (var row in season.Standings.Rows)
        {
            Assert.True(row.GamesPlayed > 0, $"{row.TeamName} played no games.");
            Assert.Equal(row.GamesPlayed, row.Wins + row.Losses);
        }

        // The league's games are its teams' games, counted twice — once for each side.
        var regularSeasonGames = season.Standings.Rows.Sum(row => row.GamesPlayed);
        Assert.Equal(0, regularSeasonGames % 2);
    }

    [Fact]
    public void ThePlayedSeasonProducesATableThatLooksLikeABasketballSeason()
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed: 2031).IsSuccess);
        Assert.True(session.AdvanceDays(session.Season().Value.Calendar.LengthInDays).IsSuccess);

        var standings = session.Season().Value.Standings;

        // Somebody has a winning record and somebody has a losing one. A model with no spread would
        // produce a table of .500 teams and a league with nothing to manage.
        Assert.Contains(standings.Rows, row => row.Wins > row.Losses);
        Assert.Contains(standings.Rows, row => row.Losses > row.Wins);

        var best = standings.Rows.Max(row => row.Wins);
        var worst = standings.Rows.Min(row => row.Wins);
        Assert.True(best - worst >= 5, $"The best team won {best} and the worst {worst}; that is not a league.");

        // Nobody goes undefeated and nobody goes winless over a full season.
        Assert.All(standings.Rows, row => Assert.True(row.Wins > 0 && row.Losses > 0));

        // Scoring is in the right register, league-wide.
        var pointsPerGame = standings.Rows.Sum(row => (long)row.PointsFor) /
            (double)standings.Rows.Sum(row => row.GamesPlayed);

        Assert.InRange(pointsPerGame, 95, 120);
    }

    [Fact]
    public void ThePostseasonIsPlayedFromTheTableTheSeasonProduced()
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed: 2031).IsSuccess);
        Assert.True(session.AdvanceDays(session.Season().Value.Calendar.LengthInDays).IsSuccess);

        var season = session.Season().Value;
        var standings = season.Standings;

        // The fixture league qualifies two teams per conference, so the bracket is a conference
        // final each side plus the final between the winners.
        var qualified = standings.Rows
            .GroupBy(row => row.ConferenceName)
            .SelectMany(conference => conference.Take(2))
            .Select(row => row.TeamId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, qualified.Count);

        var postseasonDays = season.Calendar.Phases.Single(phase => phase.Phase == "Postseason");

        var bracketGames = Enumerable.Range(postseasonDays.StartDay, postseasonDays.EndDayExclusive - postseasonDays.StartDay)
            .SelectMany(day => session.BoxScoresOn(day).Value)
            .ToList();

        Assert.NotEmpty(bracketGames);

        // Nobody who missed the postseason appears in it.
        Assert.All(bracketGames, game =>
        {
            Assert.Contains(game.HomeTeamId, qualified);
            Assert.Contains(game.AwayTeamId, qualified);
        });
    }

    [Fact]
    public void EveryPlayedGameCarriesABoxScoreThatAddsUpToItsResult()
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed: 7).IsSuccess);
        Assert.True(session.AdvanceDays(40).IsSuccess);

        var checkedGames = 0;

        for (var day = 0; day < 40; day++)
        {
            foreach (var game in session.BoxScoresOn(day).Value)
            {
                Assert.True(game.HasBoxScore, $"Game '{game.GameId}' was played without a box score.");
                Assert.Equal(game.HomePoints, game.HomeLines.Sum(line => line.Points));
                Assert.Equal(game.AwayPoints, game.AwayLines.Sum(line => line.Points));
                Assert.NotEqual(game.HomePoints, game.AwayPoints);

                checkedGames++;
            }
        }

        Assert.True(checkedGames > 0, "No games were played in the first forty days of the season.");
    }

    /// <summary>
    /// Two seasons of the same league on different seeds are different seasons.
    /// <para>
    /// Note what is deliberately <em>not</em> asserted here: that the same seed replays the same
    /// season across two loads. It cannot, and not because the engine is non-deterministic —
    /// <c>FixtureLeagueDataSource</c> mints its identifiers with <c>SortableId.NewId()</c> on every
    /// load, and the schedule generator orders teams by identifier before it shuffles, so two loads
    /// are two different leagues that merely share a name. Exact replay is proved in the simulation
    /// suite, where the league is fixed and the comparison means something.
    /// </para>
    /// </summary>
    [Fact]
    public void DifferentSeedsProduceDifferentSeasons()
    {
        var first = PlayFortyDays(seed: 99);
        var second = PlayFortyDays(seed: 100);

        Assert.NotEqual(first.Table, second.Table);

        // Both are still recognisably the same league playing the same schedule shape.
        Assert.Equal(first.GamesPlayed, second.GamesPlayed);
        Assert.InRange(first.PointsPerGame, 95, 120);
        Assert.InRange(second.PointsPerGame, 95, 120);
    }

    private static (string Table, int GamesPlayed, double PointsPerGame) PlayFortyDays(int seed)
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed).IsSuccess);
        Assert.True(session.AdvanceDays(40).IsSuccess);

        var season = session.Season().Value;
        var rows = season.Standings.Rows;

        // Keyed on the win column rather than on identifiers, which differ between loads.
        var table = string.Join("|", rows.Select(row => $"{row.Wins}-{row.Losses}").OrderBy(value => value, StringComparer.Ordinal));

        return (
            table,
            season.Calendar.PlayedGames,
            rows.Sum(row => (long)row.PointsFor) / (double)Math.Max(1, rows.Sum(row => row.GamesPlayed)));
    }

    private static LeagueSession NewSession()
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
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return session;
    }
}
