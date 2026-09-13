using BallGM.Domain.AI;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;

namespace BallGM.Rules.Tests.AI;

public sealed class RosterNeedsCalculatorTests
{
    private static readonly Season CurrentSeason = new(2031);
    private static readonly TeamId TeamId = new("TEAM-A");
    private static readonly RosterSizeLimits Limits = new(minimumPlayers: 8, maximumPlayers: 15);

    [Fact]
    public void PositionWithNobodyListedIsAStarterNeed()
    {
        var starter = Player("STARTER", overall: 70);
        var chart = Chart(Slot(starter.Id, Position.SmallForward, depthRank: 1));
        var players = ById(starter);

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, EmptyCapSheet());

        var need = result.NeedAt(Position.Center);
        Assert.NotNull(need);
        Assert.Equal(NeedSeverity.Starter, need!.Severity);
        Assert.Equal("ai_needs.no_player_at_position", need.RuleCode);
    }

    [Fact]
    public void WeakStarterIsAStarterNeed()
    {
        var starter = Player("STARTER", overall: 40);
        var chart = Chart(Slot(starter.Id, Position.Center, depthRank: 1));
        var players = ById(starter);

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, EmptyCapSheet());

        var need = result.NeedAt(Position.Center);
        Assert.NotNull(need);
        Assert.Equal(NeedSeverity.Starter, need!.Severity);
        Assert.Equal("ai_needs.weak_starter", need.RuleCode);
    }

    [Fact]
    public void StrongStarterWithNoBackupIsADepthNeed()
    {
        var starter = Player("STARTER", overall: 70);
        var chart = Chart(Slot(starter.Id, Position.Center, depthRank: 1));
        var players = ById(starter);

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, EmptyCapSheet());

        var need = result.NeedAt(Position.Center);
        Assert.NotNull(need);
        Assert.Equal(NeedSeverity.Depth, need!.Severity);
        Assert.Equal("ai_needs.no_backup", need.RuleCode);
    }

    [Fact]
    public void StrongStarterWithABackupIsNoNeedAtAll()
    {
        var starter = Player("STARTER", overall: 70);
        var backup = Player("BACKUP", overall: 60);
        var chart = Chart(
            Slot(starter.Id, Position.Center, depthRank: 1),
            Slot(backup.Id, Position.Center, depthRank: 2));
        var players = ById(starter, backup);

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, EmptyCapSheet());

        Assert.Null(result.NeedAt(Position.Center));
    }

    [Fact]
    public void RosterBelowTheMinimumAddsANote()
    {
        var starter = Player("STARTER", overall: 70);
        var chart = Chart(Slot(starter.Id, Position.Center, depthRank: 1));
        var players = ById(starter);

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, EmptyCapSheet());

        Assert.Contains(result.Notes, finding => finding.RuleCode == "ai_needs.below_roster_minimum");
    }

    [Fact]
    public void PayrollBelowTheFloorAddsANote()
    {
        var starter = Player("STARTER", overall: 70);
        var chart = Chart(Slot(starter.Id, Position.Center, depthRank: 1));
        var players = ById(starter);
        var capSheet = CapSheetWithBreachedFloor();

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, capSheet);

        Assert.Contains(result.Notes, finding => finding.RuleCode == "ai_needs.below_payroll_floor");
    }

    [Fact]
    public void PayrollAboveTheFloorAddsNoNote()
    {
        var starter = Player("STARTER", overall: 70);
        var chart = Chart(Slot(starter.Id, Position.Center, depthRank: 1));
        var players = ById(starter);
        var capSheet = CapSheetWithCompliantFloor();

        var result = RosterNeedsCalculator.Assess(TeamId, chart, players, Limits, capSheet);

        Assert.DoesNotContain(result.Notes, finding => finding.RuleCode == "ai_needs.below_payroll_floor");
    }

    private static Player Player(string id, int overall) => Domain.Players.Player.Create(
        new PlayerId(id),
        id,
        Position.Center,
        new PlayerRating(overall),
        new DateOnly(CurrentSeason.Year - 27, 1, 1),
        seasonsOfService: 5).Value;

    private static IReadOnlyDictionary<PlayerId, Player> ById(params Player[] players) =>
        players.ToDictionary(player => player.Id);

    private static DepthChartSlot Slot(PlayerId playerId, Position position, int depthRank) =>
        new(playerId, position, depthRank, minutes: depthRank == 1 ? 30 : 10);

    private static DepthChart Chart(params DepthChartSlot[] slots) =>
        DepthChart.Create(TeamId, slots).Value;

    private static TeamCapSheet EmptyCapSheet() =>
        new(TeamId, CurrentSeason, Money.Zero, Money.Zero, Money.Zero, Money.Zero, [], []);

    private static TeamCapSheet CapSheetWithBreachedFloor()
    {
        var standing = new ThresholdStanding(
            CapThresholdKind.PayrollFloor,
            new Money(80_000_000),
            signedDistanceSmallestUnits: 10_000_000,
            ThresholdPosition.Under,
            "cap.under_payroll_floor",
            "Team is under the payroll floor.");

        return new TeamCapSheet(TeamId, CurrentSeason, Money.Zero, Money.Zero, Money.Zero, new Money(70_000_000), [], [standing]);
    }

    private static TeamCapSheet CapSheetWithCompliantFloor()
    {
        var standing = new ThresholdStanding(
            CapThresholdKind.PayrollFloor,
            new Money(80_000_000),
            signedDistanceSmallestUnits: -10_000_000,
            ThresholdPosition.Over,
            "cap.over_payroll_floor",
            "Team is over the payroll floor.");

        return new TeamCapSheet(TeamId, CurrentSeason, Money.Zero, Money.Zero, Money.Zero, new Money(90_000_000), [], [standing]);
    }
}
