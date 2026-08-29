using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Rules.Configuration;
using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>
/// The postseason as the engine sequences it: drawn a round at a time as the days arrive, played
/// through the same advance path the regular season uses, and rolled back in full when a day fails.
/// <para>
/// The league whose <see cref="PostseasonRules"/> are <see cref="PostseasonRules.None"/> is covered
/// as deliberately as the league that holds one. It is the case a build assumes away, and a season
/// that could not end cleanly without a bracket would be a season no such league could finish.
/// </para>
/// </summary>
public sealed class PostseasonSeasonEngineTests
{
    [Fact]
    public void PlaysTheWholeBracketAndEndsWithTheTopSeedAsChampion()
    {
        var league = SeasonTestFixtures.Flat(8);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 4, seriesLengths: [3, 5], postseasonDays: 8);
        var context = SeasonTestFixtures.Context(league, postseason);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var run = Start(engine, context);
        var advanced = engine.Advance(run, context, run.Calendar.LengthInDays);

        Assert.True(advanced.IsSuccess);
        Assert.True(run.IsComplete);

        var bracket = run.Schedule.Fixtures.Where(fixture => fixture.Phase == SeasonPhase.Postseason).ToArray();

        // Two best-of-three semi-finals swept in two games each, then a best-of-five final swept in
        // three: the ordinally-first team always wins, so no series goes past the minimum.
        Assert.Equal(7, bracket.Length);
        Assert.All(bracket, fixture => Assert.True(run.HasResult(fixture.Id)));

