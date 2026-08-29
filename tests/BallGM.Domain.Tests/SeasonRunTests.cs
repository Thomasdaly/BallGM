using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class SeasonRunTests
{
    private static readonly Season Season = new(2031);
    private static readonly DateOnly Opening = new(2031, 7, 1);
    private static readonly TeamId Home = new("TEAM-HOME");
    private static readonly TeamId Away = new("TEAM-AWAY");

    [Fact]
    public void Season_OpensOnDayZeroWithNothingPlayed()
    {
        var run = BuildRun();

        Assert.Equal(SeasonDay.Opening, run.CurrentDay);
        Assert.Empty(run.Results);
        Assert.False(run.IsComplete);
    }

    [Fact]
    public void Season_RefusesToGoBackToADayItHasAlreadyPassed()
    {
        var run = BuildRun();
        Assert.True(run.AdvanceTo(new SeasonDay(5)).IsSuccess);

        var backwards = run.AdvanceTo(new SeasonDay(2));

        Assert.True(backwards.IsFailure);
        Assert.Equal("season.day_already_passed", backwards.Errors[0].Code);
        Assert.Equal(5, run.CurrentDay.Index);
    }

    [Fact]
    public void Season_RefusesToAdvancePastTheEndOfItsCalendar()
    {
        var run = BuildRun();

        var result = run.AdvanceTo(new SeasonDay(run.Calendar.LengthInDays + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("season.day_beyond_calendar", result.Errors[0].Code);
    }

    [Fact]
    public void Season_RefusesAResultForAGameItHasNotYetReached()
    {
        var run = BuildRun();
        var fixture = run.Schedule.Fixtures.Single(candidate => candidate.Day.Index == 3);

        var recorded = run.RecordResult(Result(fixture, 101, 99));

        Assert.True(recorded.IsFailure);
        Assert.Equal("season.game_not_yet_reached", recorded.Errors[0].Code);
    }

    [Fact]
    public void Season_RefusesTheSameGameTwiceSoATableCannotCountItTwice()
    {
        var run = BuildRun();
        Assert.True(run.AdvanceTo(new SeasonDay(4)).IsSuccess);

        var fixture = run.Schedule.Fixtures.First();
        Assert.True(run.RecordResult(Result(fixture, 101, 99)).IsSuccess);

        var again = run.RecordResult(Result(fixture, 88, 87));

        Assert.True(again.IsFailure);
        Assert.Equal("season.game_already_played", again.Errors[0].Code);
    }

    [Fact]
    public void Season_RefusesAResultForAGameItDoesNotHave()
    {
        var run = BuildRun();
        var stranger = new Fixture(new GameId("9999-0000-000"), SeasonDay.Opening, Home, Away, SeasonPhase.RegularSeason);

        var recorded = run.RecordResult(Result(stranger, 90, 80));

        Assert.True(recorded.IsFailure);
        Assert.Equal("season.result_for_unscheduled_game", recorded.Errors[0].Code);
    }

    [Fact]
    public void Season_RestoresEverythingThatChangedWhenAnAdvanceIsUnwound()
    {
        var run = BuildRun();
        var restorePoint = run.Capture();

        Assert.True(run.AdvanceTo(new SeasonDay(4)).IsSuccess);
        Assert.True(run.RecordResult(Result(run.Schedule.Fixtures.First(), 110, 100)).IsSuccess);

        run.RestoreTo(restorePoint);

        Assert.Equal(SeasonDay.Opening, run.CurrentDay);
        Assert.Empty(run.Results);
    }

    [Fact]
    public void Season_LoadedFromASaveReplaysItsResultsThroughTheSameRules()
    {
        var run = BuildRun();
        Assert.True(run.AdvanceTo(new SeasonDay(4)).IsSuccess);
        var played = Result(run.Schedule.Fixtures.First(), 105, 96);
        Assert.True(run.RecordResult(played).IsSuccess);

        var reloaded = SeasonRun.Rehydrate(
            Season,
            run.Seed,
            run.Calendar,
            run.Schedule,
            run.CurrentDay,
            [played],
            []);

        Assert.True(reloaded.IsSuccess);
        Assert.Equal(4, reloaded.Value.CurrentDay.Index);
        Assert.Single(reloaded.Value.Results);
    }

    [Fact]
    public void Season_LoadedFromASaveRefusesAHistoryThatCouldNotHaveHappened()
    {
        var run = BuildRun();
        var futureFixture = run.Schedule.Fixtures.Single(fixture => fixture.Day.Index == 3);

        // The file says the league has only reached day 1, but records a game played on day 3.
        var reloaded = SeasonRun.Rehydrate(
            Season,
            run.Seed,
            run.Calendar,
            run.Schedule,
            new SeasonDay(1),
            [Result(futureFixture, 100, 90)],
            []);

        Assert.True(reloaded.IsFailure);
        Assert.Equal("season.game_not_yet_reached", reloaded.Errors[0].Code);
    }

    [Fact]
    public void Season_ReportsWhoIsUnavailableOnADayFromTheInjurySpellsItHolds()
    {
        var run = BuildRun();
        var player = new PlayerId("PLAYER-1");

        Assert.True(run.RecordInjury(new InjurySpell(player, "Sprained ankle", new SeasonDay(2), new SeasonDay(6))).IsSuccess);

        Assert.Empty(run.UnavailableOn(new SeasonDay(1)));
        Assert.Contains(player, run.UnavailableOn(new SeasonDay(4)));
        Assert.Empty(run.UnavailableOn(new SeasonDay(6)));
    }

    [Fact]
    public void Season_RefusesToStartWithFixturesOnDaysItsCalendarDoesNotCover()
    {
        var calendar = Calendar();
        var strayFixture = new Fixture(
            new GameId("2031-0099-000"),
            new SeasonDay(99),
            Home,
            Away,
            SeasonPhase.RegularSeason);

        var schedule = SeasonSchedule.Create([strayFixture]);
        Assert.True(schedule.IsSuccess);

        var result = SeasonRun.Start(Season, new SeasonSeed(1), calendar, schedule.Value);

        Assert.True(result.IsFailure);
        Assert.Equal("season.day_beyond_calendar", result.Errors[0].Code);
    }

    private static GameResult Result(Fixture fixture, int homePoints, int awayPoints)
    {
        var result = GameResult.Create(fixture, homePoints, awayPoints);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static LeagueCalendar Calendar()
    {
        var calendar = LeagueCalendar.Create(
            Season,
            Opening,
            [new CalendarPhase(SeasonPhase.RegularSeason, SeasonDay.Opening, new SeasonDay(20))]);

        Assert.True(calendar.IsSuccess);
        return calendar.Value;
    }

    private static SeasonRun BuildRun()
    {
        var fixtures = new[]
        {
            new Fixture(GameId.For(Season, SeasonDay.Opening, 0), SeasonDay.Opening, Home, Away, SeasonPhase.RegularSeason),
            new Fixture(GameId.For(Season, new SeasonDay(3), 0), new SeasonDay(3), Away, Home, SeasonPhase.RegularSeason),
        };

        var schedule = SeasonSchedule.Create(fixtures);
        Assert.True(schedule.IsSuccess);

        var run = SeasonRun.Start(Season, new SeasonSeed(4242), Calendar(), schedule.Value);
        Assert.True(run.IsSuccess);
        return run.Value;
    }
}

public sealed class SeasonScheduleTests
{
    private static readonly Season Season = new(2031);

    [Fact]
    public void Schedule_RefusesATeamPlayingTwiceOnOneDay()
    {
        var first = new TeamId("TEAM-1");
        var second = new TeamId("TEAM-2");
        var third = new TeamId("TEAM-3");

        var result = SeasonSchedule.Create(
        [
            new Fixture(GameId.For(Season, SeasonDay.Opening, 0), SeasonDay.Opening, first, second, SeasonPhase.RegularSeason),
            new Fixture(GameId.For(Season, SeasonDay.Opening, 1), SeasonDay.Opening, first, third, SeasonPhase.RegularSeason),
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal("schedule.team_plays_twice_on_one_day", result.Errors[0].Code);
    }

    [Fact]
    public void Schedule_OrdersFixturesByDayThenIdentifierSoPlayOrderIsFixed()
    {
        var first = new TeamId("TEAM-1");
        var second = new TeamId("TEAM-2");
        var third = new TeamId("TEAM-3");
        var fourth = new TeamId("TEAM-4");

        var result = SeasonSchedule.Create(
        [
            new Fixture(GameId.For(Season, new SeasonDay(5), 1), new SeasonDay(5), third, fourth, SeasonPhase.RegularSeason),
            new Fixture(GameId.For(Season, new SeasonDay(2), 0), new SeasonDay(2), first, second, SeasonPhase.RegularSeason),
            new Fixture(GameId.For(Season, new SeasonDay(5), 0), new SeasonDay(5), first, second, SeasonPhase.RegularSeason),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["2031-0002-000", "2031-0005-000", "2031-0005-001"],
            result.Value.Fixtures.Select(fixture => fixture.Id.Value));
    }
}

public sealed class GameResultTests
{
    private static readonly Season Season = new(2031);

    [Fact]
    public void GameResult_RefusesADrawBecauseEveryTieBreakAssumesAWinnerExists()
    {
        var fixture = new Fixture(
            GameId.For(Season, SeasonDay.Opening, 0),
            SeasonDay.Opening,
            new TeamId("TEAM-1"),
            new TeamId("TEAM-2"),
            SeasonPhase.RegularSeason);

        var result = GameResult.Create(fixture, 100, 100);

        Assert.True(result.IsFailure);
        Assert.Equal("game_result.drawn_game", result.Errors[0].Code);
    }

    [Fact]
    public void BoxScore_RefusesPlayerLinesThatDoNotAddUpToTheFinalScore()
    {
        var home = new TeamId("TEAM-1");
        var away = new TeamId("TEAM-2");
        var gameId = GameId.For(Season, SeasonDay.Opening, 0);

        var result = BoxScore.Create(
            gameId,
            home,
            away,
            100,
            98,
            [
                new PlayerStatLine(new PlayerId("P1"), home, 30, 40, 5, 3, true),
                new PlayerStatLine(new PlayerId("P2"), away, 30, 98, 4, 2, true),
            ]);

        Assert.True(result.IsFailure);
        Assert.Equal("box_score.points_do_not_match_result", result.Errors[0].Code);
    }
}

public sealed class TeamRecordTests
{
    [Fact]
    public void Record_ComparesByRatioWithoutDividing()
    {
        var better = new TeamRecord(3, 1);
        var worse = new TeamRecord(5, 3);

        Assert.True(better.CompareTo(worse) > 0);
        Assert.True(worse.CompareTo(better) < 0);
    }

    [Fact]
    public void Record_TreatsEqualRatiosOnDifferentGameCountsAsLevel()
    {
        Assert.Equal(0, new TeamRecord(2, 2).CompareTo(new TeamRecord(10, 10)));
    }

    [Fact]
    public void Record_WithNoGamesPlayedSitsBelowARecordWithAWin()
    {
        Assert.True(TeamRecord.None.CompareTo(new TeamRecord(1, 0)) < 0);
        Assert.True(TeamRecord.None.CompareTo(new TeamRecord(0, 1)) > 0);
    }
}

public sealed class TieBreakSequenceTests
{
    [Fact]
    public void Sequence_RefusesTheSameTieBreakTwice()
    {
        var result = TieBreakSequence.Create(
            [StandingsTieBreak.HeadToHeadRecord, StandingsTieBreak.HeadToHeadRecord]);

        Assert.True(result.IsFailure);
        Assert.Equal("standings.duplicate_tie_break", result.Errors[0].Code);
    }

    [Fact]
    public void Sequence_TreatsAnAbsentListAsALeagueThatStatesNoTieBreak()
    {
        var result = TieBreakSequence.Create(null);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEmpty);
    }
}

public sealed class HomeCourtPatternTests
{
    [Fact]
    public void Pattern_GivesTheHigherSeedTheFirstBlock()
    {
        var pattern = HomeCourtPattern.Parse("2-2-1-1-1").Value;

        Assert.True(pattern.HigherSeedHosts(1));
        Assert.True(pattern.HigherSeedHosts(2));
        Assert.False(pattern.HigherSeedHosts(3));
        Assert.False(pattern.HigherSeedHosts(4));
        Assert.True(pattern.HigherSeedHosts(5));
        Assert.False(pattern.HigherSeedHosts(6));
        Assert.True(pattern.HigherSeedHosts(7));
    }

    [Fact]
    public void Pattern_HandlesADifferentLeaguesSequenceWithoutACodeChange()
    {
        var pattern = HomeCourtPattern.Parse("2-3-2").Value;

        Assert.True(pattern.HigherSeedHosts(2));
        Assert.False(pattern.HigherSeedHosts(3));
        Assert.False(pattern.HigherSeedHosts(5));
        Assert.True(pattern.HigherSeedHosts(6));
    }

    [Fact]
    public void Pattern_RefusesABlockOfNoGames()
    {
        var result = HomeCourtPattern.Parse("0-2-2");

        Assert.True(result.IsFailure);
        Assert.Equal("postseason.non_positive_home_court_block", result.Errors[0].Code);
    }

    [Fact]
    public void Pattern_RefusesSomethingThatIsNotASequenceOfGames()
    {
        Assert.True(HomeCourtPattern.Parse("two-two-one").IsFailure);
        Assert.True(HomeCourtPattern.Parse("   ").IsFailure);
    }
}

public sealed class LeagueAlignmentTests
{
    [Fact]
    public void Alignment_RefusesATeamPlacedInTwoDivisions()
    {
        var shared = new TeamId("TEAM-1");

        var result = LeagueAlignment.Create(
        [
            new LeagueConference("East", [new LeagueDivision("Atlantic", [shared])]),
            new LeagueConference("West", [new LeagueDivision("Pacific", [shared])]),
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "alignment.team_in_more_than_one_division");
    }

    [Fact]
    public void Alignment_AnswersWhichGroupTwoTeamsShare()
    {
        var first = new TeamId("TEAM-1");
        var second = new TeamId("TEAM-2");
        var third = new TeamId("TEAM-3");

        var alignment = LeagueAlignment.Create(
        [
            new LeagueConference("East", [
                new LeagueDivision("Atlantic", [first, second]),
                new LeagueDivision("Central", [third]),
            ]),
        ]).Value;

        Assert.True(alignment.AreInSameDivision(first, second));
        Assert.False(alignment.AreInSameDivision(first, third));
        Assert.True(alignment.AreInSameConference(first, third));
    }

    [Fact]
    public void FlatLeague_SharesNoGroupWithAnybody()
    {
        Assert.True(LeagueAlignment.Flat.IsFlat);
        Assert.False(LeagueAlignment.Flat.AreInSameConference(new TeamId("A"), new TeamId("B")));
        Assert.Null(LeagueAlignment.Flat.DivisionOf(new TeamId("A")));
    }

    [Fact]
    public void League_RefusesAnAlignmentNamingATeamItDoesNotHave()
    {
        var member = new TeamId("TEAM-1");
        var stranger = new TeamId("TEAM-STRANGER");

        var alignment = LeagueAlignment.Create(
            [new LeagueConference("East", [new LeagueDivision("Atlantic", [stranger])])]).Value;

        var result = League.Create(new LeagueId("LEAGUE-1"), "Test League", [member], alignment);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "league.alignment_team_not_in_league");
    }

    [Fact]
    public void League_WithoutAnAlignmentIsFlatRatherThanBroken()
    {
        var result = League.Create(new LeagueId("LEAGUE-1"), "Test League", [new TeamId("TEAM-1")]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Alignment.IsFlat);
    }
}

public sealed class SeasonSeedTests
{
    [Fact]
    public void Seed_DerivesTheSameGameSeedEveryTime()
    {
        var seed = new SeasonSeed(20260701);
        var gameId = new GameId("2031-0004-002");

        Assert.Equal(seed.ForGame(gameId), new SeasonSeed(20260701).ForGame(gameId));
    }

    [Fact]
    public void Seed_DerivesDifferentSeedsForDifferentGames()
    {
        var seed = new SeasonSeed(20260701);

        Assert.NotEqual(seed.ForGame(new GameId("2031-0004-000")), seed.ForGame(new GameId("2031-0004-001")));
    }

    [Fact]
    public void Seed_DerivesDifferentSeedsForTheSameGameInDifferentSeasons()
    {
        var gameId = new GameId("2031-0004-000");

        Assert.NotEqual(new SeasonSeed(1).ForGame(gameId), new SeasonSeed(2).ForGame(gameId));
    }

    [Fact]
    public void Seed_KeepsTheScheduleDrawApartFromEveryGame()
    {
        var seed = new SeasonSeed(20260701);

        Assert.NotEqual(seed.ForSchedule(), seed.ForGame(new GameId("2031-0000-000")));
    }
}
