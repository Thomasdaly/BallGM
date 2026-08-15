using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

/// <summary>
/// The aggregate operations a trade needs. Both exist because doing the same work from outside the
/// aggregate would have to break an invariant on the way through.
/// </summary>
public sealed class TeamTradeApplicationTests
{
    private static readonly Season Season2031 = new(2031);

    [Fact]
    public void ApplyTrade_AllowsAOneForOneByATeamSittingExactlyOnTheRosterMinimum()
    {
        var team = TeamWith(playerCount: 3, minimum: 3, maximum: 5);
        var leaving = team.PlayerIds.First();
        var arriving = new PlayerId(SortableId.NewId());

        var result = team.ApplyTrade([leaving], [arriving]);

        // Removing first and adding second would have dipped below the minimum halfway through, and
        // no rule anybody wrote forbids this trade.
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(3, team.RosterCount);
        Assert.Contains(arriving, team.PlayerIds);
        Assert.DoesNotContain(leaving, team.PlayerIds);
    }

    [Fact]
    public void ApplyTrade_AllowsAOneForOneByATeamSittingExactlyOnTheRosterMaximum()
    {
        var team = TeamWith(playerCount: 5, minimum: 3, maximum: 5);
        var leaving = team.PlayerIds.First();
        var arriving = new PlayerId(SortableId.NewId());

        var result = team.ApplyTrade([leaving], [arriving]);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(5, team.RosterCount);
    }

    [Fact]
    public void ApplyTrade_RejectsATradeThatWouldOverfillTheRoster()
    {
        var team = TeamWith(playerCount: 5, minimum: 3, maximum: 5);
        var rosterBefore = team.PlayerIds.ToArray();

        var result = team.ApplyTrade(
            [team.PlayerIds.First()],
            [new PlayerId(SortableId.NewId()), new PlayerId(SortableId.NewId()), new PlayerId(SortableId.NewId())]);

        Assert.True(result.IsFailure);
        Assert.Equal("roster.maximum_exceeded", Assert.Single(result.Errors).Code);
        Assert.Equal(Sorted(rosterBefore), Sorted(team.PlayerIds));
    }

    [Fact]
    public void ApplyTrade_RejectsATradeThatWouldEmptyTheRosterBelowItsMinimum()
    {
        var team = TeamWith(playerCount: 3, minimum: 3, maximum: 5);
        var rosterBefore = team.PlayerIds.ToArray();

        var result = team.ApplyTrade(team.PlayerIds.Take(2).ToArray(), []);

        Assert.True(result.IsFailure);
        Assert.Equal("roster.minimum_required", Assert.Single(result.Errors).Code);
        Assert.Equal(Sorted(rosterBefore), Sorted(team.PlayerIds));
    }

    [Fact]
    public void ApplyTrade_RejectsSendingAPlayerTheTeamDoesNotHave()
    {
        var team = TeamWith(playerCount: 4, minimum: 3, maximum: 5);

        var result = team.ApplyTrade([new PlayerId(SortableId.NewId())], []);

        Assert.True(result.IsFailure);
        Assert.Equal("roster.player_not_on_team", Assert.Single(result.Errors).Code);
        Assert.Equal(4, team.RosterCount);
    }

    [Fact]
    public void ApplyTrade_RejectsReceivingAPlayerTheTeamAlreadyHas()
    {
        var team = TeamWith(playerCount: 4, minimum: 3, maximum: 5);
        var existing = team.PlayerIds.First();

        var result = team.ApplyTrade([], [existing]);

        Assert.True(result.IsFailure);
        Assert.Equal("roster.player_already_on_team", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void RestoreRoster_PutsTheRosterBackExactlyAsItWas()
    {
        var team = TeamWith(playerCount: 4, minimum: 3, maximum: 5);
        var before = team.PlayerIds.ToArray();

        Assert.True(team.ApplyTrade([before[0]], [new PlayerId(SortableId.NewId())]).IsSuccess);
        team.RestoreRoster(before);

        Assert.Equal(Sorted(before), Sorted(team.PlayerIds));
    }

    [Fact]
    public void Contract_MovesToTheReceivingTeamSoTheSalaryTravelsWithThePlayer()
    {
        var contract = NewContract(new TeamId("TEAM-A"));
        var receiving = new TeamId("TEAM-B");

        var result = contract.TransferTo(receiving);

        Assert.True(result.IsSuccess);
        Assert.Equal(receiving, contract.TeamId);
        Assert.Equal(receiving, contract.ChargeFor(Season2031)!.TeamId);
    }

    [Fact]
    public void Contract_RefusesToMoveToTheTeamThatAlreadyHoldsIt()
    {
        var team = new TeamId("TEAM-A");
        var contract = NewContract(team);

        var result = contract.TransferTo(team);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.already_on_team", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Contract_RefusesToMoveOnceThePlayerHasBeenReleased()
    {
        var contract = NewContract(new TeamId("TEAM-A"));
        Assert.True(contract.Terminate(Season2031).IsSuccess);

        var result = contract.TransferTo(new TeamId("TEAM-B"));

        // What is left of a released contract is dead money on the team that released them, and dead
        // money is not a tradeable asset.
        Assert.True(result.IsFailure);
        Assert.Equal("contract.already_terminated", Assert.Single(result.Errors).Code);
    }

    private static IEnumerable<string> Sorted(IEnumerable<PlayerId> playerIds) =>
        playerIds.Select(playerId => playerId.Value).OrderBy(value => value, StringComparer.Ordinal);

    private static Team TeamWith(int playerCount, int minimum, int maximum)
    {
        var players = Enumerable
            .Range(0, playerCount)
            .Select(_ => new PlayerId(SortableId.NewId()))
            .ToList();

        var result = Team.Create(
            new TeamId(SortableId.NewId()),
            new FranchiseId(SortableId.NewId()),
            "Test Team",
            new RosterSizeLimits(minimum, maximum),
            players);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static Contract NewContract(TeamId teamId)
    {
        var result = Contract.Create(
            new ContractId(SortableId.NewId()),
            teamId,
            new PlayerId(SortableId.NewId()),
            [new ContractSeasonTerm(Season2031, new Money(10_000_000), new Money(10_000_000))]);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
