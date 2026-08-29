using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

public sealed class StandingsCalculatorTests
{
    private readonly StandingsCalculator _calculator = new();

    [Fact]
    public void Standings_OrderByRecordBeforeAnyTieBreakIsConsidered()
    {
        var league = SeasonTestLeague.Flat(4);
        var results = new List<GameResult>
        {
            Result(0, 0, 0, 1, 110, 100),
            Result(1, 1, 0, 2, 110, 100),
            Result(2, 2, 1, 3, 110, 100),
        };

        var standings = _calculator.Calculate(league, SeasonTestLeague.Names(league), results, Sequence(StandingsTieBreak.PointDifferential));

        Assert.Equal(SeasonTestLeague.TeamAt(0), standings.Rows[0].TeamId);
        Assert.Equal(2, standings.Rows[0].Overall.Wins);
    }

    [Fact]
    public void Standings_ApplyTheStatedTieBreaksInTheStatedOrder()
    {
        var league = SeasonTestLeague.Flat(4);

        // Teams 0 and 1 both finish 1-1. Team 1 won the meeting between them by a point; team 0 has
        // by far the better differential. Head-to-head first puts team 1 above, differential first
        // reverses it — which is the whole reason the order is the league's to state.
        var results = new List<GameResult>
        {
            Result(0, 0, 1, 0, 101, 100),
            Result(0, 1, 0, 2, 140, 90),
            Result(0, 2, 3, 1, 140, 90),
        };

        var byHeadToHead = _calculator.Calculate(
            league,
            SeasonTestLeague.Names(league),
            results,
            Sequence(StandingsTieBreak.HeadToHeadRecord, StandingsTieBreak.PointDifferential));

        var byDifferential = _calculator.Calculate(
            league,
            SeasonTestLeague.Names(league),
            results,
            Sequence(StandingsTieBreak.PointDifferential, StandingsTieBreak.HeadToHeadRecord));

        Assert.True(
            byHeadToHead.PositionOf(SeasonTestLeague.TeamAt(1)) < byHeadToHead.PositionOf(SeasonTestLeague.TeamAt(0)));

        Assert.True(
            byDifferential.PositionOf(SeasonTestLeague.TeamAt(0)) < byDifferential.PositionOf(SeasonTestLeague.TeamAt(1)));
    }

    [Fact]
    public void Standings_ReportEveryTieTheStatedSequenceDidNotResolve()
    {
        var league = SeasonTestLeague.Flat(2);

        // One win each, identical points, and they have not met. Nothing separates them.
        var results = new List<GameResult>();

        var standings = _calculator.Calculate(league, SeasonTestLeague.Names(league), results, StandingsRules.None);

        Assert.Contains(standings.Notes, note => note.RuleCode == "standings.tie_unresolved_by_ruleset");
    }

    [Fact]
    public void Standings_ReportThatALeagueStatesNoTieBreakAtAll()
    {
        var league = SeasonTestLeague.Flat(2);

        var standings = _calculator.Calculate(league, SeasonTestLeague.Names(league), [], StandingsRules.None);

        Assert.Contains(standings.Notes, note => note.RuleCode == "standings.no_tie_break_sequence_configured");
    }

    [Fact]
    public void Standings_ReportATieBreakThisLeagueHasNoGroupsToApply()
    {
        var league = SeasonTestLeague.Flat(4);

        var standings = _calculator.Calculate(
            league,
            SeasonTestLeague.Names(league),
            [],
            Sequence(StandingsTieBreak.DivisionRecord, StandingsTieBreak.ConferenceRecord));

        Assert.Contains(standings.Notes, note => note.RuleCode == "standings.tie_break_needs_divisions");
        Assert.Contains(standings.Notes, note => note.RuleCode == "standings.tie_break_needs_conferences");
    }

    [Fact]
    public void Standings_LeaveDivisionAndConferenceRecordsAbsentInALeagueWithNoGroups()
    {
        var league = SeasonTestLeague.Flat(2);

        var standings = _calculator.Calculate(league, SeasonTestLeague.Names(league), [], StandingsRules.None);

        Assert.All(standings.Rows, row => Assert.Null(row.DivisionRecord));
        Assert.All(standings.Rows, row => Assert.Null(row.ConferenceRecord));
    }

    [Fact]
    public void Standings_CountAGroupRecordOnlyAgainstOpponentsInThatGroup()
    {
        var league = SeasonTestLeague.TwoConferences(4);

        var results = new List<GameResult>
        {
            // Team 0 beats its division rival, then loses across the conference divide.
            Result(0, 0, 0, 1, 110, 100),
            Result(1, 1, 2, 0, 110, 100),
        };

        var standings = _calculator.Calculate(league, SeasonTestLeague.Names(league), results, StandingsRules.None);
        var row = standings.Row(SeasonTestLeague.TeamAt(0))!;

        Assert.Equal(new TeamRecord(1, 1), row.Overall);
        Assert.Equal(new TeamRecord(1, 0), row.DivisionRecord);
        Assert.Equal(new TeamRecord(1, 0), row.ConferenceRecord);
    }

    [Fact]
    public void Standings_CountOnlyRegularSeasonGames()
    {
        var league = SeasonTestLeague.Flat(2);
        var day = new SeasonDay(0);

        var postseasonFixture = new Fixture(
            GameId.For(SeasonTestLeague.Season, day, 0),
            day,
            SeasonTestLeague.TeamAt(0),
            SeasonTestLeague.TeamAt(1),
            SeasonPhase.Postseason);

        var postseasonResult = GameResult.Create(postseasonFixture, 120, 90);
        Assert.True(postseasonResult.IsSuccess);

        var standings = _calculator.Calculate(
            league,
            SeasonTestLeague.Names(league),
            [postseasonResult.Value],
            StandingsRules.None);

        Assert.All(standings.Rows, row => Assert.Equal(0, row.GamesPlayed));
    }

    private static GameResult Result(int slot, int dayIndex, int homeIndex, int awayIndex, int homePoints, int awayPoints) =>
        SeasonTestLeague.Result(
            SeasonTestLeague.Season,
            new SeasonDay(dayIndex),
            slot,
            SeasonTestLeague.TeamAt(homeIndex),
            SeasonTestLeague.TeamAt(awayIndex),
            homePoints,
            awayPoints);

    private static StandingsRules Sequence(params StandingsTieBreak[] steps)
    {
        var result = StandingsRules.Create(steps);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
