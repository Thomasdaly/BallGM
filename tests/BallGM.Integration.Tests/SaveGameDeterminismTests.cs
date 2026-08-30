using BallGM.Application.Leagues;
using BallGM.Application.Saves;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// The acceptance test for the save game: a season saved partway through, reloaded, and finished
/// produces the same champion and the same box scores, game for game, as the same season played
/// straight through without ever saving.
/// <para>
/// <b>Why this starts from one <see cref="LeagueSession.Load"/>, not two.</b>
/// <see cref="FixtureLeagueDataSource"/> mints every identifier fresh with
/// <c>SortableId.NewId()</c> on each <c>Load()</c>, and the schedule generator orders teams by
/// identifier before it shuffles — so two separate loads are two different leagues that merely share
/// a name, exactly as <c>PlayedSeasonTests.DifferentSeedsProduceDifferentSeasons</c> already
/// documents. Comparing an uninterrupted run against a save-and-resume run therefore has to start
/// from the <em>same</em> loaded league: this loads the fixture exactly once, immediately saves that
/// pre-season snapshot to get identifier-stable JSON, and reloads <em>that save</em> into every
/// session the test actually plays a season on.
/// </para>
/// </summary>
public sealed class SaveGameDeterminismTests
{
    private const int Seed = 4242;

    [Fact]
    public void SavingMidSeasonAndResumingMatchesAnUninterruptedRunExactly()
    {
        var store = new SaveGameSerializer();

        var initialSession = NewSession(store);
        Assert.True(initialSession.Load().IsSuccess);

        var initialSave = initialSession.Save();
        Assert.True(initialSave.IsSuccess, string.Join("; ", initialSave.Errors.Select(error => error.Message)));

        // Branch A: the same league, played straight through without ever saving mid-season.
        var branchA = LoadedFrom(store, initialSave.Value);
        Assert.True(branchA.StartSeason(Seed).IsSuccess);
        var totalDays = branchA.Season().Value.Calendar.LengthInDays;
        Assert.True(branchA.AdvanceDays(totalDays).IsSuccess);

        // Branch B: the same league and the same seed, saved halfway through the season, reloaded
        // into a fresh session, and finished from there.
        var branchB = LoadedFrom(store, initialSave.Value);
        Assert.True(branchB.StartSeason(Seed).IsSuccess);

        var halfway = totalDays / 2;
        Assert.True(branchB.AdvanceDays(halfway).IsSuccess);

        var midSave = branchB.Save();
        Assert.True(midSave.IsSuccess, string.Join("; ", midSave.Errors.Select(error => error.Message)));

        var resumed = LoadedFrom(store, midSave.Value);
        Assert.True(resumed.AdvanceDays(totalDays - halfway).IsSuccess);

        // Every game, in every day of the season, scored identically.
        for (var day = 0; day < totalDays; day++)
        {
            var uninterrupted = branchA.BoxScoresOn(day).Value;
            var resumedDay = resumed.BoxScoresOn(day).Value;

            Assert.Equal(
                uninterrupted.Select(game => (game.GameId, game.HomeTeamId, game.HomePoints, game.AwayTeamId, game.AwayPoints)),
                resumedDay.Select(game => (game.GameId, game.HomeTeamId, game.HomePoints, game.AwayTeamId, game.AwayPoints)));
        }

        // And the same champion, concluded from each session independently.
        var championA = branchA.ConcludeSeason();
        var championResumed = resumed.ConcludeSeason();

        Assert.True(championA.IsSuccess, string.Join("; ", championA.Errors.Select(error => error.Message)));
        Assert.True(championResumed.IsSuccess, string.Join("; ", championResumed.Errors.Select(error => error.Message)));
        Assert.Equal(championA.Value.ChampionTeamId, championResumed.Value.ChampionTeamId);
        Assert.Equal(
            championA.Value.FinalStandings.Select(row => (row.TeamId, row.Wins, row.Losses)),
            championResumed.Value.FinalStandings.Select(row => (row.TeamId, row.Wins, row.Losses)));
    }

    private static LeagueSession LoadedFrom(ISaveGameStore store, string json)
    {
        var session = NewSession(store);
        var loaded = session.LoadSave(json);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Errors.Select(error => error.Message)));
        return session;
    }

    private static LeagueSession NewSession(ISaveGameStore store) =>
        new(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            store);
}
