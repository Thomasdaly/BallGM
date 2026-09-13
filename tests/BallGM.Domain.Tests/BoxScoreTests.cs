using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class BoxScoreTests
{
    private static readonly Season Season = new(2031);
    private static readonly TeamId Home = new("TEAM-HOME");
    private static readonly TeamId Away = new("TEAM-AWAY");

    [Fact]
    public void ReboundsIsTheSumOfTheOffensiveAndDefensiveSplit()
    {
        var line = Line(new PlayerId("P1"), Home, minutes: 30, points: 10, offensiveRebounds: 3, defensiveRebounds: 5, assists: 2, usagePercent: 100);

        Assert.Equal(8, line.Rebounds);
    }

    [Fact]
    public void PointsMustEqualTheShootingLineItWasScoredWith()
    {
        Assert.Throws<ArgumentException>(() => new PlayerStatLine(
            new PlayerId("P1"), Home, minutes: 20, points: 10, offensiveRebounds: 0, defensiveRebounds: 0,
            assists: 0, usagePercent: 100, fieldGoalsAttempted: 4, fieldGoalsMade: 4, threePointsAttempted: 0,
            threePointsMade: 0, freeThrowsAttempted: 0, freeThrowsMade: 0, started: true));
    }

    [Fact]
    public void ASingleThreePointMakeAndAnAndOneAddUpCorrectly()
    {
        // 1 three (3), plus a separate and-one trip on a two (2 + 1) = 6 points from 2 FGA (1 make of
        // each type) and 1 FTA made.
        var line = new PlayerStatLine(
            new PlayerId("P1"), Home, minutes: 20, points: 6, offensiveRebounds: 0, defensiveRebounds: 0,
            assists: 0, usagePercent: 100, fieldGoalsAttempted: 2, fieldGoalsMade: 2, threePointsAttempted: 1,
            threePointsMade: 1, freeThrowsAttempted: 1, freeThrowsMade: 1, started: true);

        Assert.Equal(6, line.Points);
    }

    [Fact]
    public void CreateSucceedsWhenEveryTeamsUsageSharesSumToOneHundred()
    {
        var result = Build(
            homePoints: 10,
            awayPoints: 5,
            [
                Line(new PlayerId("H1"), Home, 20, 6, 1, 1, 1, 60),
                Line(new PlayerId("H2"), Home, 20, 4, 1, 1, 1, 40),
                Line(new PlayerId("A1"), Away, 20, 5, 1, 1, 1, 100),
            ]);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void CreateRefusesATeamWhoseUsageSharesDoNotSumToOneHundred()
    {
        var result = Build(
            homePoints: 10,
            awayPoints: 5,
            [
                Line(new PlayerId("H1"), Home, 20, 6, 1, 1, 1, 60),
                Line(new PlayerId("H2"), Home, 20, 4, 1, 1, 1, 30),
                Line(new PlayerId("A1"), Away, 20, 5, 1, 1, 1, 100),
            ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "box_score.usage_percent_does_not_sum_to_whole");
    }

    [Fact]
    public void CreateDoesNotCheckUsageForATeamWithNoLinesAtAll()
    {
        // Nothing to sum is not the same as summing to the wrong number — a team with no lines is
        // its own, separately reported situation, not a usage-share violation.
        var result = Build(
            homePoints: 5,
            awayPoints: 0,
            [
                Line(new PlayerId("H1"), Home, 20, 5, 1, 1, 1, 100),
            ]);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    private static DomainOperationResult<BoxScore> Build(
        int homePoints, int awayPoints, IEnumerable<PlayerStatLine> lines) =>
        BoxScore.Create(GameId.For(Season, SeasonDay.Opening, 0), Home, Away, homePoints, awayPoints, lines);

    /// <summary>
    /// A line whose shooting composition does not matter to the test that builds it — every point
    /// comes from an (unrealistic but internally consistent) run of free throws, so the caller only
    /// has to state the one figure it actually cares about.
    /// </summary>
    private static PlayerStatLine Line(
        PlayerId playerId, TeamId teamId, int minutes, int points, int offensiveRebounds, int defensiveRebounds,
        int assists, int usagePercent) =>
        new(
            playerId, teamId, minutes, points, offensiveRebounds, defensiveRebounds, assists, usagePercent,
            fieldGoalsAttempted: 0, fieldGoalsMade: 0, threePointsAttempted: 0, threePointsMade: 0,
            freeThrowsAttempted: points, freeThrowsMade: points, started: true);
}
