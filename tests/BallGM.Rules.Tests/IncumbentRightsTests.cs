using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

public sealed class IncumbentRightsTests
{
    private static readonly IncumbentRightsRules Rights = IncumbentRightsRules.Create(2, 1, 125).Value;

    // Payroll 125M against a 100M cap: no room, but under the first apron so the allowance is there.
    private static readonly long[] OverTheCap = [45_000_000, 45_000_000, 35_000_000];

    [Fact]
    public void ATeamThatHeldItsFreeAgentLongEnough_ReSignsHimOverTheCapAboveItsAllowance()
    {
        var league = SigningTestLeague.Build(OverTheCap);
        league.PlayedFor(league.Team.Id, seasons: 2);

        var withRights = new SigningValidator().Validate(league.Offer(20_000_000), league.Context() with { IncumbentRights = Rights }).Value;
        var without = new SigningValidator().Validate(league.Offer(20_000_000), league.Context()).Value;

        Assert.True(withRights.IsLegal);
        Assert.Contains(withRights.Routes, route => route.Kind == SigningRouteKind.IncumbentRights && route.Permits);
        Assert.False(without.IsLegal);
    }

    [Fact]
    public void OneSeasonWithTheTeam_OrAnotherTeamsHistory_EarnsNoIncumbentRights()
    {
        var shortStay = SigningTestLeague.Build(OverTheCap);
        shortStay.PlayedFor(shortStay.Team.Id, seasons: 1);
        Assert.False(new SigningValidator().Validate(shortStay.Offer(20_000_000), shortStay.Context() with { IncumbentRights = Rights }).Value.IsLegal);
        Assert.Equal(1, shortStay.Context().SeasonsWithTeam);

        var elsewhere = SigningTestLeague.Build(OverTheCap);
        elsewhere.PlayedFor(new TeamId("TEAM-ELSEWHERE"), seasons: 4);
        Assert.False(new SigningValidator().Validate(elsewhere.Offer(20_000_000), elsewhere.Context() with { IncumbentRights = Rights }).Value.IsLegal);
        Assert.Null(elsewhere.Context().HoldsIncumbentRights);
    }

    [Fact]
    public void AReleaseForfeitsIncumbentRights()
    {
        var league = SigningTestLeague.Build(OverTheCap);
        league.ReleasedFrom(league.Team.Id, 10_000_000, boughtOut: false);

        Assert.Equal(0, league.Context().SeasonsWithTeam);
        Assert.False((league.Context() with { IncumbentRights = Rights }).HoldsIncumbentRights);
    }

    [Fact]
    public void AnUnclaimedRookieDealPlayerIsTenderedAQualifyingOffer_AVeteranIsNot()
    {
        var season = new Season(2031);
        // In the final season of a four-season deal from 2028: three seasons completed, none before it.
        var rookie = Player.Create(new PlayerId("ROOKIE"), "Young Gun", Position.PointGuard, new PlayerRating(75), new DateOnly(2008, 1, 1), 3).Value;
        var veteran = Player.Create(new PlayerId("VETERAN"), "Old Hand", Position.Center, new PlayerRating(75), new DateOnly(1999, 1, 1), 10).Value;
        var roster = Enumerable.Range(0, 8)
            .Select(index => Player.Create(new PlayerId($"BENCH-{index}"), $"Bench {index}", Position.SmallForward, new PlayerRating(60), new DateOnly(2003, 1, 1), 5).Value)
            .Append(rookie)
            .Append(veteran)
            .ToList();
        var franchise = Franchise.Create(new FranchiseId("F"), "Franchise").Value;
        var team = Team.Create(new TeamId("TEAM"), franchise.Id, "Testfield", new RosterSizeLimits(5, 15), roster.Select(player => player.Id)).Value;
        Contract Deal(Player player, int firstYear) => Contract.Create(new ContractId($"C-{player.Id.Value}"), team.Id, player.Id,
            Enumerable.Range(firstYear, 2032 - firstYear).Select(year => new ContractSeasonTerm(new Season(year), new Money(4_000_000), new Money(4_000_000)))).Value;
        var contracts = new List<Contract> { Deal(rookie, 2028), Deal(veteran, 2029) };

        var tenders = OffseasonModel.QualifyingOffers(team, roster, contracts, season, Rights, new DateOnly(2032, 7, 1));

        var tender = Assert.Single(tenders);
        Assert.Equal(rookie.Id, tender.PlayerId);
        Assert.Equal(5_000_000, tender.Salary.SmallestUnits);
        Assert.Empty(OffseasonModel.QualifyingOffers(team, roster, contracts, season, IncumbentRightsRules.None, new DateOnly(2032, 7, 1)));
    }

    [Fact]
    public void IncumbentRightsRulesRefuseNonsense()
    {
        Assert.True(IncumbentRightsRules.Create(0, 1, 125).IsFailure);
        Assert.True(IncumbentRightsRules.Create(2, 1, 50).IsFailure);
        Assert.True(IncumbentRightsRules.Create(2, 0, 0).IsSuccess);
    }
}

public sealed class SalaryScaleTests
{
    private static IReadOnlyList<Player> League(int offset) => Enumerable.Range(0, 100)
        .Select(index => Player.Create(new PlayerId($"P-{index}"), $"Player {index}", Position.SmallForward, new PlayerRating(40 + (index / 2) + offset), new DateOnly(2000, 1, 1), 5).Value)
        .ToList();

    [Fact]
    public void TheLeaguesBestAskTheMaximum_AndTheBottomHalfTheMinimum()
    {
        var scale = SalaryScale.From(League(0));

        Assert.Equal(100, scale.ShareFor(89));
        Assert.Equal(0, scale.ShareFor(40));
        Assert.Equal(0, scale.ShareFor(55));
        Assert.InRange(scale.ShareFor(80), 30, 80);
    }

    [Fact]
    public void AsksFollowRankNotTheRatingScale()
    {
        var low = SalaryScale.From(League(0));
        var high = SalaryScale.From(League(10));

        foreach (var overall in new[] { 50, 65, 75, 82, 89 })
        {
            Assert.Equal(low.ShareFor(overall), high.ShareFor(overall + 10));
        }
    }
}
