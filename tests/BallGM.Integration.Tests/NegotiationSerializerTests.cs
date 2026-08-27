using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Negotiations;

namespace BallGM.Integration.Tests;

/// <summary>
/// The save round trip for a negotiation that is still running. A free agency half-finished when a
/// player closes the game is the state this format exists for, so the tests are about what survives
/// the trip and about what a bad file is refused for.
/// </summary>
public sealed class NegotiationSerializerTests
{
    private static readonly PlayerId FreeAgent = new("PLAYER-FREE");
    private static readonly Season Current = new(2031);

    private readonly NegotiationSerializer _serializer = new();

    [Fact]
    public void Negotiation_SurvivesASaveAndLoadWithItsWholeHistory()
    {
        var negotiation = InFlight();

        var restored = _serializer.Deserialize(_serializer.Serialize(negotiation));

        Assert.True(restored.IsSuccess);
        var loaded = restored.Value;

        Assert.Equal(negotiation.Id, loaded.Id);
        Assert.Equal(negotiation.PlayerId, loaded.PlayerId);
        Assert.Equal(negotiation.State, loaded.State);
        Assert.Equal(negotiation.OpenedOn, loaded.OpenedOn);

        // The history is the negotiation, so every line of it has to come back — including the
        // superseded offer and the counter, which is what an AI front office reads back from.
        Assert.Equal(
            negotiation.History.Select(entry => (entry.Sequence, entry.Kind, entry.Day, entry.Author, entry.Narrative)),
            loaded.History.Select(entry => (entry.Sequence, entry.Kind, entry.Day, entry.Author, entry.Narrative)));

        Assert.Equal(
            negotiation.LiveOffersOn(new SeasonDay(2), expiryDays: 3).Select(offer => offer.Id.Value),
            loaded.LiveOffersOn(new SeasonDay(2), expiryDays: 3).Select(offer => offer.Id.Value));

        Assert.Equal(negotiation.Counteroffers().Count, loaded.Counteroffers().Count);
    }

    [Fact]
    public void Negotiation_SurvivesASaveAndLoadAfterItHasResolvedAndSigned()
    {
        var negotiation = InFlight();
        negotiation.Resolve(new OfferId("OFFER-A2"), new SeasonDay(3), "He took the improved offer.");
        negotiation.RecordSigned(new ContractId("CONTRACT-1"), new SeasonDay(3));

        var loaded = _serializer.Deserialize(_serializer.Serialize(negotiation));

        Assert.True(loaded.IsSuccess);
        Assert.Equal(NegotiationState.Signed, loaded.Value.State);
        Assert.Equal(new OfferId("OFFER-A2"), loaded.Value.AcceptedOfferId);
        Assert.Equal(new ContractId("CONTRACT-1"), loaded.Value.SignedContractId);
    }

    [Fact]
    public void Negotiation_KeepsTheOfferedTermsExactlyAsTheyWere()
    {
        var negotiation = InFlight();

        var loaded = _serializer.Deserialize(_serializer.Serialize(negotiation)).Value;
        var offer = loaded.FindOffer(new OfferId("OFFER-A2"))!;

        Assert.Equal(3, offer.SeasonCount);
        Assert.Equal(21_000_000, offer.FirstSeasonCompensation.SmallestUnits);
        Assert.True(offer.IsFullyGuaranteed);
        Assert.Equal(Current, offer.FirstSeason);
    }

    [Fact]
    public void Negotiation_RefusesASaveWrittenByAnotherSchemaVersion()
    {
        var json = _serializer.Serialize(InFlight())
            .Replace($"\"schemaVersion\": {NegotiationEnvelope.CurrentSchemaVersion}", "\"schemaVersion\": 99", StringComparison.Ordinal);

        var result = _serializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.unsupported_schema_version");
    }

    [Fact]
    public void Negotiation_RefusesASaveCarryingFieldsThisBuildDoesNotKnow()
    {
        // Structural refusal rather than a silent drop: a file from a later build carries half a
        // negotiation this build has never heard of, and reading the half it recognises would load a
        // market that is not the one the file describes.
        var json = _serializer.Serialize(InFlight())
            .Replace("\"playerId\":", "\"moraleRating\": 42,\n  \"playerId\":", StringComparison.Ordinal);

        var result = _serializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.malformed_file");
    }

    [Fact]
    public void Negotiation_RefusesASaveWhoseHistoryCouldNotHaveHappened()
    {
        // The file claims the player countered an offer nobody ever made. Loading replays the
        // history through the aggregate, so this is refused by the same rule that would have refused
        // it live rather than loaded into a state no live negotiation could reach.
        var json = _serializer.Serialize(InFlight())
            .Replace(
                "\"inResponseToOfferId\": \"OFFER-A1\"",
                "\"inResponseToOfferId\": \"OFFER-NEVER-MADE\"",
                StringComparison.Ordinal);

        var result = _serializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.history_could_not_be_replayed");
    }

    [Fact]
    public void Negotiation_RefusesASaveThatClaimsAnOutcomeItsOwnHistoryDoesNotSupport()
    {
        var json = _serializer.Serialize(InFlight())
            .Replace("\"state\": \"Open\"", "\"state\": \"Signed\"", StringComparison.Ordinal);

        var result = _serializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.state_does_not_match_history");
    }

    [Fact]
    public void Negotiation_RefusesAPayloadThatIsNotJson()
    {
        var result = _serializer.Deserialize("{ not json");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation.malformed_file");
    }

    /// <summary>
    /// A market genuinely in flight: two teams bidding, one of them having improved on itself, one
    /// offer withdrawn, and a counter from the player still unanswered.
    /// </summary>
    private static Negotiation InFlight()
    {
        var negotiation = Negotiation.Open(new NegotiationId("NEGOTIATION-1"), FreeAgent, SeasonDay.Opening).Value;

        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-A1", 18_000_000, 2), new SeasonDay(0));
        negotiation.PlaceOffer(Offer("TEAM-B", "OFFER-B1", 20_000_000, 4), new SeasonDay(0));
        negotiation.Counter(Offer("TEAM-A", "COUNTER-A1", 24_000_000, 3), new OfferId("OFFER-A1"), new SeasonDay(1));
        negotiation.PlaceOffer(Offer("TEAM-A", "OFFER-A2", 21_000_000, 3), new SeasonDay(2));
        negotiation.WithdrawOffer(new OfferId("OFFER-B1"), new SeasonDay(2));

        return negotiation;
    }

    private static Offer Offer(string teamId, string offerId, long compensation, int seasons) =>
        Domain.Negotiations.Offer.Create(
            new OfferId(offerId),
            new TeamId(teamId),
            FreeAgent,
            Enumerable.Range(0, seasons).Select(index => new ContractSeasonTerm(
                new Season(Current.Year + index),
                new Money(compensation),
                new Money(compensation)))).Value;
}