        var final = bracket.OrderBy(fixture => fixture.Day.Index).Last();
        Assert.Equal(SeasonTestFixtures.TeamAt(0), run.ResultOf(final.Id)!.WinnerId);
    }

    [Fact]
    public void DrawsTheFirstRoundFromTheRegularSeasonTableRatherThanFromTheTeamList()
    {
        var league = SeasonTestFixtures.Flat(8);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 4, seriesLengths: [3, 5], postseasonDays: 8);
        var context = SeasonTestFixtures.Context(league, postseason);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var run = Start(engine, context);
        Assert.True(engine.Advance(run, context, run.Calendar.LengthInDays).IsSuccess);

        var standings = engine.Standings(run, context);
        var qualified = standings.Rows.Take(4).Select(row => row.TeamId).ToArray();

        var firstRoundDay = run.Calendar.Phase(SeasonPhase.Postseason)!.StartDay;
        var openingGames = run.Schedule.On(firstRoundDay);

        Assert.Equal(2, openingGames.Count);

        // Bracket order pairs the first seed with the fourth and the second with the third, so
        // nobody outside the top four appears and the top two cannot meet before the final.
        Assert.Equal(qualified[0], openingGames[0].HomeTeamId);
        Assert.Equal(qualified[3], openingGames[0].AwayTeamId);
        Assert.Equal(qualified[1], openingGames[1].HomeTeamId);
        Assert.Equal(qualified[2], openingGames[1].AwayTeamId);
    }

    [Fact]
    public void AdvancingOneDayAtATimeDrawsTheSameBracketAsAdvancingInOneGo()
    {
        var league = SeasonTestFixtures.Flat(8);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 4, seriesLengths: [3, 5], postseasonDays: 8);

        var wholeContext = SeasonTestFixtures.Context(league, postseason);
        var wholeEngine = new SeasonEngine(new OrdinalMatchEngine());
        var whole = Start(wholeEngine, wholeContext);
        Assert.True(wholeEngine.Advance(whole, wholeContext, whole.Calendar.LengthInDays).IsSuccess);

        var stepContext = SeasonTestFixtures.Context(league, postseason);
        var stepEngine = new SeasonEngine(new OrdinalMatchEngine());
        var stepped = Start(stepEngine, stepContext);

        while (!stepped.IsComplete)
        {
            Assert.True(stepEngine.Advance(stepped, stepContext, 1).IsSuccess);
        }

        Assert.Equal(
            whole.Schedule.Fixtures.Select(fixture => fixture.Id.Value),
            stepped.Schedule.Fixtures.Select(fixture => fixture.Id.Value));

        Assert.Equal(
            whole.ResultsInPlayOrder.Select(result => $"{result.GameId.Value}:{result.HomePoints}-{result.AwayPoints}"),
            stepped.ResultsInPlayOrder.Select(result => $"{result.GameId.Value}:{result.HomePoints}-{result.AwayPoints}"));
    }

    [Fact]
    public void ALeagueWithNoPostseasonEndsWhenItsRegularSeasonDoes()
    {
        var league = SeasonTestFixtures.Flat(8);
        var context = SeasonTestFixtures.Context(league, PostseasonRules.None);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var run = Start(engine, context);

        Assert.False(run.Calendar.Has(SeasonPhase.Postseason));

        var advanced = engine.Advance(run, context, run.Calendar.LengthInDays);

        Assert.True(advanced.IsSuccess);
        Assert.True(run.IsComplete);
        Assert.Empty(run.Unplayed);
        Assert.DoesNotContain(run.Schedule.Fixtures, fixture => fixture.Phase == SeasonPhase.Postseason);

        // The season is over and nothing about it is left unstated: the absent postseason is a note
        // on every assessment rather than a silence.
        Assert.Contains(
            advanced.Value.Assessment.Notes,
            note => note.RuleCode == "season.postseason_not_configured");
    }

    [Fact]
    public void ALeagueWithNoPostseasonRefusesToBeAdvancedPastTheEndOfItsSeason()
    {
        var league = SeasonTestFixtures.Flat(8);
        var context = SeasonTestFixtures.Context(league, PostseasonRules.None);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var run = Start(engine, context);
        Assert.True(engine.Advance(run, context, run.Calendar.LengthInDays).IsSuccess);

        var pastTheEnd = engine.Advance(run, context, 1);

        Assert.True(pastTheEnd.IsFailure);
        Assert.Contains(pastTheEnd.Errors, error => error.Code == "season.advance_past_end_of_season");
    }

    [Fact]
    public void RefusesAnAdvanceThatWouldReachAPostseasonThisLeagueCannotSeed()
    {
        // Five qualifiers is not a power of two, so instead: a league of four teams that qualifies
        // four per conference has no bracket to draw from a conference of two.
        var league = SeasonTestFixtures.Flat(4);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 8, seriesLengths: [3, 3, 3], postseasonDays: 12);
        var context = SeasonTestFixtures.Context(league, postseason);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var run = Start(engine, context);
        var advanced = engine.Advance(run, context, run.Calendar.LengthInDays);

        Assert.True(advanced.IsFailure);
        Assert.Contains(advanced.Errors, error => error.Code == "season.postseason_bracket_cannot_be_seeded");

        // Refused, and nothing moved: the assessment ran before a single day was advanced.
        Assert.Equal(SeasonDay.Opening, run.CurrentDay);
        Assert.Empty(run.Results);
    }

    [Fact]
    public void WarnsAtTheStartWhenThePostseasonNeedsMoreDaysThanTheCalendarReservesForIt()
    {
        var league = SeasonTestFixtures.Flat(8);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 4, seriesLengths: [7, 7], postseasonDays: 4);
        var context = SeasonTestFixtures.Context(league, postseason);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var started = engine.Start(context, SeasonTestFixtures.Opening, seed: 7);

        Assert.True(started.IsSuccess);
        Assert.Contains(
            started.Value.Warnings,
            warning => warning.RuleCode == "season.postseason_needs_more_days_than_reserved");
    }

    [Fact]
    public void AssessingAnAdvanceThatReachesThePostseasonSaysTheBracketIsNotDrawnYet()
    {
        var league = SeasonTestFixtures.Flat(8);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 4, seriesLengths: [3, 5], postseasonDays: 8);
        var context = SeasonTestFixtures.Context(league, postseason);
        var engine = new SeasonEngine(new OrdinalMatchEngine());

        var run = Start(engine, context);
        var assessment = engine.Assess(run, context, run.Calendar.LengthInDays);

        Assert.True(assessment.IsSuccess);
        Assert.True(assessment.Value.IsPermitted);
        Assert.Contains(
            assessment.Value.Notes,
            note => note.RuleCode == "season.postseason_bracket_not_yet_drawn");

        // An assessment changes nothing, the bracket included.
        Assert.DoesNotContain(run.Schedule.Fixtures, fixture => fixture.Phase == SeasonPhase.Postseason);
    }

    [Fact]
    public void ABuildWithNoMatchModelStillDrawsTheOpeningRound()
    {
        var league = SeasonTestFixtures.Flat(8);
        var postseason = SeasonTestFixtures.Postseason(qualifiers: 4, seriesLengths: [3, 5], postseasonDays: 8);
        var context = SeasonTestFixtures.Context(league, postseason);
        var engine = new SeasonEngine(new UnplayedMatchEngine());

        var run = Start(engine, context);
        Assert.True(engine.Advance(run, context, run.Calendar.LengthInDays).IsSuccess);

        // Nobody won anything, so no round is ever decided and the bracket never leaves round one —
        // but a bracket is a statement about the table, and the table exists whether or not this
        // build can decide a game.
        var bracket = run.Schedule.Fixtures.Where(fixture => fixture.Phase == SeasonPhase.Postseason).ToArray();

        Assert.NotEmpty(bracket);
        Assert.All(bracket, fixture => Assert.False(run.HasResult(fixture.Id)));
    }

    private static SeasonRun Start(SeasonEngine engine, SeasonContext context)
    {
        var started = engine.Start(context, SeasonTestFixtures.Opening, seed: 7);
        Assert.True(started.IsSuccess);
        return started.Value.Run;
    }
}
