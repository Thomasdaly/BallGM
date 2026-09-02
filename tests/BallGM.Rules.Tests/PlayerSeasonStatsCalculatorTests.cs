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

        var lines = PlayerSeasonStatsCalculator.Calculate(results);

        var homeStarLine = Assert.Single(lines, line => line.PlayerId == HomeStar);
        Assert.Equal(2, homeStarLine.GamesPlayed);
        Assert.Equal(35, homeStarLine.TotalPoints);
        Assert.Equal(40, homeStarLine.TotalMinutes);
        Assert.Equal(6, homeStarLine.TotalRebounds);
        Assert.Equal(4, homeStarLine.TotalAssists);
    }

    [Fact]
    public void CalculateOmitsAPlayerWhoHasNotAppearedRatherThanReportingZeroes()
    {
        var results = new[] { MakeResult(day: 0, slot: 0, homePoints: 20, awayPoints: 10, homeStarPoints: 20, awayStarPoints: 10) };

        var lines = PlayerSeasonStatsCalculator.Calculate(results);

        Assert.DoesNotContain(lines, line => line.PlayerId.Value == "nobody-played-this-player");
    }

    [Fact]
    public void CalculateReturnsNoLinesForAResultWithNoBoxScore()
    {
        var fixture = new Fixture(GameId.For(TestSeason, new SeasonDay(0), 0), new SeasonDay(0), Home, Away, SeasonPhase.RegularSeason);
        var result = GameResult.Create(fixture, homePoints: 20, awayPoints: 10).Value;

        var lines = PlayerSeasonStatsCalculator.Calculate([result]);

        Assert.Empty(lines);
    }

    private static GameResult MakeResult(int day, int slot, int homePoints, int awayPoints, int homeStarPoints, int awayStarPoints)
    {
        var seasonDay = new SeasonDay(day);
        var fixture = new Fixture(GameId.For(TestSeason, seasonDay, slot), seasonDay, Home, Away, SeasonPhase.RegularSeason);

        var boxScore = BoxScore.Create(
            fixture.Id,
            Home,
            Away,
            homePoints,
            awayPoints,
            [
                new PlayerStatLine(HomeStar, Home, minutes: 20, points: homeStarPoints, rebounds: 3, assists: 2, started: true),
                new PlayerStatLine(AwayStar, Away, minutes: 18, points: awayStarPoints, rebounds: 4, assists: 1, started: true),
            ]).Value;

        return GameResult.Create(fixture, homePoints, awayPoints, boxScore).Value;
    }
}
