using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Negotiations;

/// <summary>
/// One free agent's market: every offer made to them, every counter they made back, and where the
/// whole thing has got to. The aggregate that Milestone 6a deliberately did without — one team
/// offering one uncontested player needs no history, and a market does.
/// <para>
/// Two things it is careful not to be. It is not a mutable "current offer": offers are appended and
/// superseded, never edited, so the sequence of what was asked and refused survives. And it holds no
/// opinion about whether an offer is <em>good</em> — that is <see cref="OfferPreference"/>, decided
/// by the rules layer against the league, and an aggregate that scored offers would be a rulebook
/// hiding inside a data structure.
/// </para>
/// <para>
/// Expiry is a <em>query</em>, not a field that goes stale. <see cref="LiveOffersOn"/> answers what
/// stands on a given day for a given league's expiry rule; recording that an offer expired is a
/// separate, explicit act, so an assessment can ask the question without changing the answer.
/// </para>
/// </summary>
public sealed class Negotiation
{
    private const string NotOpenCode = "negotiation.not_open";
    private const string NotResolvedCode = "negotiation.not_resolved";
    private const string AlreadyOverCode = "negotiation.already_over";
    private const string WrongPlayerCode = "negotiation.offer_for_another_player";
    private const string TimeRunsBackwardsCode = "negotiation.day_precedes_history";
    private const string UnknownOfferCode = "negotiation.unknown_offer";
    private const string OfferNotLiveCode = "negotiation.offer_not_live";
    private const string CounterTeamMismatchCode = "negotiation.counter_addresses_another_team";
    private const string AcceptedOfferUnknownCode = "negotiation.accepted_offer_not_on_the_table";

    private readonly List<NegotiationEntry> _history = [];

    private Negotiation(NegotiationId id, PlayerId playerId, SeasonDay openedOn)
    {
        Id = id;
        PlayerId = playerId;
        OpenedOn = openedOn;
        State = NegotiationState.Open;
    }

    public NegotiationId Id { get; }

    public PlayerId PlayerId { get; }

    public SeasonDay OpenedOn { get; }

    public NegotiationState State { get; private set; }

    /// <summary>The offer the player took, once the market has resolved on one.</summary>
    public OfferId? AcceptedOfferId { get; private set; }

    /// <summary>The contract the accepted offer became, once it has been executed.</summary>
    public ContractId? SignedContractId { get; private set; }

    public IReadOnlyList<NegotiationEntry> History => _history.AsReadOnly();

    public bool IsOpen => State == NegotiationState.Open;

    public bool IsOver => State is NegotiationState.Signed or NegotiationState.Closed;

    /// <summary>The most recent day anything happened on. Nothing may be recorded before it.</summary>
    public SeasonDay LastActivityOn => _history.Count == 0 ? OpenedOn : _history[^1].Day;

    /// <summary>Opens a negotiation. Structural nulls throw; there is no business rule to break yet.</summary>
    public static DomainOperationResult<Negotiation> Open(NegotiationId id, PlayerId playerId, SeasonDay openedOn)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(openedOn);

        var negotiation = new Negotiation(id, playerId, openedOn);
        negotiation.Append(
            NegotiationEventKind.Opened,
            openedOn,
            NegotiationParty.Player,
            teamId: null,
            offer: null,
            inResponseTo: null,
            "The player reached the market.");

