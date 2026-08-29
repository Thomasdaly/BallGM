using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

/// <summary>
/// The bracket as a rule: who qualifies, who plays whom, where, and on which day. Nothing here
/// touches a season being advanced — that sequencing is <c>SeasonEngine</c>'s and is tested against
/// the real engine in the simulation suite.
/// </summary>
public sealed class PostseasonBracketBuilderTests
{
    private static readonly Season Season = SeasonTestLeague.Season;

    [Fact]
    public void SeedsTheTopTeamsOfEachConferenceInTableOrder()
    {
        var league = SeasonTestLeague.TwoConferences(8);
        var standings = Table(league, [0, 4, 1, 5, 2, 6, 3, 7]);

        var seeding = new PostseasonBracketBuilder().Seed(league, standings, Rules(qualifiers: 2, lengths: [3, 3]));

        Assert.True(seeding.IsSuccess);

        // Coastal is TEAM-00..03 and Interior TEAM-04..07, so the table above puts TEAM-00 and
        // TEAM-01 top of one conference and TEAM-04 and TEAM-05 top of the other.
        Assert.Equal(
            [SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(1)],
            seeding.Value.InGroup("Coastal").Select(seed => seed.TeamId));

        Assert.Equal(
            [SeasonTestLeague.TeamAt(4), SeasonTestLeague.TeamAt(5)],
            seeding.Value.InGroup("Interior").Select(seed => seed.TeamId));

        Assert.Equal([1, 2], seeding.Value.InGroup("Coastal").Select(seed => seed.Seed));

        // The league-wide position, not the conference seed: Interior's number one sat second in the
        // table, and that is what decides home advantage in a final between two number ones.
        Assert.Equal(1, seeding.Value.InGroup("Coastal")[0].LeaguePosition);
        Assert.Equal(2, seeding.Value.InGroup("Interior")[0].LeaguePosition);
    }

    [Fact]
    public void SeedsAFlatLeagueFromTheLeagueWideTable()
    {
        var league = SeasonTestLeague.Flat(8);
        var standings = Table(league, [7, 6, 5, 4, 3, 2, 1, 0]);

        var seeding = new PostseasonBracketBuilder().Seed(league, standings, Rules(qualifiers: 4, lengths: [3, 3]));

        Assert.True(seeding.IsSuccess);
        Assert.Equal([null], seeding.Value.Groups);

        Assert.Equal(
            [SeasonTestLeague.TeamAt(7), SeasonTestLeague.TeamAt(6), SeasonTestLeague.TeamAt(5), SeasonTestLeague.TeamAt(4)],
            seeding.Value.Seeds.Select(seed => seed.TeamId));
    }

    [Fact]
    public void RefusesToSeedAConferenceWithFewerTeamsThanItQualifies()
    {
        var league = SeasonTestLeague.TwoConferences(4);
        var standings = Table(league, [0, 1, 2, 3]);

        var seeding = new PostseasonBracketBuilder().Seed(league, standings, Rules(qualifiers: 4, lengths: [3, 3, 3]));

        Assert.True(seeding.IsFailure);
        Assert.Equal(PostseasonBracketBuilder.TooFewTeamsCode, Assert.Single(seeding.Errors).Code);
    }

    [Fact]
    public void RefusesToSeedALeagueWithNoPostseason()
    {
        var league = SeasonTestLeague.Flat(4);

        var seeding = new PostseasonBracketBuilder().Seed(league, Table(league, [0, 1, 2, 3]), PostseasonRules.None);

        Assert.True(seeding.IsFailure);
        Assert.Equal(PostseasonBracketBuilder.NotConfiguredCode, Assert.Single(seeding.Errors).Code);
    }

    [Fact]
    public void WarnsWhenTheLastPostseasonPlaceWasTakenOnAnEqualRecord()
    {
        var league = SeasonTestLeague.Flat(4);

        // Fourth and fifth do not exist in a four-team league, so the boundary is second and third:
        // both 5-5, and only the table's order separates them.
        var rows = new[]
        {
            Row(league, 0, new TeamRecord(9, 1)),
            Row(league, 1, new TeamRecord(5, 5)),
            Row(league, 2, new TeamRecord(5, 5)),
            Row(league, 3, new TeamRecord(1, 9)),
        };

        var seeding = new PostseasonBracketBuilder().Seed(
            league,
            new Standings(rows, []),
            Rules(qualifiers: 2, lengths: [3]));

        Assert.True(seeding.IsSuccess);
        Assert.Contains(
            seeding.Value.Warnings,
            warning => warning.RuleCode == PostseasonBracketBuilder.LastPlaceOnEqualRecordsCode);
    }

