using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

/// <summary>
/// The negotiation aggregate: what may happen when, what is on the table, and what the history keeps.
/// </summary>
public sealed class NegotiationTests
{
    private static readonly PlayerId FreeAgent = new("PLAYER-FREE");
    private static readonly Season Current = new(2031);

    [Fact]
    public void Negotiation_OpensWithItsOwnFirstHistoryEntry()
    {
        var negotiation = Open();

        Assert.Equal(NegotiationState.Open, negotiation.State);
        Assert.Equal(NegotiationEventKind.Opened, negotiation.History.Single().Kind);
        Assert.Null(negotiation.AcceptedOfferId);
    }

    [Fact]
    public void Negotiation_KeepsASupersededOfferInTheHistoryButNotOnTheTable()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-2", 14_000_000), new SeasonDay(1));

        Assert.Equal(2, negotiation.AllTeamOffers().Count);

        // One team, one live offer: the later one supersedes the earlier rather than amending it, so
        // the sequence of what was asked and refused survives.
        var live = negotiation.LiveOffersOn(new SeasonDay(1), expiryDays: null);
        Assert.Equal("OFFER-2", Assert.Single(live).Id.Value);
    }

    [Fact]
    public void Negotiation_TreatsACounterofferAsAnOfferInTheHistoryRatherThanAStateChange()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));

        var countered = negotiation.Counter(Offer("TEAM-A", "COUNTER-1", 16_000_000), new OfferId("OFFER-1"), new SeasonDay(1));

        Assert.True(countered.IsSuccess);
        Assert.Equal(NegotiationState.Open, negotiation.State);

        var counter = Assert.Single(negotiation.Counteroffers());
        Assert.Equal(NegotiationParty.Player, counter.Author);
        Assert.Equal(new OfferId("OFFER-1"), counter.InResponseTo);

        // A counter is not on the table: it is what the player wants, not what anyone has offered.
        Assert.Equal("OFFER-1", Assert.Single(negotiation.LiveOffersOn(new SeasonDay(1), null)).Id.Value);
    }

    [Fact]
    public void Negotiation_RefusesACounterAddressedToADifferentTeamThanTheOfferItAnswers()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));

        var result = negotiation.Counter(Offer("TEAM-B", "COUNTER-1", 16_000_000), new OfferId("OFFER-1"), new SeasonDay(1));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.counter_addresses_another_team");
    }

    [Fact]
    public void Negotiation_TakesAWithdrawnOfferOffTheTable()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));
        negotiation.WithdrawOffer(new OfferId("OFFER-1"), new SeasonDay(1));

        Assert.Empty(negotiation.LiveOffersOn(new SeasonDay(1), null));
        Assert.True(negotiation.WithdrawOffer(new OfferId("OFFER-1"), new SeasonDay(2)).IsFailure);
    }

    [Fact]
    public void Negotiation_DropsAnOfferFromTheTableOnceItHasStoodLongerThanTheLeagueAllows()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));

        // Placed on day nought with a three-day expiry: live through day two, gone on day three.
        Assert.Single(negotiation.LiveOffersOn(new SeasonDay(2), expiryDays: 3));
        Assert.Empty(negotiation.LiveOffersOn(new SeasonDay(3), expiryDays: 3));

        // A league that sets no expiry never times anything out, which is a different rule from a
        // very long one and is expressed by the field being absent.
        Assert.Single(negotiation.LiveOffersOn(new SeasonDay(300), expiryDays: null));
    }

    [Fact]
    public void Negotiation_ReportsWhatHasExpiredWithoutTheQuestionExpiringIt()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));

        var before = negotiation.History.Count;
        Assert.Single(negotiation.OffersExpiringBy(new SeasonDay(3), expiryDays: 3));
        Assert.Equal(before, negotiation.History.Count);

        negotiation.RecordExpiry(new OfferId("OFFER-1"), new SeasonDay(3));

        Assert.Empty(negotiation.OffersExpiringBy(new SeasonDay(3), expiryDays: 3));
        Assert.Contains(negotiation.History, entry => entry.Kind == NegotiationEventKind.OfferExpired);
    }

    [Fact]
    public void Negotiation_RefusesAnythingRecordedBeforeItsOwnLastActivity()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(4));

        var result = negotiation.PlaceOffer(Offer("TEAM-B", "OFFER-2", 12_000_000), new SeasonDay(2));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.day_precedes_history");
    }

    [Fact]
    public void Negotiation_RefusesEverythingOnceItIsOver()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));
        negotiation.Resolve(new OfferId("OFFER-1"), new SeasonDay(1), "He took it.");
        negotiation.RecordSigned(new ContractId("CONTRACT-1"), new SeasonDay(1));

        Assert.Equal(NegotiationState.Signed, negotiation.State);
        Assert.True(negotiation.IsOver);
        Assert.True(negotiation.PlaceOffer(Offer("TEAM-B", "OFFER-2", 20_000_000), new SeasonDay(2)).IsFailure);
        Assert.True(negotiation.Close("Too late.", new SeasonDay(2)).IsFailure);
    }

    [Fact]
    public void Negotiation_ClosesWithNobodySignedWhenNothingIsAccepted()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));

        var resolved = negotiation.Resolve(null, new SeasonDay(1), "He turned it down.");

        Assert.True(resolved.IsSuccess);
        Assert.Equal(NegotiationState.Closed, negotiation.State);
        Assert.Null(negotiation.AcceptedOfferId);
    }

    [Fact]
    public void Negotiation_RefusesToAcceptAnOfferThatIsNoLongerOnTheTable()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));
        negotiation.WithdrawOffer(new OfferId("OFFER-1"), new SeasonDay(1));

        var result = negotiation.Resolve(new OfferId("OFFER-1"), new SeasonDay(1), "He took it.");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.offer_not_live");
    }

    [Fact]
    public void Negotiation_RefusesAnOfferForSomebodyElse()
    {
        var negotiation = Open();

        var other = Negotiations.Offer.Create(
            new OfferId("OFFER-1"),
            new TeamId("TEAM-A"),
            new PlayerId("PLAYER-OTHER"),
            [new ContractSeasonTerm(Current, new Money(10_000_000), new Money(10_000_000))]).Value;

        var result = negotiation.PlaceOffer(other, new SeasonDay(0));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.offer_for_another_player");
    }

    [Fact]
    public void Negotiation_RestoresToAnEarlierPointExactly()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));

        var state = negotiation.State;
        var historyCount = negotiation.History.Count;

        negotiation.Resolve(new OfferId("OFFER-1"), new SeasonDay(1), "He took it.");
        negotiation.RestoreTo(state, null, null, historyCount);

        Assert.Equal(NegotiationState.Open, negotiation.State);
        Assert.Equal(historyCount, negotiation.History.Count);
        Assert.Null(negotiation.AcceptedOfferId);

        // Restoring is not a rule-checked operation, so a restore point that never existed is a
        // programming error rather than a refusal a caller could sensibly handle.
        Assert.Throws<ArgumentOutOfRangeException>(() => negotiation.RestoreTo(state, null, null, 99));
    }

    [Fact]
    public void Negotiation_OrdersWhatIsOnTheTableByTheStatedKeyRatherThanByArrival()
    {
        var negotiation = Open();
        negotiation.PlaceOffer(Offer("TEAM-C", "OFFER-3", 10_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-1", 10_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(Offer("TEAM-B", "OFFER-2", 10_000_000), new SeasonDay(0));

        var live = negotiation.LiveOffersOn(new SeasonDay(0), null);

        Assert.Equal(["TEAM-A", "TEAM-B", "TEAM-C"], live.Select(offer => offer.TeamId.Value));
    }

    private static Negotiation Open() =>
        Negotiation.Open(new NegotiationId("NEGOTIATION-1"), FreeAgent, SeasonDay.Opening).Value;

    private static Offer Offer(string teamId, string offerId, long compensation) =>
        Domain.Negotiations.Offer.Create(
            new OfferId(offerId),
            new TeamId(teamId),
            FreeAgent,
            [new ContractSeasonTerm(Current, new Money(compensation), new Money(compensation))]).Value;
}
