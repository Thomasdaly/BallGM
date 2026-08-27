using System.Text.Json;
using System.Text.Json.Serialization;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Contracts;

namespace BallGM.Infrastructure.Negotiations;

/// <summary>
/// Reads and writes an in-flight negotiation as versioned JSON. Like every other serializer here it
/// never throws on bad content: a save is untrusted input the moment it is a file on a disk, so a
/// malformed or impossible negotiation produces a structured failure a loader can explain.
/// <para>
/// Loading <em>replays</em> the history through the aggregate rather than assigning its fields. A
/// save that claims a team withdrew an offer it never made, or that a market resolved on an offer
/// that had expired, is refused by the same rule that would have refused it live — and the state the
/// file declares is checked against the state the replay actually reached, so a file cannot assert an
/// outcome its own history does not support.
/// </para>
/// </summary>
public sealed class NegotiationSerializer
{
    private const string MalformedFileCode = "negotiation.malformed_file";
    private const string UnsupportedSchemaVersionCode = "negotiation.unsupported_schema_version";
    private const string InvalidFieldCode = "negotiation.invalid_field";
    private const string ImpossibleHistoryCode = "negotiation.history_could_not_be_replayed";
    private const string StateMismatchCode = "negotiation.state_does_not_match_history";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,

        // Not every entry carries an offer or a team, and writing those back as explicit nulls would
        // read as a third state nobody defined. Same bargain the ruleset serializer strikes.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // A file from a later build fails structurally rather than silently dropping the half of a
        // negotiation this build has never heard of. The permanent half of the versioning fix, which
        // does not depend on anyone remembering to move a constant.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string Serialize(Negotiation negotiation)
    {
        ArgumentNullException.ThrowIfNull(negotiation);

        var envelope = new NegotiationEnvelope(
            NegotiationEnvelope.CurrentSchemaVersion,
            negotiation.Id.Value,
            negotiation.PlayerId.Value,
            negotiation.OpenedOn.Index,
            negotiation.State.ToString(),
            negotiation.AcceptedOfferId?.Value,
            negotiation.SignedContractId?.Value,
            negotiation.History.Select(ToEnvelope).ToList());

        return JsonSerializer.Serialize(envelope, Options);
    }

