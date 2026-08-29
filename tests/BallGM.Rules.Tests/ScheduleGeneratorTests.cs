using BallGM.Domain.Seasons;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

public sealed class ScheduleGeneratorTests
{
    private readonly ScheduleGenerator _generator = new();

    [Fact]
    public void Schedule_IsIdenticalWhenGeneratedTwiceFromTheSameSeed()
    {
        var league = SeasonTestLeague.Flat(6);
        var calendar = SeasonTestLeague.Calendar(120);
        var rules = Rules(0, 120);

        var first = Generate(league, calendar, rules, 20, new SeasonSeed(4242));
        var second = Generate(league, calendar, rules, 20, new SeasonSeed(4242));

        Assert.Equal(
            first.Schedule.Fixtures.Select(Describe),
            second.Schedule.Fixtures.Select(Describe));
    }

    [Fact]
    public void Schedule_DiffersWhenGeneratedFromADifferentSeed()
    {
        var league = SeasonTestLeague.Flat(6);
        var calendar = SeasonTestLeague.Calendar(120);
        var rules = Rules(0, 120);

        var first = Generate(league, calendar, rules, 20, new SeasonSeed(1));
        var second = Generate(league, calendar, rules, 20, new SeasonSeed(2));

        Assert.NotEqual(
            first.Schedule.Fixtures.Select(Describe),
            second.Schedule.Fixtures.Select(Describe));
    }

    [Fact]
    public void Schedule_GivesEveryTeamTheConfiguredNumberOfGamesInAnEvenLeague()
    {
        var league = SeasonTestLeague.Flat(6);
        var generation = Generate(league, SeasonTestLeague.Calendar(120), Rules(0, 120), 20, new SeasonSeed(7));

        Assert.All(generation.GamesPerTeam.Values, count => Assert.Equal(20, count));
        Assert.DoesNotContain(generation.Warnings, warning => warning.RuleCode == "schedule.unbalanced_team_count");
    }

    [Fact]
    public void Schedule_NeverPutsATeamInTwoGamesOnOneDay()
    {
        var league = SeasonTestLeague.Flat(6);
        var generation = Generate(league, SeasonTestLeague.Calendar(120), Rules(0, 120), 20, new SeasonSeed(7));

        foreach (var day in generation.Schedule.Fixtures.GroupBy(fixture => fixture.Day.Index))
        {
            var teams = day.SelectMany(fixture => new[] { fixture.HomeTeamId.Value, fixture.AwayTeamId.Value }).ToArray();
            Assert.Equal(teams.Length, teams.Distinct().Count());
        }
    }

    [Fact]
    public void Schedule_WarnsThatAnOddTeamCountCannotBeBalanced()
    {
        // Twenty-two games across five teams: five whole rotations give everyone twenty, and the
        // remaining two rounds each sit one team out. That imbalance is a property of an odd league,
        // not of the generator, so it is reported with the figures rather than hidden.
        var league = SeasonTestLeague.Flat(5);
        var generation = Generate(league, SeasonTestLeague.Calendar(120), Rules(0, 120), 22, new SeasonSeed(7));

        Assert.Contains(generation.Warnings, warning => warning.RuleCode == "schedule.unbalanced_team_count");
        Assert.True(generation.GamesPerTeam.Values.Max() - generation.GamesPerTeam.Values.Min() >= 1);
    }

    [Fact]
    public void Schedule_BalancesAnOddLeagueWhereTheGameCountDividesEvenly()
    {
        var league = SeasonTestLeague.Flat(5);
        var generation = Generate(league, SeasonTestLeague.Calendar(120), Rules(0, 120), 20, new SeasonSeed(7));

        Assert.All(generation.GamesPerTeam.Values, count => Assert.Equal(20, count));
    }

    [Fact]
    public void Schedule_NotesThatALeagueStatingNoWeightingPlaysEveryOpponentEqually()
    {
        var league = SeasonTestLeague.TwoConferences(6);
        var generation = Generate(league, SeasonTestLeague.Calendar(120), Rules(0, 120), 20, new SeasonSeed(7));

        Assert.Contains(generation.Notes, note => note.RuleCode == "schedule.opponent_weighting_not_configured");
    }

