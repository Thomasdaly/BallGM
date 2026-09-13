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
        var line = new PlayerStatLine(
            new PlayerId("P1"), Home, minutes: 30, points: 10,
            offensiveRebounds: 3, defensiveRebounds: 5, assists: 2, usagePercent: 100, started: true);

        Assert.Equal(8, line.Rebounds);
    }

    [Fact]
    public void CreateSucceedsWhenEveryTeamsUsageSharesSumToOneHundred()
    {
        var result = Build(
            homePoints: 10,
            awayPoints: 5,
            [
                new PlayerStatLine(new PlayerId("H1"), Home, 20, 6, 1, 1, 1, 60, true),
                new PlayerStatLine(new PlayerId("H2"), Home, 20, 4, 1, 1, 1, 40, true),
                new PlayerStatLine(new PlayerId("A1"), Away, 20, 5, 1, 1, 1, 100, true),
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
                new PlayerStatLine(new PlayerId("H1"), Home, 20, 6, 1, 1, 1, 60, true),
                new PlayerStatLine(new PlayerId("H2"), Home, 20, 4, 1, 1, 1, 30, true),
                new PlayerStatLine(new PlayerId("A1"), Away, 20, 5, 1, 1, 1, 100, true),
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
                new PlayerStatLine(new PlayerId("H1"), Home, 20, 5, 1, 1, 1, 100, true),
            ]);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    private static DomainOperationResult<BoxScore> Build(
        int homePoints, int awayPoints, IEnumerable<PlayerStatLine> lines) =>
        BoxScore.Create(GameId.For(Season, SeasonDay.Opening, 0), Home, Away, homePoints, awayPoints, lines);
}
