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
                Line(new PlayerId("H1"), Home, 20, 6, 1, 1, 0, 60),
                Line(new PlayerId("H2"), Home, 20, 4, 1, 1, 0, 40),
                Line(new PlayerId("A1"), Away, 20, 5, 1, 1, 0, 100),
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
                Line(new PlayerId("H1"), Home, 20, 6, 1, 1, 0, 60),
                Line(new PlayerId("H2"), Home, 20, 4, 1, 1, 0, 30),
                Line(new PlayerId("A1"), Away, 20, 5, 1, 1, 0, 100),
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
                Line(new PlayerId("H1"), Home, 20, 5, 1, 1, 0, 100),
            ]);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void ThreesMadeCannotExceedFieldGoalsMade()
    {
        // 5 FGA, 1 FGM, 5 3PA, 3 3PM would mean negative two-point makes, yet it adds up to 5 points.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerStatLine(
            new PlayerId("P1"), Home, minutes: 20, points: 5, offensiveRebounds: 0, defensiveRebounds: 0,
            assists: 0, usagePercent: 100, fieldGoalsAttempted: 5, fieldGoalsMade: 1, threePointsAttempted: 5,
            threePointsMade: 3, freeThrowsAttempted: 0, freeThrowsMade: 0, started: true));
    }

    [Fact]
    public void CreateRefusesALiveMissThatNoReboundAccountsFor()
    {
        // Home misses 3 field goals; only 2 rebounds are recorded off them, and no team rebound.
        PlayerStatLine[] lines =
        [
            Shooter(new PlayerId("H1"), Home, fga: 5, fgm: 2, oreb: 1, dreb: 0, ast: 0),
            Shooter(new PlayerId("A1"), Away, fga: 2, fgm: 2, oreb: 0, dreb: 1, ast: 0),
        ];

        var result = BoxScore.Create(
            GameId.For(Season, SeasonDay.Opening, 0), 4, 4 + 1, lines,
            new TeamStatLine(Home, 0, 0, 0), new TeamStatLine(Away, 0, 0, 0));

        Assert.Contains(result.Errors, error => error.Code == "box_score.rebounds_do_not_account_for_misses");
    }

    [Fact]
    public void CreateAcceptsMissesAccountedForByPlayerTeamAndFreeThrowRebounds()
    {
        // Home: 3 missed FGs + 1 live FT miss = 4 = own OREB 1 + own team OREB 1 + away DREB 1 + away team DREB 1.
        PlayerStatLine[] lines =
        [
            Shooter(new PlayerId("H1"), Home, fga: 5, fgm: 2, oreb: 1, dreb: 0, ast: 0, ftm: 0, fta: 1),
            Shooter(new PlayerId("A1"), Away, fga: 2, fgm: 2, oreb: 0, dreb: 1, ast: 0),
        ];

        var result = BoxScore.Create(
            GameId.For(Season, SeasonDay.Opening, 0), 4, 4, lines,
            new TeamStatLine(Home, 1, 0, 1), new TeamStatLine(Away, 0, 1, 0));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void CreateRefusesAnAssistOnAPlayersOwnBasket()
    {
        // The only home basket is H1's own; H1 cannot have assisted it.
        PlayerStatLine[] lines =
        [
            Shooter(new PlayerId("H1"), Home, fga: 1, fgm: 1, oreb: 0, dreb: 0, ast: 1, usage: 50),
            Shooter(new PlayerId("H2"), Home, fga: 0, fgm: 0, oreb: 0, dreb: 0, ast: 0, usage: 50),
            Shooter(new PlayerId("A1"), Away, fga: 0, fgm: 0, oreb: 0, dreb: 0, ast: 0),
        ];
        var (home, away) = BalancedTeamLines.For(Home, Away, lines);

        var result = BoxScore.Create(GameId.For(Season, SeasonDay.Opening, 0), 2, 0, lines, home, away);

        Assert.Contains(result.Errors, error => error.Code == "box_score.assists_exceed_teammate_makes");
    }

    private static PlayerStatLine Shooter(
        PlayerId playerId, TeamId teamId, int fga, int fgm, int oreb, int dreb, int ast, int ftm = 0, int fta = 0, int usage = 100) =>
        new(
            playerId, teamId, minutes: 20, points: (2 * fgm) + ftm, oreb, dreb, ast, usage,
            fieldGoalsAttempted: fga, fieldGoalsMade: fgm, threePointsAttempted: 0, threePointsMade: 0,
            freeThrowsAttempted: fta, freeThrowsMade: ftm, started: true);

    private static DomainOperationResult<BoxScore> Build(
        int homePoints, int awayPoints, IReadOnlyCollection<PlayerStatLine> lines)
    {
        var (home, away) = BalancedTeamLines.For(Home, Away, lines);
        return BoxScore.Create(GameId.For(Season, SeasonDay.Opening, 0), homePoints, awayPoints, lines, home, away);
    }

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
