using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

/// <summary>
/// The offer value object. An offer is a proposed contract, so it has to fail every shape check the
/// contract it would become fails — an offer that passes a check the contract cannot is an offer
/// nobody could accept, which is a worse bug than one refused early.
/// </summary>
public sealed class OfferTests
{
    private static readonly TeamId Team = new("TEAM-1");
    private static readonly PlayerId Player = new("PLAYER-1");

    [Fact]
    public void AnOfferReportsItsOwnTotalsRatherThanMakingTheCallerAddThemUp()
    {
        var offer = Build((2031, 10_000_000, 10_000_000), (2032, 11_000_000, 5_500_000));

        Assert.Equal(2, offer.SeasonCount);
        Assert.Equal(2031, offer.FirstSeason.Year);
        Assert.Equal(2032, offer.LastSeason.Year);
        Assert.Equal(10_000_000, offer.FirstSeasonCompensation.SmallestUnits);
        Assert.Equal(21_000_000, offer.TotalCompensation.SmallestUnits);
        Assert.Equal(15_500_000, offer.TotalGuaranteed.SmallestUnits);
        Assert.False(offer.IsFullyGuaranteed);
    }

    [Fact]
    public void SeasonsAreOrderedRegardlessOfHowTheyArrive()
    {
        var offer = Build((2033, 12_000_000, 12_000_000), (2031, 10_000_000, 10_000_000), (2032, 11_000_000, 11_000_000));

        Assert.Equal([2031, 2032, 2033], offer.Terms.Select(term => term.Season.Year));
        Assert.True(offer.IsFullyGuaranteed);
    }

    [Fact]
    public void AnOfferWithAGapInItsSeasonsIsRefused()
    {
        var result = Create((2031, 10_000_000, 10_000_000), (2033, 10_000_000, 10_000_000));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.seasons_not_contiguous", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AnOfferGuaranteeingMoreThanItPaysIsRefused()
    {
        var result = Create((2031, 10_000_000, 12_000_000));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.guarantee_exceeds_compensation", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AnOfferCoveringNoSeasonsIsRefused()
    {
        var result = Offer.Create(new OfferId("OFFER-1"), Team, Player, []);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.no_seasons", Assert.Single(result.Errors).Code);
    }

    /// <summary>An offer that pays nothing in a season it covers is not an offer for that season.</summary>
    [Fact]
    public void AnOfferPayingNothingInASeasonIsRefused()
    {
        var result = Create((2031, 10_000_000, 10_000_000), (2032, 0, 0));

        Assert.True(result.IsFailure);
        Assert.Equal("offer.non_positive_compensation", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// The seasons an offer carries are the seasons the contract carries. Rebuilding the run from
    /// parts at acceptance would let what the player agreed to and what the contract says drift.
    /// </summary>
    [Fact]
    public void TheContractTermsAreTheOfferTerms()
    {
        var offer = Build((2031, 10_000_000, 10_000_000), (2032, 11_000_000, 11_000_000));

        Assert.Equal(offer.Terms, offer.ToContractTerms());
    }

    private static Offer Build(params (int Year, long Compensation, long Guaranteed)[] seasons) =>
        Create(seasons).Value;

    private static DomainOperationResult<Offer> Create(params (int Year, long Compensation, long Guaranteed)[] seasons) =>
        Offer.Create(
            new OfferId("OFFER-1"),
            Team,
            Player,
            seasons.Select(season => new ContractSeasonTerm(
                new Season(season.Year),
                new Money(season.Compensation),
                new Money(season.Guaranteed))));
}