    [Fact]
    public void Schedule_HonoursTheStatedOpponentWeighting()
    {
        var league = SeasonTestLeague.TwoConferences(6);
        var rules = ScheduleRules.Create(0, 200, 0, 6, 6, 4);
        Assert.True(rules.IsSuccess);

        var generation = Generate(league, SeasonTestLeague.Calendar(200), rules.Value, 24, new SeasonSeed(7));

        // Two division rivals at six games each, three cross-conference opponents at four.
        Assert.All(generation.GamesPerTeam.Values, count => Assert.Equal((2 * 6) + (3 * 4), count));
    }

    [Fact]
    public void Schedule_ReportsThatAWeightingCannotApplyInALeagueWithNoGroups()
    {
        var league = SeasonTestLeague.Flat(6);
        var rules = ScheduleRules.Create(0, 200, 0, 6, 6, 4);
        Assert.True(rules.IsSuccess);

        var generation = Generate(league, SeasonTestLeague.Calendar(200), rules.Value, 20, new SeasonSeed(7));

        Assert.Contains(generation.Notes, note => note.RuleCode == "schedule.opponent_weighting_without_groups");
    }

    [Fact]
    public void Schedule_WarnsWhenTheWeightingProducesADifferentGameCountFromTheOneStated()
    {
        var league = SeasonTestLeague.TwoConferences(6);
        var rules = ScheduleRules.Create(0, 200, 0, 6, 6, 4);
        Assert.True(rules.IsSuccess);

        var generation = Generate(league, SeasonTestLeague.Calendar(200), rules.Value, 82, new SeasonSeed(7));

        Assert.Contains(generation.Warnings, warning => warning.RuleCode == "schedule.weighting_disagrees_with_game_count");
    }

    [Fact]
    public void Schedule_RefusesToFitASeasonIntoFewerDaysThanItNeeds()
    {
        var league = SeasonTestLeague.Flat(6);
        var calendar = SeasonTestLeague.Calendar(3);

        var result = _generator.Generate(
            SeasonTestLeague.Season,
            league,
            calendar,
            Rules(0, 3),
            20,
            new SeasonSeed(7));

        Assert.True(result.IsFailure);
        Assert.Equal("schedule.not_enough_days", result.Errors[0].Code);
    }

    [Fact]
    public void Schedule_RefusesALeagueWithNobodyToPlay()
    {
        var league = SeasonTestLeague.Flat(1);

        var result = _generator.Generate(
            SeasonTestLeague.Season,
            league,
            SeasonTestLeague.Calendar(50),
            Rules(0, 50),
            10,
            new SeasonSeed(7));

        Assert.True(result.IsFailure);
        Assert.Equal("schedule.too_few_teams", result.Errors[0].Code);
    }

    [Fact]
    public void Schedule_SplitsEachPairsMeetingsBetweenTheTwoVenues()
    {
        var league = SeasonTestLeague.Flat(4);
        var generation = Generate(league, SeasonTestLeague.Calendar(120), Rules(0, 120), 12, new SeasonSeed(11));

        foreach (var pair in generation.Schedule.Fixtures
                     .GroupBy(fixture => string.CompareOrdinal(fixture.HomeTeamId.Value, fixture.AwayTeamId.Value) <= 0
                         ? (fixture.HomeTeamId.Value, fixture.AwayTeamId.Value)
                         : (fixture.AwayTeamId.Value, fixture.HomeTeamId.Value)))
        {
            var homeForFirst = pair.Count(fixture => fixture.HomeTeamId.Value == pair.Key.Item1);
            var homeForSecond = pair.Count() - homeForFirst;

            Assert.True(Math.Abs(homeForFirst - homeForSecond) <= 1);
        }
    }

    private static string Describe(Fixture fixture) =>
        $"{fixture.Id.Value}:{fixture.HomeTeamId.Value}v{fixture.AwayTeamId.Value}";

    private static ScheduleRules Rules(int preseasonDays, int regularSeasonDays)
    {
        var result = ScheduleRules.Create(preseasonDays, regularSeasonDays, 0);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private ScheduleGeneration Generate(
        Domain.Leagues.League league,
        LeagueCalendar calendar,
        ScheduleRules rules,
        int gameCount,
        SeasonSeed seed)
    {
        var result = _generator.Generate(SeasonTestLeague.Season, league, calendar, rules, gameCount, seed);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
