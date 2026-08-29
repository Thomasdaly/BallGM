using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

public sealed class MinutesAllocationBoundsTests
{
    [Fact]
    public void TeamMinutes_AreTheGameLengthTimesThePlayersOnTheFloor()
    {
        Assert.Equal(
            MinutesAllocationBounds.RegulationMinutes * MinutesAllocationBounds.PlayersOnFloor,
            MinutesAllocationBounds.TeamMinutesPerGame);
    }

    [Fact]
    public void NoPlayerMayBeGivenAWholeGame()
    {
        Assert.True(MinutesAllocationBounds.MaximumMinutesPerPlayer < MinutesAllocationBounds.RegulationMinutes);
    }

    [Fact]
    public void TheRotationFloorSitsBelowTheRotationCeiling()
    {
        Assert.True(MinutesAllocationBounds.MinimumRotationMinutes < MinutesAllocationBounds.MaximumMinutesPerPlayer);
    }

    [Fact]
    public void AFullRotationsMinimumMinutesFitInsideAGame()
    {
        Assert.True(
            MinutesAllocationBounds.MaximumRotationSize * MinutesAllocationBounds.MinimumRotationMinutes
            <= MinutesAllocationBounds.TeamMinutesPerGame);
    }

    [Fact]
    public void ARotationIsAtLeastAsBigAsTheNumberOfPlayersOnTheFloor()
    {
        Assert.True(MinutesAllocationBounds.MaximumRotationSize >= MinutesAllocationBounds.PlayersOnFloor);
    }

    [Fact]
    public void TheStatedShortHandedThresholdIsTheOneTheMaximumImplies()
    {
        Assert.Equal(
            (int)Math.Ceiling((double)MinutesAllocationBounds.TeamMinutesPerGame / MinutesAllocationBounds.MaximumMinutesPerPlayer),
            MinutesAllocationBounds.MinimumRotationWithinBounds);
    }
}

public sealed class DepthChartBuilderTests
{
    private static readonly TeamId Team = new("TEAM-01");
    private static readonly RosterSizeLimits Limits = new(12, 15);

    private readonly DepthChartBuilder _builder = new();

    [Fact]
    public void Rotation_DividesExactlyTheMinutesAGameHas()
    {
        var build = Build(SeasonTestLeague.Squad(Team, 12), rosterCount: 12);

        Assert.Equal(MinutesAllocationBounds.TeamMinutesPerGame, build.Chart.TotalMinutes);
    }

    [Fact]
    public void Rotation_KeepsEveryPlayerInsideTheStatedMinutesBounds()
    {
        var build = Build(SeasonTestLeague.Squad(Team, 12), rosterCount: 12);

        Assert.All(build.Chart.Slots, slot =>
        {
            Assert.True(slot.Minutes >= MinutesAllocationBounds.MinimumRotationMinutes);
            Assert.True(slot.Minutes <= MinutesAllocationBounds.MaximumMinutesPerPlayer);
        });
    }

    [Fact]
    public void Rotation_IsNoLargerThanTheStatedMaximum()
    {
        var build = Build(SeasonTestLeague.Squad(Team, 15), rosterCount: 15);

        Assert.True(build.Chart.PlayerCount <= MinutesAllocationBounds.MaximumRotationSize);
    }

    [Fact]
    public void Rotation_StartsSomebodyAtEveryPosition()
    {
        var build = Build(SeasonTestLeague.Squad(Team, 12), rosterCount: 12);

        Assert.Equal(Enum.GetValues<Position>().Length, build.Chart.Starters.Count);
    }

    [Fact]
    public void Rotation_IsTheSameEveryTimeForTheSameSquad()
    {
        var squad = SeasonTestLeague.Squad(Team, 12);

        var first = Build(squad, rosterCount: 12);
        var second = Build(squad, rosterCount: 12);

        Assert.Equal(
            first.Chart.Slots.Select(slot => $"{slot.PlayerId.Value}:{slot.Position}:{slot.DepthRank}:{slot.Minutes}"),
            second.Chart.Slots.Select(slot => $"{slot.PlayerId.Value}:{slot.Position}:{slot.DepthRank}:{slot.Minutes}"));
    }

    [Fact]
    public void Rotation_ReportsAPositionCoveredBySomebodyWhoDoesNotPlayThere()
    {
        // Five guards and nobody else: two of them have to cover the frontcourt.
        var guards = Enumerable.Range(0, 6)
            .Select(index => new AvailablePlayer(new PlayerId($"G{index}"), Position.PointGuard, 70 - index))
            .ToList();

        var build = Build(guards, rosterCount: 6);

        Assert.Contains(build.Notes, note => note.RuleCode == "depth_chart.position_covered_out_of_position");
    }

    [Fact]
    public void Rotation_ReportsBreachingThePerPlayerMaximumWhenASquadIsTooShortToCoverAGame()
    {
        var build = Build(SeasonTestLeague.Squad(Team, 5), rosterCount: 5);

        Assert.Contains(build.Warnings, warning => warning.RuleCode == "depth_chart.minutes_above_normal_maximum");
        Assert.Equal(MinutesAllocationBounds.TeamMinutesPerGame, build.Chart.TotalMinutes);
        Assert.Contains(build.Chart.Slots, slot => slot.Minutes > MinutesAllocationBounds.MaximumMinutesPerPlayer);
    }

    [Fact]
    public void Rotation_NotesARosterBelowTheLeagueMinimumWithoutRefusingToFieldATeam()
    {
        var build = Build(SeasonTestLeague.Squad(Team, 8), rosterCount: 8);

        Assert.Contains(build.Notes, note => note.RuleCode == "depth_chart.roster_below_league_minimum");
        Assert.False(build.Chart.IsEmpty);
    }

    [Fact]
    public void Rotation_IsEmptyAndSaysSoWhenNobodyIsAvailable()
    {
        var build = Build([], rosterCount: 0);

        Assert.True(build.Chart.IsEmpty);
        Assert.Contains(build.Warnings, warning => warning.RuleCode == "depth_chart.no_available_players");
    }

    [Fact]
    public void Rotation_RanksTheBestPlayerAtAPositionAsItsStarter()
    {
        var players = Enum.GetValues<Position>()
            .Select(position => new AvailablePlayer(new PlayerId($"FILLER-{position}"), position, 60))
            .Append(new AvailablePlayer(new PlayerId("BACKUP"), Position.Center, 55))
            .Append(new AvailablePlayer(new PlayerId("STARTER"), Position.Center, 90))
            .ToList();

        var build = Build(players, rosterCount: players.Count);
        var centre = build.Chart.At(Position.Center);

        Assert.Equal("STARTER", centre[0].PlayerId.Value);
        Assert.True(centre[0].IsStarter);
    }

    private DepthChartBuild Build(IReadOnlyList<AvailablePlayer> players, int rosterCount)
    {
        var result = _builder.Build(Team, players, Limits, rosterCount);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
