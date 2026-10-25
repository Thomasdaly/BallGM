using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class SeasonScheduleExtensionTests
{
    private static readonly Season Season = new(2031);
    private static readonly TeamId A = new("TEAM-A");
    private static readonly TeamId B = new("TEAM-B");
    private static readonly TeamId C = new("TEAM-C");
    private static readonly TeamId D = new("TEAM-D");

    private static Fixture Game(int day, int slot, TeamId home, TeamId away, SeasonPhase phase = SeasonPhase.RegularSeason) =>
        new(GameId.For(Season, new SeasonDay(day), slot), new SeasonDay(day), home, away, phase);

    [Fact]
    public void AddingPostseasonGames_ReadsExactlyAsBuildingTheWholeScheduleAtOnce()
    {
        Fixture[] regular = [Game(0, 0, A, B), Game(0, 1, C, D), Game(2, 0, B, C), Game(5, 0, D, A)];
        Fixture[] postseason = [Game(9, 1, C, D, SeasonPhase.Postseason), Game(4, 0, A, C, SeasonPhase.Postseason), Game(9, 0, A, B, SeasonPhase.Postseason)];

        var extended = SeasonSchedule.Create(regular).Value.With(postseason).Value;
        var whole = SeasonSchedule.Create([.. regular, .. postseason]).Value;

        Assert.Equal(whole.Fixtures, extended.Fixtures);
        foreach (var day in Enumerable.Range(0, 11).Select(index => new SeasonDay(index)))
        {
            Assert.Equal(whole.On(day), extended.On(day));
            foreach (var team in new[] { A, B, C, D })
            {
                Assert.Equal(whole.PreviousGameDay(team, day), extended.PreviousGameDay(team, day));
            }
        }

        Assert.All(postseason, fixture => Assert.Same(fixture, extended.Game(fixture.Id)));
    }

    [Fact]
    public void AddingAGameForATeamAlreadyPlayingThatDay_IsRefusedAsCreateRefusesIt()
    {
        var schedule = SeasonSchedule.Create([Game(3, 0, A, B)]).Value;

        var clash = schedule.With([Game(3, 1, B, C)]);
        var duplicate = schedule.With([Game(3, 0, C, D)]);

        Assert.Equal("schedule.team_plays_twice_on_one_day", Assert.Single(clash.Errors).Code);
        Assert.Equal("schedule.duplicate_game_id", Assert.Single(duplicate.Errors).Code);
    }
}
