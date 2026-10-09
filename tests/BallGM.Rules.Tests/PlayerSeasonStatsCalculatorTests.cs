using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

public sealed class PlayerSeasonStatsCalculatorTests
{
    private static readonly Season TestSeason = new(2030);
    private static readonly TeamId Home = new("team-home");
    private static readonly TeamId Away = new("team-away");
    private static readonly PlayerId HomeStar = new("player-home-star");
    private static readonly PlayerId AwayStar = new("player-away-star");

    [Fact]
    public void CalculateSumsAPlayersLinesAcrossEveryGamePlayed()
    {
        var results = new[]
        {
            MakeResult(day: 0, slot: 0, homePoints: 20, awayPoints: 10, homeStarPoints: 20, awayStarPoints: 10),
            MakeResult(day: 1, slot: 0, homePoints: 15, awayPoints: 12, homeStarPoints: 15, awayStarPoints: 12),
        };

        var lines = PlayerSeasonStatsCalculator.Calculate(results, SeasonPhase.RegularSeason);

        var homeStarLine = Assert.Single(lines, line => line.PlayerId == HomeStar);
        Assert.Equal(2, homeStarLine.Totals.Games);
        Assert.Equal(35, homeStarLine.Totals.Points);
        Assert.Equal(40, homeStarLine.Totals.Minutes);
        Assert.Equal(6, homeStarLine.Totals.Rebounds);
        Assert.Equal(4, homeStarLine.Totals.Assists);
    }

    [Fact]
    public void CalculateOmitsAPlayerWhoHasNotAppearedRatherThanReportingZeroes()
    {
        var results = new[] { MakeResult(day: 0, slot: 0, homePoints: 20, awayPoints: 10, homeStarPoints: 20, awayStarPoints: 10) };

        var lines = PlayerSeasonStatsCalculator.Calculate(results, SeasonPhase.RegularSeason);

        Assert.DoesNotContain(lines, line => line.PlayerId.Value == "nobody-played-this-player");
    }

    [Fact]
    public void CalculateReturnsNoLinesForAResultWithNoBoxScore()
    {
        var fixture = new Fixture(GameId.For(TestSeason, new SeasonDay(0), 0), new SeasonDay(0), Home, Away, SeasonPhase.RegularSeason);
        var result = GameResult.Create(fixture, homePoints: 20, awayPoints: 10).Value;

        var lines = PlayerSeasonStatsCalculator.Calculate([result], SeasonPhase.RegularSeason);

        Assert.Empty(lines);
    }

    [Fact]
    public void CalculateNeverMixesPostseasonGamesIntoTheRegularSeason()
    {
        var results = new[]
        {
            MakeResult(day: 0, slot: 0, homePoints: 20, awayPoints: 10, homeStarPoints: 20, awayStarPoints: 10),
            MakeResult(day: 90, slot: 0, homePoints: 30, awayPoints: 10, homeStarPoints: 30, awayStarPoints: 10, SeasonPhase.Postseason),
        };

        var regular = Assert.Single(PlayerSeasonStatsCalculator.Calculate(results, SeasonPhase.RegularSeason), line => line.PlayerId == HomeStar);
        var playoffs = Assert.Single(PlayerSeasonStatsCalculator.Calculate(results, SeasonPhase.Postseason), line => line.PlayerId == HomeStar);

        Assert.Equal((1, 20), (regular.Totals.Games, regular.Totals.Points));
        Assert.Equal((1, 30), (playoffs.Totals.Games, playoffs.Totals.Points));
    }

    private static GameResult MakeResult(int day, int slot, int homePoints, int awayPoints, int homeStarPoints, int awayStarPoints, SeasonPhase phase = SeasonPhase.RegularSeason)
    {
        var seasonDay = new SeasonDay(day);
        var fixture = new Fixture(GameId.For(TestSeason, seasonDay, slot), seasonDay, Home, Away, phase);

        // Each star's assists are on a teammate's baskets: a box score refuses an assist nobody else scored.
        PlayerStatLine[] lines =
        [
            new PlayerStatLine(
                HomeStar, Home, minutes: 20, points: homeStarPoints, offensiveRebounds: 1, defensiveRebounds: 2,
                assists: 2, usagePercent: 50, fieldGoalsAttempted: 0, fieldGoalsMade: 0, threePointsAttempted: 0,
                threePointsMade: 0, freeThrowsAttempted: homeStarPoints, freeThrowsMade: homeStarPoints, started: true),
            Teammate(new PlayerId("player-home-mate"), Home, madeTwos: 2),
            new PlayerStatLine(
                AwayStar, Away, minutes: 18, points: awayStarPoints, offensiveRebounds: 1, defensiveRebounds: 3,
                assists: 1, usagePercent: 50, fieldGoalsAttempted: 0, fieldGoalsMade: 0, threePointsAttempted: 0,
                threePointsMade: 0, freeThrowsAttempted: awayStarPoints, freeThrowsMade: awayStarPoints, started: true),
            Teammate(new PlayerId("player-away-mate"), Away, madeTwos: 1),
        ];
        homePoints += 4;
        awayPoints += 2;
        var (homeLine, awayLine) = BalancedTeamLines.For(Home, Away, lines);
        var boxScore = BoxScore.Create(fixture.Id, homePoints, awayPoints, lines, homeLine, awayLine).Value;

        return GameResult.Create(fixture, homePoints, awayPoints, boxScore).Value;
    }

    private static PlayerStatLine Teammate(PlayerId playerId, TeamId teamId, int madeTwos) =>
        new(playerId, teamId, minutes: 10, points: 2 * madeTwos, offensiveRebounds: 0, defensiveRebounds: 0, assists: 0,
            usagePercent: 50, fieldGoalsAttempted: madeTwos, fieldGoalsMade: madeTwos, threePointsAttempted: 0,
            threePointsMade: 0, freeThrowsAttempted: 0, freeThrowsMade: 0, started: false);
}