    [Fact]
    public void DrawsTheFirstRoundAsTopSeedAgainstBottomSeedOnTheFirstPostseasonDay()
    {
        var league = SeasonTestLeague.Flat(8);
        var rules = Rules(qualifiers: 4, lengths: [3, 5], postseasonDays: 10);
        var calendar = Calendar(rules);
        var seeding = Seed(league, [0, 1, 2, 3, 4, 5, 6, 7], rules);

        var draw = new PostseasonBracketBuilder().DrawFor(
            Season,
            seeding,
            rules,
            calendar,
            SeasonSchedule.Empty,
            [],
            calendar.Phase(SeasonPhase.Postseason)!.StartDay);

        Assert.True(draw.IsSuccess);
        Assert.Empty(draw.Value.Violations);
        Assert.Equal(1, draw.Value.LiveRound);

        // Bracket order for four qualifiers is 1-4, 2-3, so the top seed cannot meet the second seed
        // before the last round. The higher seed hosts game 1 under a 2-2-1-1-1 sequence.
        Assert.Equal(2, draw.Value.Fixtures.Count);
        Assert.Equal(SeasonTestLeague.TeamAt(0), draw.Value.Fixtures[0].HomeTeamId);
        Assert.Equal(SeasonTestLeague.TeamAt(3), draw.Value.Fixtures[0].AwayTeamId);
        Assert.Equal(SeasonTestLeague.TeamAt(1), draw.Value.Fixtures[1].HomeTeamId);
        Assert.Equal(SeasonTestLeague.TeamAt(2), draw.Value.Fixtures[1].AwayTeamId);
        Assert.All(draw.Value.Fixtures, fixture => Assert.Equal(SeasonPhase.Postseason, fixture.Phase));
    }

    [Fact]
    public void GivesTheLowerSeedHomeAdvantageWhereTheStatedSequenceDoes()
    {
        var league = SeasonTestLeague.Flat(8);

        // Best-of-five, so that two wins leaves both series still alive and there is a third game
        // for the sequence to place.
        var rules = Rules(qualifiers: 4, lengths: [5, 5], postseasonDays: 12);
        var calendar = Calendar(rules);
        var seeding = Seed(league, [0, 1, 2, 3, 4, 5, 6, 7], rules);
        var builder = new PostseasonBracketBuilder();
        var start = calendar.Phase(SeasonPhase.Postseason)!.StartDay;

        // Two games in, a 2-2-1-1-1 sequence hands the third to the lower seed.
        var schedule = Schedule(
            Game(start, 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(3)),
            Game(start, 1, SeasonTestLeague.TeamAt(1), SeasonTestLeague.TeamAt(2)),
            Game(start.Plus(1), 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(3)),
            Game(start.Plus(1), 1, SeasonTestLeague.TeamAt(1), SeasonTestLeague.TeamAt(2)));

        var results = new[]
        {
            Won(schedule, start, 0, home: false),
            Won(schedule, start, 1, home: false),
            Won(schedule, start.Plus(1), 0, home: false),
            Won(schedule, start.Plus(1), 1, home: false),
        };

        var draw = builder.DrawFor(Season, seeding, rules, calendar, schedule, results, start.Plus(2));

        Assert.True(draw.IsSuccess);
        Assert.Equal(2, draw.Value.Fixtures.Count);
        Assert.Equal(SeasonTestLeague.TeamAt(3), draw.Value.Fixtures[0].HomeTeamId);
        Assert.Equal(SeasonTestLeague.TeamAt(0), draw.Value.Fixtures[0].AwayTeamId);
    }

    [Fact]
    public void DrawsNothingOnADayOutsideThePostseason()
    {
        var league = SeasonTestLeague.Flat(8);
        var rules = Rules(qualifiers: 4, lengths: [3, 5], postseasonDays: 10);
        var calendar = Calendar(rules);

        var draw = new PostseasonBracketBuilder().DrawFor(
            Season,
            Seed(league, [0, 1, 2, 3, 4, 5, 6, 7], rules),
            rules,
            calendar,
            SeasonSchedule.Empty,
            [],
            SeasonDay.Opening);

        Assert.True(draw.IsSuccess);
        Assert.Empty(draw.Value.Fixtures);
        Assert.Empty(draw.Value.Violations);
    }

    [Fact]
    public void ReportsAPostseasonThatNeedsMoreDaysThanTheCalendarReserves()
    {
        var league = SeasonTestLeague.Flat(8);
        var rules = Rules(qualifiers: 4, lengths: [7, 7], postseasonDays: 3);
        var calendar = Calendar(rules);
        var seeding = Seed(league, [0, 1, 2, 3, 4, 5, 6, 7], rules);
        var start = calendar.Phase(SeasonPhase.Postseason)!.StartDay;

        var schedule = Schedule(
            Game(start, 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(3)),
            Game(start.Plus(1), 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(3)),
            Game(start.Plus(2), 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(3)));

        var draw = new PostseasonBracketBuilder().DrawFor(
            Season, seeding, rules, calendar, schedule, [], start.Plus(2));

        Assert.True(draw.IsSuccess);
        Assert.Contains(
            draw.Value.Violations,
            violation => violation.RuleCode == PostseasonBracketBuilder.RunsPastItsDaysCode);
    }