        return DomainOperationResult<Negotiation>.Success(negotiation);
    }

    /// <summary>
    /// Puts a team's terms on the table. A second offer from the same team supersedes its first
    /// rather than replacing it in the record — both stay in the history, and only the later one is
    /// live.
    /// </summary>
    public DomainOperationResult PlaceOffer(Offer offer, SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(day);

        var guard = GuardOpen(day);
        if (guard.IsFailure)
        {
            return guard;
        }

        if (offer.PlayerId != PlayerId)
        {
            return Fail(
                WrongPlayerCode,
                $"This offer is for player '{offer.PlayerId.Value}' and cannot join the negotiation over player '{PlayerId.Value}'.");
        }

        var superseded = LatestTeamOffer(offer.TeamId);

        Append(
            NegotiationEventKind.OfferPlaced,
            day,
            NegotiationParty.Team,
            offer.TeamId,
            offer,
            inResponseTo: null,
            superseded is null
                ? $"Team '{offer.TeamId.Value}' offered {offer.SeasonCount} season(s), {offer.FirstSeasonCompensation.SmallestUnits} in the first."
                : $"Team '{offer.TeamId.Value}' improved on its own offer: {offer.SeasonCount} season(s), {offer.FirstSeasonCompensation.SmallestUnits} in the first.");

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Records what the player would rather have, addressed at one team. A new offer in the history
    /// authored by the player, not a state transition: the negotiation stays open, nothing is
    /// accepted, and the team answers a counter by placing its next offer.
    /// </summary>
    public DomainOperationResult Counter(Offer counteroffer, OfferId inResponseTo, SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(counteroffer);
        ArgumentNullException.ThrowIfNull(inResponseTo);
        ArgumentNullException.ThrowIfNull(day);

        var guard = GuardOpen(day);
        if (guard.IsFailure)
        {
            return guard;
        }

        if (counteroffer.PlayerId != PlayerId)
        {
            return Fail(
                WrongPlayerCode,
                $"This counteroffer is for player '{counteroffer.PlayerId.Value}' and cannot join the negotiation over player '{PlayerId.Value}'.");
        }

        var answered = FindOffer(inResponseTo);
        if (answered is null)
        {
            return Fail(
                UnknownOfferCode,
                $"Offer '{inResponseTo.Value}' is not part of this negotiation, so there is nothing to counter.");
        }

        if (answered.TeamId != counteroffer.TeamId)
        {
            return Fail(
                CounterTeamMismatchCode,
                $"This counteroffer is addressed to team '{counteroffer.TeamId.Value}' but answers an offer from team '{answered.TeamId.Value}'.");
        }

        Append(
            NegotiationEventKind.Counteroffer,
            day,
            NegotiationParty.Player,
            counteroffer.TeamId,
            counteroffer,
            inResponseTo,
            $"The player countered team '{counteroffer.TeamId.Value}': {counteroffer.SeasonCount} season(s), {counteroffer.FirstSeasonCompensation.SmallestUnits} in the first.");

        return DomainOperationResult.Success;
    }

    /// <summary>Takes a team's live offer off the table.</summary>
    public DomainOperationResult WithdrawOffer(OfferId offerId, SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(offerId);
        ArgumentNullException.ThrowIfNull(day);

        var guard = GuardOpen(day);
        if (guard.IsFailure)
        {
            return guard;
        }

        var offer = FindOffer(offerId);
        if (offer is null)
        {
            return Fail(UnknownOfferCode, $"Offer '{offerId.Value}' is not part of this negotiation.");
        }

        if (IsClosedOut(offerId))
        {
            return Fail(
                OfferNotLiveCode,
                $"Offer '{offerId.Value}' has already been withdrawn or has expired, so there is nothing to take back.");
        }

        Append(
            NegotiationEventKind.OfferWithdrawn,
            day,
            NegotiationParty.Team,
            offer.TeamId,
            offer,
            inResponseTo: null,
            $"Team '{offer.TeamId.Value}' withdrew its offer.");

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Records that an offer stood too long. Separate from <see cref="LiveOffersOn"/> on purpose: an
    /// assessment has to be able to ask what has expired without that question being what expires it.
    /// </summary>
    public DomainOperationResult RecordExpiry(OfferId offerId, SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(offerId);
        ArgumentNullException.ThrowIfNull(day);

        var guard = GuardOpen(day);
        if (guard.IsFailure)
        {
            return guard;
        }

        var offer = FindOffer(offerId);
        if (offer is null)
        {
            return Fail(UnknownOfferCode, $"Offer '{offerId.Value}' is not part of this negotiation.");
        }

        if (IsClosedOut(offerId))
        {
            return Fail(
                OfferNotLiveCode,
                $"Offer '{offerId.Value}' was already withdrawn or expired, so it cannot expire again.");
        }

        Append(
            NegotiationEventKind.OfferExpired,
            day,
            NegotiationParty.Team,
            offer.TeamId,
            offer,
            inResponseTo: null,
            $"Team '{offer.TeamId.Value}'s offer expired.");

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Closes the bidding on one answer. <paramref name="acceptedOfferId"/> of <c>null</c> is a real
    /// outcome and not a failure: a market where nothing on the table clears what the player will
    /// accept resolves with nobody signed.
    /// </summary>
    public DomainOperationResult Resolve(OfferId? acceptedOfferId, SeasonDay day, string narrative)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentException.ThrowIfNullOrWhiteSpace(narrative);

        var guard = GuardOpen(day);
        if (guard.IsFailure)
        {
            return guard;
        }

        Offer? accepted = null;
        if (acceptedOfferId is not null)
        {
            accepted = FindOffer(acceptedOfferId);
            if (accepted is null)
            {
                return Fail(
                    AcceptedOfferUnknownCode,
                    $"Offer '{acceptedOfferId.Value}' cannot be accepted because it is not part of this negotiation.");
            }

            if (IsClosedOut(acceptedOfferId))
            {
                return Fail(
                    OfferNotLiveCode,
                    $"Offer '{acceptedOfferId.Value}' was withdrawn or expired and cannot be accepted.");
            }
        }

        State = acceptedOfferId is null ? NegotiationState.Closed : NegotiationState.Resolved;
        AcceptedOfferId = acceptedOfferId;

        Append(
            NegotiationEventKind.MarketResolved,
            day,
            NegotiationParty.Player,
            accepted?.TeamId,
            accepted,
            inResponseTo: null,
            narrative);

        return DomainOperationResult.Success;
    }

    /// <summary>Records that the accepted offer became a contract. The last thing that happens.</summary>
    public DomainOperationResult RecordSigned(ContractId contractId, SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(contractId);
        ArgumentNullException.ThrowIfNull(day);

        if (State != NegotiationState.Resolved)
        {
            return Fail(
                NotResolvedCode,
                $"This negotiation is {State} and no offer has been accepted, so there is nothing to sign.");
        }

        if (day < LastActivityOn)
        {
            return Fail(TimeRunsBackwardsCode, DayMessage(day));
        }

        State = NegotiationState.Signed;
        SignedContractId = contractId;

        var accepted = AcceptedOfferId is null ? null : FindOffer(AcceptedOfferId);

        Append(
            NegotiationEventKind.ContractSigned,
            day,
            NegotiationParty.Team,
            accepted?.TeamId,
            accepted,
            inResponseTo: null,
            $"The accepted offer became contract '{contractId.Value}'.");

        return DomainOperationResult.Success;
    }

    /// <summary>Ends the negotiation with nobody signed.</summary>
    public DomainOperationResult Close(string reason, SeasonDay day)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(day);

        if (IsOver)
        {
            return Fail(AlreadyOverCode, $"This negotiation is already {State} and cannot be closed again.");
        }

        if (day < LastActivityOn)
        {
            return Fail(TimeRunsBackwardsCode, DayMessage(day));
        }

        State = NegotiationState.Closed;
        AcceptedOfferId = null;

        Append(
            NegotiationEventKind.Closed,
            day,
            NegotiationParty.Player,
            teamId: null,
            offer: null,
            inResponseTo: null,
            reason);

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Every offer any team has made, oldest first, superseded ones included. What a front office
    /// reads back; not what is on the table.
    /// </summary>
    public IReadOnlyList<Offer> AllTeamOffers() =>
        _history
            .Where(entry => entry.Kind == NegotiationEventKind.OfferPlaced && entry.Offer is not null)
            .Select(entry => entry.Offer!)
            .ToList();

    /// <summary>Every counteroffer the player has made, oldest first.</summary>
    public IReadOnlyList<NegotiationEntry> Counteroffers() =>
        _history.Where(entry => entry.Kind == NegotiationEventKind.Counteroffer).ToList();

    /// <summary>The player's most recent counter to one team, if they have made one.</summary>
    public NegotiationEntry? LatestCounterTo(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);

        return _history
            .Where(entry => entry.Kind == NegotiationEventKind.Counteroffer && entry.TeamId == teamId)
            .LastOrDefault();
    }

    /// <summary>
    /// What actually stands on <paramref name="asOf"/>: one offer per team at most — the latest that
    /// team placed — with withdrawn, expired, and timed-out offers left out.
    /// <paramref name="expiryDays"/> of <c>null</c> is a league where offers do not expire, so the
    /// day is only asked about for offers placed at all.
    /// <para>
    /// Ordered by the market's stated key — team identifier then offer identifier, both ordinal
    /// ascending — rather than by arrival, so that what a resolution point iterates does not depend
    /// on the order a UI happened to submit things in.
    /// </para>
    /// </summary>
    public IReadOnlyList<Offer> LiveOffersOn(SeasonDay asOf, int? expiryDays)
    {
        ArgumentNullException.ThrowIfNull(asOf);

        var latestByTeam = new Dictionary<TeamId, (Offer Offer, SeasonDay Day)>();

        foreach (var entry in _history.Where(entry => entry.Kind == NegotiationEventKind.OfferPlaced && entry.Offer is not null))
        {
            if (entry.Day > asOf)
            {
                continue;
            }

            latestByTeam[entry.Offer!.TeamId] = (entry.Offer, entry.Day);
        }

        return latestByTeam.Values
            .Where(candidate => !IsClosedOut(candidate.Offer.Id))
            .Where(candidate => !HasExpiredOn(candidate.Day, asOf, expiryDays))
            .Select(candidate => candidate.Offer)
            .OrderBy(offer => offer.TeamId.Value, StringComparer.Ordinal)
            .ThenBy(offer => offer.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The offers that have run out of time as of <paramref name="asOf"/> and have not yet been
    /// recorded as expired — what an execution turns into <see cref="NegotiationEventKind.OfferExpired"/>
    /// entries before it resolves anything.
    /// </summary>
    public IReadOnlyList<Offer> OffersExpiringBy(SeasonDay asOf, int? expiryDays)
    {
        ArgumentNullException.ThrowIfNull(asOf);

        if (expiryDays is null)
        {
            return [];
        }

        var latestByTeam = new Dictionary<TeamId, (Offer Offer, SeasonDay Day)>();

        foreach (var entry in _history.Where(entry => entry.Kind == NegotiationEventKind.OfferPlaced && entry.Offer is not null))
        {
            if (entry.Day > asOf)
            {
                continue;
            }

            latestByTeam[entry.Offer!.TeamId] = (entry.Offer, entry.Day);
        }

        return latestByTeam.Values
            .Where(candidate => !IsClosedOut(candidate.Offer.Id))
            .Where(candidate => HasExpiredOn(candidate.Day, asOf, expiryDays))
            .Select(candidate => candidate.Offer)
            .OrderBy(offer => offer.TeamId.Value, StringComparer.Ordinal)
            .ThenBy(offer => offer.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The day a given offer was placed, if it was.</summary>
    public SeasonDay? PlacedOn(OfferId offerId)
    {
        ArgumentNullException.ThrowIfNull(offerId);

        return _history
            .FirstOrDefault(entry => entry.Kind == NegotiationEventKind.OfferPlaced && entry.Offer?.Id == offerId)
            ?.Day;
    }

    public Offer? FindOffer(OfferId offerId)
    {
        ArgumentNullException.ThrowIfNull(offerId);

        return _history
            .FirstOrDefault(entry => entry.Offer?.Id == offerId)
            ?.Offer;
    }

    /// <summary>
    /// Puts the negotiation back exactly as it was, for an execution that has to unwind.
    /// <para>
    /// Deliberately not "call <see cref="Close"/> to undo a <see cref="Resolve"/>": every method
    /// above is rule-checked and can legitimately refuse, and an undo that can be refused is not an
    /// undo. The same bargain <c>Team.RestoreRoster</c> strikes, for the same reason.
    /// </para>
    /// </summary>
    public void RestoreTo(NegotiationState state, OfferId? acceptedOfferId, ContractId? signedContractId, int historyCount)
    {
        if (historyCount < 0 || historyCount > _history.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(historyCount),
                historyCount,
                "A restore point has to be a length this negotiation's history actually reached.");
        }

        State = state;
        AcceptedOfferId = acceptedOfferId;
        SignedContractId = signedContractId;
        _history.RemoveRange(historyCount, _history.Count - historyCount);
    }

    private static bool HasExpiredOn(SeasonDay placedOn, SeasonDay asOf, int? expiryDays) =>
        expiryDays is { } days && asOf.DaysSince(placedOn) >= days;

    private Offer? LatestTeamOffer(TeamId teamId) =>
        _history
            .Where(entry => entry.Kind == NegotiationEventKind.OfferPlaced && entry.Offer?.TeamId == teamId)
            .LastOrDefault()
            ?.Offer;

    private bool IsClosedOut(OfferId offerId) =>
        _history.Any(entry =>
            entry.Offer?.Id == offerId &&
            entry.Kind is NegotiationEventKind.OfferWithdrawn or NegotiationEventKind.OfferExpired);

    private DomainOperationResult GuardOpen(SeasonDay day)
    {
        if (!IsOpen)
        {
            return Fail(
                NotOpenCode,
                $"This negotiation is {State}. Offers, counters and withdrawals only happen while it is open.");
        }

        if (day < LastActivityOn)
        {
            return Fail(TimeRunsBackwardsCode, DayMessage(day));
        }

        return DomainOperationResult.Success;
    }

    private string DayMessage(SeasonDay day) =>
        $"This would be recorded on {day} but the negotiation's last activity was on {LastActivityOn}. A negotiation's history only runs forwards.";

    private void Append(
        NegotiationEventKind kind,
        SeasonDay day,
        NegotiationParty author,
        TeamId? teamId,
        Offer? offer,
        OfferId? inResponseTo,
        string narrative) =>
        _history.Add(new NegotiationEntry(_history.Count, kind, day, author, teamId, offer, inResponseTo, narrative));

    private static DomainOperationResult Fail(string code, string message) =>
        DomainOperationResult.Failure(new DomainError(code, message));
}