    public DomainOperationResult<Negotiation> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        NegotiationEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<NegotiationEnvelope>(json, Options);
        }
        catch (JsonException exception)
        {
            return Fail(MalformedFileCode, $"The negotiation is not valid JSON: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail(MalformedFileCode, "The negotiation payload did not contain a negotiation.");
        }

        if (envelope.SchemaVersion != NegotiationEnvelope.CurrentSchemaVersion)
        {
            return Fail(
                UnsupportedSchemaVersionCode,
                $"Negotiation schema version {envelope.SchemaVersion} cannot be read by this build, which reads version {NegotiationEnvelope.CurrentSchemaVersion}.");
        }

        if (envelope.History is null || envelope.History.Count == 0)
        {
            return Fail(MalformedFileCode, "The negotiation declares no history. Every negotiation records the day it opened.");
        }

        if (!Enum.TryParse<NegotiationState>(envelope.State, out var declaredState))
        {
            return Fail(InvalidFieldCode, $"'{envelope.State}' is not a negotiation state this build knows.");
        }

        try
        {
            var openedResult = Negotiation.Open(
                new NegotiationId(envelope.NegotiationId),
                new PlayerId(envelope.PlayerId),
                new SeasonDay(envelope.OpenedOnDay));

            if (openedResult.IsFailure)
            {
                return openedResult;
            }

            var negotiation = openedResult.Value;

            // The opening entry is written by Open itself, so replay starts after it. A file whose
            // first entry is anything else did not come from a negotiation.
            if (envelope.History[0].Kind != NegotiationEventKind.Opened.ToString())
            {
                return Fail(
                    InvalidFieldCode,
                    $"The first entry of a negotiation is always '{NegotiationEventKind.Opened}', but this one is '{envelope.History[0].Kind}'.");
            }

            foreach (var entry in envelope.History.Skip(1))
            {
                var replayed = Replay(negotiation, entry, envelope);
                if (replayed.IsFailure)
                {
                    return DomainOperationResult<Negotiation>.Failure(replayed.Errors
                        .Prepend(new DomainError(
                            ImpossibleHistoryCode,
                            $"Entry {entry.Sequence} ('{entry.Kind}' on day {entry.Day}) is not something that could have happened in this negotiation."))
                        .ToArray());
                }
            }

            if (negotiation.State != declaredState)
            {
                return Fail(
                    StateMismatchCode,
                    $"This save declares the negotiation is {declaredState}, but replaying its own history leaves it {negotiation.State}.");
            }

            return DomainOperationResult<Negotiation>.Success(negotiation);
        }
        catch (ArgumentException exception)
        {
            return Fail(InvalidFieldCode, exception.Message);
        }
    }

    private static DomainOperationResult Replay(
        Negotiation negotiation,
        NegotiationEntryEnvelope entry,
        NegotiationEnvelope envelope)
    {
        var day = new SeasonDay(entry.Day);

        if (!Enum.TryParse<NegotiationEventKind>(entry.Kind, out var kind))
        {
            return DomainOperationResult.Failure(new DomainError(
                InvalidFieldCode,
                $"'{entry.Kind}' is not a negotiation event this build knows."));
        }

        switch (kind)
        {
            case NegotiationEventKind.OfferPlaced:
                {
                    var offer = ToOffer(entry.Offer);
                    return offer.IsFailure
                        ? DomainOperationResult.Failure(offer.Errors.ToArray())
                        : negotiation.PlaceOffer(offer.Value, day);
                }

            case NegotiationEventKind.Counteroffer:
                {
                    if (entry.InResponseToOfferId is null)
                    {
                        return DomainOperationResult.Failure(new DomainError(
                            InvalidFieldCode,
                            "A counteroffer has to name the offer it answers."));
                    }

                    var counter = ToOffer(entry.Offer);
                    return counter.IsFailure
                        ? DomainOperationResult.Failure(counter.Errors.ToArray())
                        : negotiation.Counter(counter.Value, new OfferId(entry.InResponseToOfferId), day);
                }

            case NegotiationEventKind.OfferWithdrawn:
                return entry.Offer is null
                    ? MissingOffer(kind)
                    : negotiation.WithdrawOffer(new OfferId(entry.Offer.OfferId), day);

            case NegotiationEventKind.OfferExpired:
                return entry.Offer is null
                    ? MissingOffer(kind)
                    : negotiation.RecordExpiry(new OfferId(entry.Offer.OfferId), day);

            case NegotiationEventKind.MarketResolved:
                return negotiation.Resolve(
                    entry.Offer is null ? null : new OfferId(entry.Offer.OfferId),
                    day,
                    entry.Narrative);

            case NegotiationEventKind.ContractSigned:
                return envelope.SignedContractId is null
                    ? DomainOperationResult.Failure(new DomainError(
                        InvalidFieldCode,
                        "This negotiation records a signing but names no contract."))
                    : negotiation.RecordSigned(new ContractId(envelope.SignedContractId), day);

            case NegotiationEventKind.Closed:
                return negotiation.Close(entry.Narrative, day);

            case NegotiationEventKind.Opened:
                return DomainOperationResult.Failure(new DomainError(
                    InvalidFieldCode,
                    "A negotiation opens once. A second opening entry is a file two saves were merged into."));

            default:
                return DomainOperationResult.Failure(new DomainError(
                    InvalidFieldCode,
                    $"'{kind}' is not an event this build knows how to replay."));
        }
    }

    private static DomainOperationResult MissingOffer(NegotiationEventKind kind) =>
        DomainOperationResult.Failure(new DomainError(
            InvalidFieldCode,
            $"A '{kind}' entry has to name the offer it is about."));

    private static NegotiationEntryEnvelope ToEnvelope(NegotiationEntry entry) =>
        new(
            entry.Sequence,
            entry.Kind.ToString(),
            entry.Day.Index,
            entry.Author.ToString(),
            entry.TeamId?.Value,
            entry.InResponseTo?.Value,
            entry.Offer is null ? null : ToEnvelope(entry.Offer),
            entry.Narrative);

    private static OfferEnvelope ToEnvelope(Offer offer) =>
        new(
            offer.Id.Value,
            offer.TeamId.Value,
            offer.PlayerId.Value,
            offer.Terms
                .Select(term => new ContractSeasonTermEnvelope(
                    term.Season.Year,
                    term.Compensation.SmallestUnits,
                    term.GuaranteedAmount.SmallestUnits,
                    term.Option.ToString(),
                    term.OptionStatus.ToString()))
                .ToList());

    private static DomainOperationResult<Offer> ToOffer(OfferEnvelope? envelope)
    {
        if (envelope is null)
        {
            return DomainOperationResult<Offer>.Failure(new DomainError(
                InvalidFieldCode,
                "An entry that puts terms on the table has to carry them."));
        }

        if (envelope.Seasons is null || envelope.Seasons.Count == 0)
        {
            return DomainOperationResult<Offer>.Failure(new DomainError(
                InvalidFieldCode,
                $"Offer '{envelope.OfferId}' declares no seasons."));
        }

        var terms = new List<ContractSeasonTerm>(envelope.Seasons.Count);
        foreach (var season in envelope.Seasons)
        {
            if (!Enum.TryParse<ContractOptionKind>(season.Option, out var option))
            {
                return DomainOperationResult<Offer>.Failure(new DomainError(
                    InvalidFieldCode,
                    $"Season {season.SeasonYear} of offer '{envelope.OfferId}' declares an unknown contract option '{season.Option}'."));
            }

            if (!Enum.TryParse<ContractOptionStatus>(season.OptionStatus, out var status))
            {
                return DomainOperationResult<Offer>.Failure(new DomainError(
                    InvalidFieldCode,
                    $"Season {season.SeasonYear} of offer '{envelope.OfferId}' declares an unknown option status '{season.OptionStatus}'."));
            }

            terms.Add(new ContractSeasonTerm(
                new Season(season.SeasonYear),
                new Money(season.Compensation),
                new Money(season.GuaranteedAmount),
                option,
                status));
        }

        return Offer.Create(
            new OfferId(envelope.OfferId),
            new TeamId(envelope.TeamId),
            new PlayerId(envelope.PlayerId),
            terms);
    }

    private static DomainOperationResult<Negotiation> Fail(string code, string message) =>
        DomainOperationResult<Negotiation>.Failure(new DomainError(code, message));
}