    [Fact]
    public void ReportsARulesetStatingADifferentNumberOfSeriesLengthsThanTheLeagueHasRounds()
    {
        var league = SeasonTestLeague.Flat(8);

        // Four qualifiers in a flat league is two rounds. Three stated lengths describes a round
        // nobody can play.
        var rules = Rules(qualifiers: 4, lengths: [3, 5, 7], postseasonDays: 20);
        var calendar = Calendar(rules);

        var draw = new PostseasonBracketBuilder().DrawFor(
            Season,
            Seed(league, [0, 1, 2, 3, 4, 5, 6, 7], rules),
            rules,
            calendar,
            SeasonSchedule.Empty,
            [],
            calendar.Phase(SeasonPhase.Postseason)!.StartDay);

        Assert.True(draw.IsSuccess);
        Assert.Equal(
            PostseasonBracketBuilder.RoundCountMismatchCode,
            Assert.Single(draw.Value.Violations).RuleCode);
    }

    [Fact]
    public void ReportsTheChampionOnceEverySeriesIsDecided()
    {
        var league = SeasonTestLeague.Flat(4);
        var rules = Rules(qualifiers: 2, lengths: [3], postseasonDays: 5);
        var calendar = Calendar(rules);
        var seeding = Seed(league, [0, 1, 2, 3], rules);
        var start = calendar.Phase(SeasonPhase.Postseason)!.StartDay;

        var schedule = Schedule(
            Game(start, 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(1)),
            Game(start.Plus(1), 0, SeasonTestLeague.TeamAt(0), SeasonTestLeague.TeamAt(1)));

        var results = new[]
        {
            Won(schedule, start, 0, home: true),
            Won(schedule, start.Plus(1), 0, home: true),
        };

        var draw = new PostseasonBracketBuilder().DrawFor(
            Season, seeding, rules, calendar, schedule, results, start.Plus(2));

        Assert.True(draw.IsSuccess);
        Assert.True(draw.Value.IsComplete);
        Assert.Equal(SeasonTestLeague.TeamAt(0), draw.Value.ChampionId);
        Assert.Empty(draw.Value.Fixtures);
    }

    private static PostseasonRules Rules(int qualifiers, IReadOnlyList<int> lengths, int postseasonDays = 10)
    {
        var created = PostseasonRules.Create(postseasonDays, qualifiers, lengths, "2-2-1-1-1", null, 10);
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static LeagueCalendar Calendar(PostseasonRules rules)
    {
        var scheduleRules = ScheduleRules.Create(0, 10, 0);
        Assert.True(scheduleRules.IsSuccess);

        var calendar = new SeasonCalendarBuilder().Build(Season, SeasonTestLeague.Opening, scheduleRules.Value, rules);
        Assert.True(calendar.IsSuccess);
        return calendar.Value;
    }

    private static PostseasonSeeding Seed(League league, IReadOnlyList<int> order, PostseasonRules rules)
    {
        var seeding = new PostseasonBracketBuilder().Seed(league, Table(league, order), rules);
        Assert.True(seeding.IsSuccess);
        return seeding.Value;
    }

    /// <summary>A table already in the stated order, best first, on descending win totals.</summary>
    private static Standings Table(League league, IReadOnlyList<int> order)
    {
        var rows = order
            .Select((teamIndex, position) => Row(league, teamIndex, new TeamRecord(order.Count - position, position)))
            .ToArray();

        return new Standings(rows, []);
    }

    private static StandingsRow Row(League league, int teamIndex, TeamRecord record)
    {
        var teamId = SeasonTestLeague.TeamAt(teamIndex);

        return new StandingsRow(
            teamId,
            $"Club {teamId.Value}",
            league.Alignment.ConferenceOf(teamId),
            league.Alignment.DivisionOf(teamId),
            record,
            null,
            null,
            record.Wins * 100,
            record.Losses * 100);
    }

    private static Fixture Game(SeasonDay day, int slot, TeamId home, TeamId away) =>
        new(GameId.For(Season, day, slot), day, home, away, SeasonPhase.Postseason);

    private static SeasonSchedule Schedule(params Fixture[] fixtures)
    {
        var schedule = SeasonSchedule.Create(fixtures);
        Assert.True(schedule.IsSuccess);
        return schedule.Value;
    }

    private static GameResult Won(SeasonSchedule schedule, SeasonDay day, int slot, bool home)
    {
        var fixture = schedule.Game(GameId.For(Season, day, slot))!;
        var result = GameResult.Create(fixture, home ? 101 : 99, home ? 99 : 101);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
