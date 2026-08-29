using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

/// <summary>
/// The season boundary as a rule: what a finished season leaves behind, and the two ways concluding
/// one can be refused. Nothing here touches sequencing or the calendar advancing itself — that is
/// <c>SeasonEngine</c>'s, and is exercised against the real fixture stack in the integration suite.
/// </summary>
public sealed class SeasonConclusionTests
{
    private static readonly Season Season = SeasonTestLeague.Season;

    [Fact]
    public void ConcludesARegularSeasonWithNoPostseasonAndRecordsNoChampion()
    {
        var league = SeasonTestLeague.Flat(2);
        var run = PlayedRegularSeason(league, [(0, 1, 101, 90)]);
        var teams = BuildTeams(league, playersPerTeam: 2);
        var players = teams.SelectMany(team => team.PlayerIds).Select(id => BuildPlayer(id)).ToList();

        var conclusion = new SeasonConclusion().Conclude(
            league,
            SeasonTestLeague.Names(league),
            teams,
            players,
            [],
            run,
            StandingsRules.None,
            PostseasonRules.None);

        Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));
        Assert.Null(conclusion.Value.Entry.ChampionTeamId);
        Assert.Equal(2, conclusion.Value.Entry.FinalStandings.Count);
        Assert.Equal(4, conclusion.Value.PlayersCreditedService);
        Assert.Empty(conclusion.Value.PlayersReleasedToFreeAgency);
        Assert.Single(league.History);
        Assert.All(players, player => Assert.Equal(1, player.SeasonsOfService));
    }

    [Fact]
    public void ConcludesAPostseasonAndRecordsTheChampionFromTheReDrawnBracket()
    {
        var league = SeasonTestLeague.Flat(4);
        var postseasonRules = PostseasonRules.Create(5, 2, [3], "2-2-1-1-1", null, 10).Value;
        var scheduleRules = ScheduleRules.Create(0, 10, 0).Value;
        var calendar = new SeasonCalendarBuilder().Build(Season, SeasonTestLeague.Opening, scheduleRules, postseasonRules).Value;
        var postseasonStart = calendar.Phase(SeasonPhase.Postseason)!.StartDay;

        // Team 0 and team 1 finish 2-0, team 2 and team 3 finish 0-2. The tie inside each pair falls
        // to the terminal key (team identifier), which is deterministic and puts team 0 and team 1
        // top of the table exactly as the postseason fixtures below assume.
        var regularSeasonFixtures = new[]
        {
            Fixture(SeasonDay.Opening, 0, 0, 2, SeasonPhase.RegularSeason),
            Fixture(SeasonDay.Opening.Plus(1), 0, 0, 3, SeasonPhase.RegularSeason),
            Fixture(SeasonDay.Opening.Plus(2), 0, 1, 2, SeasonPhase.RegularSeason),
            Fixture(SeasonDay.Opening.Plus(3), 0, 1, 3, SeasonPhase.RegularSeason),
        };

        var postseasonFixtures = new[]
        {
            Fixture(postseasonStart, 0, 0, 1, SeasonPhase.Postseason),
            Fixture(postseasonStart.Plus(1), 0, 0, 1, SeasonPhase.Postseason),
        };

        var run = BuildRun(
            calendar,
            [.. regularSeasonFixtures, .. postseasonFixtures],
            [
                (regularSeasonFixtures[0], 101, 90),
                (regularSeasonFixtures[1], 101, 90),
                (regularSeasonFixtures[2], 101, 90),
                (regularSeasonFixtures[3], 101, 90),
                (postseasonFixtures[0], 101, 99),
                (postseasonFixtures[1], 101, 99),
            ]);

        var teams = BuildTeams(league, playersPerTeam: 1);
        var players = teams.SelectMany(team => team.PlayerIds).Select(id => BuildPlayer(id)).ToList();

        var conclusion = new SeasonConclusion().Conclude(
            league,
            SeasonTestLeague.Names(league),
            teams,
            players,
            [],
            run,
            StandingsRules.None,
            postseasonRules);

        Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));
        Assert.Equal(SeasonTestLeague.TeamAt(0), conclusion.Value.Entry.ChampionTeamId);
        Assert.Empty(conclusion.Value.Notes);
    }

    [Fact]
    public void ExpiredContractsReleasePlayersToFreeAgencyWhileStillCreditingTheirService()
    {
        var league = SeasonTestLeague.Flat(1);
        var run = PlayedRegularSeason(league, []);
        var teamId = SeasonTestLeague.TeamAt(0);

        var expiringPlayerId = PlayerIdFor(teamId, 0);
        var retainedPlayerId = PlayerIdFor(teamId, 1);

        var team = Team.Create(
            teamId,
            new FranchiseId($"{teamId.Value}-FR"),
            "Club",
            new RosterSizeLimits(0, 15),
            [expiringPlayerId, retainedPlayerId]).Value;

        var players = new[] { BuildPlayer(expiringPlayerId), BuildPlayer(retainedPlayerId) };

        var expiringContract = Contract.Create(
            new ContractId("CONTRACT-EXPIRING"),
            teamId,
            expiringPlayerId,
            [new ContractSeasonTerm(Season, new Money(1_000_000), new Money(1_000_000))]).Value;

        var retainedContract = Contract.Create(
            new ContractId("CONTRACT-RETAINED"),
            teamId,
            retainedPlayerId,
            [
                new ContractSeasonTerm(Season, new Money(1_000_000), new Money(1_000_000)),
                new ContractSeasonTerm(new Season(Season.Year + 1), new Money(1_000_000), new Money(1_000_000)),
            ]).Value;

        var conclusion = new SeasonConclusion().Conclude(
            league,
            SeasonTestLeague.Names(league),
            [team],
            players,
            [expiringContract, retainedContract],
            run,
            StandingsRules.None,
            PostseasonRules.None);

        Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));
        Assert.Equal([expiringPlayerId], conclusion.Value.PlayersReleasedToFreeAgency);
        Assert.Equal(2, conclusion.Value.PlayersCreditedService);
        Assert.DoesNotContain(expiringPlayerId, team.PlayerIds);
        Assert.Contains(retainedPlayerId, team.PlayerIds);
        Assert.All(players, player => Assert.Equal(1, player.SeasonsOfService));
    }

    [Fact]
    public void RefusesToConcludeASeasonThatHasNotReachedItsLastDay()
    {
        var league = SeasonTestLeague.Flat(2);
        var calendar = SeasonTestLeague.Calendar(regularSeasonDays: 3);
        var runResult = SeasonRun.Start(Season, new SeasonSeed(1), calendar, SeasonSchedule.Empty);
        Assert.True(runResult.IsSuccess);

        var conclusion = new SeasonConclusion().Conclude(
            league,
            SeasonTestLeague.Names(league),
            [],
            [],
            [],
            runResult.Value,
            StandingsRules.None,
            PostseasonRules.None);

        Assert.True(conclusion.IsFailure);
        Assert.Equal("season.conclusion_of_incomplete_season", Assert.Single(conclusion.Errors).Code);
        Assert.Empty(league.History);
    }

    [Fact]
    public void RefusesToConcludeASeasonAlreadyConcludedAndMutatesNothingFurther()
    {
        var league = SeasonTestLeague.Flat(2);
        var run = PlayedRegularSeason(league, [(0, 1, 101, 90)]);
        var teams = BuildTeams(league, playersPerTeam: 2);
        var players = teams.SelectMany(team => team.PlayerIds).Select(id => BuildPlayer(id)).ToList();
        var conclusion = new SeasonConclusion();

        var first = conclusion.Conclude(
            league, SeasonTestLeague.Names(league), teams, players, [], run, StandingsRules.None, PostseasonRules.None);
        Assert.True(first.IsSuccess);

        var second = conclusion.Conclude(
            league, SeasonTestLeague.Names(league), teams, players, [], run, StandingsRules.None, PostseasonRules.None);

        Assert.True(second.IsFailure);
        Assert.Equal("season.already_concluded", Assert.Single(second.Errors).Code);
        Assert.Single(league.History);
        Assert.All(players, player => Assert.Equal(1, player.SeasonsOfService));
    }

    private static SeasonRun PlayedRegularSeason(
        League league,
        IReadOnlyList<(int Home, int Away, int HomePoints, int AwayPoints)> games)
    {
        var calendar = SeasonTestLeague.Calendar(regularSeasonDays: 3);

        var results = games
            .Select((game, index) => (
                Fixture: Fixture(SeasonDay.Opening, index, game.Home, game.Away, SeasonPhase.RegularSeason),
                game.HomePoints,
                game.AwayPoints))
            .ToList();

        return BuildRun(calendar, results.Select(result => result.Fixture).ToList(), results);
    }

    private static SeasonRun BuildRun(
        LeagueCalendar calendar,
        IReadOnlyList<Fixture> fixtures,
        IReadOnlyList<(Fixture Fixture, int HomePoints, int AwayPoints)> results)
    {
        var scheduleResult = SeasonSchedule.Create(fixtures);
        Assert.True(scheduleResult.IsSuccess);

        var runResult = SeasonRun.Start(Season, new SeasonSeed(1), calendar, scheduleResult.Value);
        Assert.True(runResult.IsSuccess);
        var run = runResult.Value;

        Assert.True(run.AdvanceTo(calendar.EndDayExclusive).IsSuccess);

        foreach (var (fixture, homePoints, awayPoints) in results)
        {
            var result = GameResult.Create(fixture, homePoints, awayPoints);
            Assert.True(result.IsSuccess);
            Assert.True(run.RecordResult(result.Value).IsSuccess);
        }

        return run;
    }

    private static Fixture Fixture(SeasonDay day, int slot, int homeIndex, int awayIndex, SeasonPhase phase) =>
        new(
            GameId.For(Season, day, slot),
            day,
            SeasonTestLeague.TeamAt(homeIndex),
            SeasonTestLeague.TeamAt(awayIndex),
            phase);

    private static IReadOnlyList<Team> BuildTeams(League league, int playersPerTeam)
    {
        var limits = new RosterSizeLimits(minimumPlayers: 0, maximumPlayers: 15);

        return league.TeamIds
            .Select(teamId =>
            {
                var playerIds = Enumerable.Range(0, playersPerTeam).Select(index => PlayerIdFor(teamId, index)).ToList();
                var team = Team.Create(teamId, new FranchiseId($"{teamId.Value}-FR"), $"Club {teamId.Value}", limits, playerIds);
                Assert.True(team.IsSuccess);
                return team.Value;
            })
            .ToList();
    }

    private static PlayerId PlayerIdFor(TeamId teamId, int index) => new($"{teamId.Value}-P{index:D2}");

    private static Player BuildPlayer(PlayerId id, int seasonsOfService = 0) =>
        Player.Create(
            id,
            $"Player {id.Value}",
            Position.PointGuard,
            new PlayerRating(60),
            new DateOnly(2000, 1, 1),
            seasonsOfService).Value;
}
