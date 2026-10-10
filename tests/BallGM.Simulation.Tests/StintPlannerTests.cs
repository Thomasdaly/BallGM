using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

public sealed class StintPlannerTests
{
    private static readonly RotationPlayer[] Rotation =
    [
        new("P0", 36, true, 1), new("P1", 34, true, 1), new("P2", 32, true, 1), new("P3", 30, true, 1), new("P4", 28, true, 1),
        new("P5", 24, false, 2), new("P6", 20, false, 2), new("P7", 16, false, 2), new("P8", 12, false, 2), new("P9", 8, false, 2),
    ];

    [Theory]
    [InlineData(89)]
    [InlineData(98)]
    [InlineData(107)]
    public void EveryPossessionHasFiveDifferentPlayersAndShareMatchesPlannedMinutes(int possessions)
    {
        var plan = StintPlanner.Plan(Rotation, possessions);

        Assert.All(plan, lineup => Assert.Equal(5, lineup.Distinct().Count()));
        Assert.Equal([0, 1, 2, 3, 4], plan[0]);

        var played = new int[Rotation.Length];
        foreach (var lineup in plan)
        {
            foreach (var index in lineup)
            {
                played[index]++;
            }
        }

        var minutes = StintPlanner.Minutes(played, possessions, new int[Rotation.Length], overtimePeriods: 0);

        Assert.Equal(240, minutes.Sum());
        for (var index = 0; index < Rotation.Length; index++)
        {
            Assert.InRange(minutes[index], Rotation[index].PlannedMinutes - 1, Rotation[index].PlannedMinutes + 1);
        }
    }

    [Fact]
    public void ARotationOfFivePlaysEveryPossession()
    {
        var plan = StintPlanner.Plan(Rotation[..5], 98);

        Assert.All(plan, lineup => Assert.Equal([0, 1, 2, 3, 4], lineup));
    }

    [Fact]
    public void OvertimeMinutesAreAddedFivePerPeriodForTheFiveWhoPlayedThem()
    {
        var regulation = new[] { 98, 98, 98, 98, 98, 0 };
        var overtime = new[] { 22, 22, 22, 22, 22, 0 };

        var minutes = StintPlanner.Minutes(regulation, 98, overtime, overtimePeriods: 2);

        Assert.Equal([58, 58, 58, 58, 58, 0], minutes);
    }

    [Fact]
    public void ARotationPlanningMoreThanAFullGameForSomeoneIsCappedAtTheGame()
    {
        // 200 planned minutes with three players at 42: scaled up, each would be owed more possessions
        // than the game has. Nobody can play more than every possession.
        RotationPlayer[] overPlanned =
        [
            new("A", 42, true, 1), new("B", 42, true, 1), new("C", 42, true, 1), new("D", 42, true, 1),
            new("E", 20, true, 1), new("F", 12, false, 2),
        ];

        var plan = StintPlanner.Plan(overPlanned, 98);

        Assert.All(plan, lineup => Assert.Equal(5, lineup.Distinct().Count()));
        var played = Enumerable.Range(0, overPlanned.Length).Select(index => plan.Count(lineup => lineup.Contains(index))).ToArray();
        Assert.Equal(5 * 98, played.Sum());
        Assert.All(played, count => Assert.InRange(count, 0, 98));
    }

    [Fact]
    public void AShortHandedRotationOfFourPlaysEveryPossessionAndFinishes()
    {
        // Regression: a team down to four fit players, each planned past the whole game, once sent
        // the possession targets into a loop that could never place the fifth place a possession.
        var rotation = Enumerable.Range(0, 4)
            .Select(index => new RotationPlayer($"P{index}", 60, IsStarter: true, DepthRank: index))
            .ToList();

        var plan = StintPlanner.Plan(rotation, 100);

        Assert.Equal(100, plan.Length);
        Assert.All(plan, lineup => Assert.Equal([0, 1, 2, 3], lineup));
    }
}
