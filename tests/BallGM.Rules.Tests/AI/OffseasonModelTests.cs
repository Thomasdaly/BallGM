using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;

namespace BallGM.Rules.Tests.AI;

public sealed class OffseasonModelTests
{
    private static readonly DateOnly Summer = new(2031, 7, 1);

    [Fact]
    public void ATeamBidsForAFreeAgentWhoImprovesItsRotation_AtHisAskingPrice()
    {
        var league = MarketTestLeague.Build([[5_000_000, 5_000_000, 5_000_000]], freeAgentOverall: 80);

        var offers = OffseasonModel.MarketOffers(league.Team(0), [league.FreeAgent], league.Context(), CompetitiveDirection.Retooling, Summer, round: 0, maximumOffers: 3);

        var offer = Assert.Single(offers);
        Assert.Equal(league.FreeAgent.Id, offer.Offer.PlayerId);
        Assert.Contains("bids", offer.Rationale.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullRosterBidsForNobody_AndPricesFallAsTheSummerGoesOn()
    {
        var full = MarketTestLeague.Build([[1_000_000, 1_000_000, 1_000_000]], maximumRoster: 3);
        Assert.Empty(OffseasonModel.MarketOffers(full.Team(0), [full.FreeAgent], full.Context(), CompetitiveDirection.Retooling, Summer, 0, 3));

        var open = MarketTestLeague.Build([[5_000_000, 5_000_000, 5_000_000]], freeAgentOverall: 80);
        var first = OffseasonModel.MarketOffers(open.Team(0), [open.FreeAgent], open.Context(), CompetitiveDirection.Retooling, Summer, 0, 3).Single();
        var later = OffseasonModel.MarketOffers(open.Team(0), [open.FreeAgent], open.Context(), CompetitiveDirection.Retooling, Summer, 3, 3).Single();
        Assert.True(later.Offer.FirstSeasonCompensation < first.Offer.FirstSeasonCompensation);
    }

    [Fact]
    public void AFreeAgentTakesTheMoney_ThenTheWinningTeam_ThenTheLongerDeal()
    {
        var league = MarketTestLeague.Build([[1_000_000], [1_000_000], [1_000_000]]);
        var richer = league.Offer(0, 20_000_000, seasons: 2);
        var winner = league.Offer(1, 19_500_000, seasons: 2);
        var loser = league.Offer(2, 19_500_000, seasons: 4);
        var records = new Dictionary<TeamId, TeamRecord>
        {
            [league.Team(0).Id] = new(20, 62),
            [league.Team(1).Id] = new(62, 20),
            [league.Team(2).Id] = new(20, 62),
        };

        // A .756 team's 19.5 million beats a .244 team's 20 million: the weighting is small but real.
        Assert.Equal(league.Team(1).Id, OffseasonModel.Choose([richer, winner, loser], records, "Player").Offer.TeamId);
        Assert.Equal(league.Team(0).Id, OffseasonModel.Choose([richer, loser], records, "Player").Offer.TeamId);
    }

    [Fact]
    public void DirectionSetsTheBudget_AndAgeSetsTheTerm()
    {
        var thresholds = SigningTestLeague.StandardThresholds;
        Assert.Equal(thresholds.FirstApron, OffseasonModel.BudgetFor(CompetitiveDirection.Contending, thresholds));
        Assert.Equal(thresholds.LuxuryTax, OffseasonModel.BudgetFor(CompetitiveDirection.Retooling, thresholds));
        Assert.Equal(thresholds.SoftCap, OffseasonModel.BudgetFor(CompetitiveDirection.Rebuilding, thresholds));

        Assert.Equal(5, OffseasonModel.TermFor(24, star: true, maximumSeasons: 5));
        Assert.Equal(1, OffseasonModel.TermFor(34, star: true, maximumSeasons: 5));
        Assert.Equal(3, OffseasonModel.TermFor(22, star: false, maximumSeasons: 3));
    }

    [Fact]
    public void AWinningTeamContends_AndALosingOneWithAnOldRosterRebuilds()
    {
        var old = Enumerable.Range(0, 5)
            .Select(index => Player.Create(new PlayerId($"OLD-{index}"), $"Old {index}", Position.Center, new PlayerRating(60), new DateOnly(1995, 1, 1), 10).Value)
            .ToList();

        Assert.Equal(CompetitiveDirection.Contending, OffseasonModel.DirectionFor(new TeamRecord(50, 32), old, Summer, 26));
        Assert.Equal(CompetitiveDirection.Rebuilding, OffseasonModel.DirectionFor(new TeamRecord(25, 57), old, Summer, 26));
    }

    [Fact]
    public void ARoundGivesEachTeamAtMostOneSigning_AndTheBestPlayerChoosesFirst()
    {
        var league = MarketTestLeague.Build([[1_000_000], [1_000_000]]);
        var star = league.FreeAgent;
        var offers = new[]
        {
            new MarketOffer(league.Offer(0, 10_000_000), 10, new RuleFinding("bid", "bid")),
            new MarketOffer(league.Offer(1, 12_000_000), 10, new RuleFinding("bid", "bid")),
        };

        var accepted = OffseasonModel.ResolveRound(offers, new Dictionary<PlayerId, Player> { [star.Id] = star }, new Dictionary<TeamId, TeamRecord>());

        var only = Assert.Single(accepted);
        Assert.Equal(league.Team(1).Id, only.Bid.Offer.TeamId);
    }
}
