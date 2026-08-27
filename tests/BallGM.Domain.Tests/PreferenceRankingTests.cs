using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

/// <summary>
/// How two offers are compared. The rule under test is the one the whole market rests on: factors
/// speak in turn, a factor inside its materiality band has no opinion, and nothing is ever added up.
/// </summary>
public sealed class PreferenceRankingTests
{
    [Fact]
    public void Ranking_LetsTheFirstFactorThatActuallyDiffersDecide()
    {
        var richer = Preference(money: 90, term: 40, fit: 40, demand: 40);
        var poorer = Preference(money: 60, term: 100, fit: 100, demand: 100);

        var comparison = PreferenceRanking.Compare(richer, poorer);

        // Money leads the order, and thirty points is well past its band, so nothing after it is
        // consulted — including three factors where the other offer is far ahead.
        Assert.Equal(1, comparison.Sign);
        Assert.Equal(PreferenceFactorKind.Money, comparison.DecidedBy);
    }

    [Fact]
    public void Ranking_HandsOverToTheNextFactorWhenTheDifferenceIsTooSmallToNotice()
    {
        var shorter = Preference(money: 82, term: 60, fit: 50, demand: 50);
        var longer = Preference(money: 79, term: 90, fit: 50, demand: 50);

        var comparison = PreferenceRanking.Compare(shorter, longer);

        // Three points of money is inside the band, so money says nothing and term decides. This is
        // the whole reason a weighted total was not used: there, three points would still have paid.
        Assert.Equal(-1, comparison.Sign);
        Assert.Equal(PreferenceFactorKind.Term, comparison.DecidedBy);
    }

    [Fact]
    public void Ranking_ReportsIndifferenceWhenNoFactorSeparatesTheOffers()
    {
        var left = Preference(money: 80, term: 70, fit: 60, demand: 50);
        var right = Preference(money: 78, term: 66, fit: 55, demand: 44);

        var comparison = PreferenceRanking.Compare(left, right);

        // Every gap is inside its own band. This is the only state a seeded draw is allowed to
        // resolve, so it has to be reachable by small differences and not only by exact ties.
        Assert.True(comparison.IsIndifferent);
        Assert.Null(comparison.DecidedBy);
    }

    [Fact]
    public void Ranking_IsSymmetricAboutIndifference()
    {
        var left = Preference(money: 80, term: 70, fit: 60, demand: 50);
        var right = Preference(money: 78, term: 66, fit: 55, demand: 44);

        // If A cannot tell B apart from itself, B must not be able to tell A apart either, or the
        // comparison is not an ordering at all and the ranking depends on which way round it is asked.
        Assert.True(PreferenceRanking.Compare(left, right).IsIndifferent);
        Assert.True(PreferenceRanking.Compare(right, left).IsIndifferent);
    }

    [Fact]
    public void Ranking_WeighsTheFactorsInTheStatedOrder()
    {
        Assert.Equal(
            [
                PreferenceFactorKind.Money,
                PreferenceFactorKind.Term,
                PreferenceFactorKind.TeamFit,
                PreferenceFactorKind.MarketDemand,
            ],
            PreferenceRanking.FactorOrder);
    }

    private static OfferPreference Preference(int money, int term, int fit, int demand) =>
        new(
            new OfferId("OFFER-1"),
            new TeamId("TEAM-A"),
            [
                new PreferenceContribution(PreferenceFactorKind.Money, money, 5, "test.money", $"Money reads {money}."),
                new PreferenceContribution(PreferenceFactorKind.Term, term, 10, "test.term", $"Term reads {term}."),
                new PreferenceContribution(PreferenceFactorKind.TeamFit, fit, 10, "test.fit", $"Fit reads {fit}."),
                new PreferenceContribution(PreferenceFactorKind.MarketDemand, demand, 15, "test.demand", $"Demand reads {demand}."),
            ],
            true,
            "test.reservation",
            "Clears the asking price.");
}
