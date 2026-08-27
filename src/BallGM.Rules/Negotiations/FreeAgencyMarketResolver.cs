using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Negotiations;

/// <summary>
/// Works out what a free agent would do with the offers in front of them, and changes nothing.
/// <para>
/// The market half of Milestone 6. Where <see cref="SigningValidator"/> answers "may this team sign
/// this player", this answers "given everyone who wants them, who gets them" — and it answers it by
/// running every competing offer through that same validator rather than inventing a second
/// rulebook. An offer that stopped being legal since it was made loses on a rule code, not on taste.
/// </para>
/// <para>
/// Safe to call on every keystroke, exactly like the trade and signing validators, which is what
/// lets the free-agency board re-ask the question whenever anything changes.
/// </para>
/// </summary>
public sealed class FreeAgencyMarketResolver
{
    public const string WrongPlayerCode = "market.negotiation_for_another_player";
    public const string NotOpenCode = "market.negotiation_not_open";
    public const string UnknownTeamCode = "market.offering_team_not_in_league";
    public const string OfferIllegalCode = "market.offer_no_longer_signable";
    public const string BelowAskingPriceCode = "market.offer_below_asking_price";
    public const string NoExpiryCode = "market.no_offer_expiry_configured";
    public const string ImmediateResolutionCode = "market.resolves_on_arrival";
    public const string NoCompensationRangeCode = "market.no_compensation_range_configured";
    public const string OfferExpiredCode = "market.offer_expired";
    public const string NothingOnTheTableCode = "market.no_offers_on_the_table";

    private readonly SigningValidator _signingValidator = new();
    private readonly PreferenceModel _preferences = new();

    public DomainOperationResult<MarketAssessment> Assess(Negotiation negotiation, MarketContext context)
    {
        ArgumentNullException.ThrowIfNull(negotiation);
        ArgumentNullException.ThrowIfNull(context);

        // Identity and state mismatches are caller bugs rather than market outcomes: there is no
        // assessment to hand back about the wrong player, or about bidding that has already finished.
        if (negotiation.PlayerId != context.Player.Id)
        {
            return DomainOperationResult<MarketAssessment>.Failure(new DomainError(
                WrongPlayerCode,
                $"This negotiation is over player '{negotiation.PlayerId.Value}' but is being resolved against player '{context.Player.Id.Value}'."));
        }

        if (!negotiation.IsOpen)
        {
            return DomainOperationResult<MarketAssessment>.Failure(new DomainError(
                NotOpenCode,
                $"This negotiation is {negotiation.State}, so there is no market left to resolve."));
        }

        var rules = context.NegotiationRules;
        var mode = rules.MarketResolution;
        var warnings = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        if (rules.OfferExpiryDays is null)
        {
            notes.Add(new RuleFinding(
                NoExpiryCode,
                "This league sets no offer expiry, so nothing on the table ever times out."));
        }

        if (_preferences.AskingPrice(context) is null)
        {
            notes.Add(new RuleFinding(
                NoCompensationRangeCode,
                $"This league configures no minimum or maximum salary, so {context.Player.FullName} has no asking price and weighs offers only against each other."));
        }

        var expiring = negotiation.OffersExpiringBy(context.Day, rules.OfferExpiryDays);
        foreach (var expired in expiring)
        {
            warnings.Add(new RuleFinding(
                OfferExpiredCode,
                $"The offer from this team has stood longer than the {rules.OfferExpiryDays} day(s) this league allows and is out of the running.",
                expired.TeamId));
        }

        var live = negotiation.LiveOffersOn(context.Day, rules.OfferExpiryDays);

        // Immediate resolution means the player decides the instant an offer lands, so the order that
        // matters is the order they landed in. ResolutionPoint deliberately discards arrival: offers
        // accumulate, and the market is walked in the stated key order instead — team identifier then
        // offer identifier, both ordinal ascending, which LiveOffersOn already returns them in.
        var candidates = mode == MarketResolutionMode.Immediate
            ? OrderByArrival(negotiation, live)
            : live;

        if (mode == MarketResolutionMode.Immediate)
        {
            notes.Add(new RuleFinding(
                ImmediateResolutionCode,
                "This league resolves offers the moment they arrive, so the first acceptable offer wins and later ones are never weighed against it."));
        }

        var standings = new List<MarketOfferStanding>();
        var eligible = new List<(Offer Offer, OfferPreference Preference)>();

        foreach (var offer in candidates)
        {
            var team = context.TeamFor(offer.TeamId);
            if (team is null)
            {
                standings.Add(new MarketOfferStanding(
                    offer,
                    _preferences.Evaluate(offer, live, context),
                    false,
                    [new RuleFinding(UnknownTeamCode, $"Team '{offer.TeamId.Value}' is no longer in this league.", offer.TeamId)],
                    0,
                    "Out: the offering team is not in this league."));
                continue;
            }

            var preference = _preferences.Evaluate(offer, live, context);

            var signingResult = _signingValidator.Validate(offer, context.SigningContextFor(team));
            if (signingResult.IsFailure)
            {
                standings.Add(new MarketOfferStanding(
                    offer,
                    preference,
                    false,
                    signingResult.Errors.Select(error => new RuleFinding(error.Code, error.Message, offer.TeamId)).ToList(),
                    0,
                    $"Out: {team.Name}'s offer could not be assessed."));
                continue;
            }

            var signing = signingResult.Value;
            if (!signing.IsLegal)
            {
                var exclusions = signing.Violations
                    .Prepend(new RuleFinding(
                        OfferIllegalCode,
                        $"{team.Name} could not sign this contract as the league stands now, so the offer is out of the running whatever the player thinks of it.",
                        offer.TeamId))
                    .ToList();

                standings.Add(new MarketOfferStanding(offer, preference, false, exclusions, 0, $"Out: {team.Name} cannot sign this offer."));
                continue;
            }

            if (!preference.MeetsReservation)
            {
                standings.Add(new MarketOfferStanding(
                    offer,
                    preference,
                    true,
                    [new RuleFinding(BelowAskingPriceCode, preference.ReservationExplanation, offer.TeamId)],
                    0,
                    $"Out: {preference.ReservationExplanation}"));
                continue;
            }

            eligible.Add((offer, preference));
        }

        var (winner, tieBreakUsed, ranked) = Rank(eligible, mode, context);

        foreach (var (offer, preference, rank, narrative) in ranked)
        {
            standings.Add(new MarketOfferStanding(offer, preference, true, [], rank, narrative));
        }

        var narrativeLine = Narrate(context, live.Count, winner, tieBreakUsed, eligible.Count);

        if (live.Count == 0)
        {
            notes.Add(new RuleFinding(
                NothingOnTheTableCode,
                $"Nothing stands on the table for {context.Player.FullName} on {context.Day}."));
        }

        return DomainOperationResult<MarketAssessment>.Success(new MarketAssessment(
            negotiation.Id,
            context.Player.Id,
            context.Day,
            mode,
            standings,
            expiring,
            winner?.Offer.Id,
            tieBreakUsed,
            warnings,
            notes,
            narrativeLine));
    }

    /// <summary>
    /// The order offers were placed in. Only <see cref="MarketResolutionMode.Immediate"/> reads it,
    /// and that is the whole content of that mode: whoever got there first is weighed first.
    /// </summary>
    private static IReadOnlyList<Offer> OrderByArrival(Negotiation negotiation, IReadOnlyList<Offer> live)
    {
        var arrival = negotiation.History
            .Where(entry => entry.Kind == NegotiationEventKind.OfferPlaced && entry.Offer is not null)
            .ToDictionary(entry => entry.Offer!.Id, entry => entry.Sequence);

        return live.OrderBy(offer => arrival.TryGetValue(offer.Id, out var sequence) ? sequence : int.MaxValue).ToList();
    }

    /// <summary>
    /// Puts the eligible offers in finishing order.
    /// <para>
    /// Under <see cref="MarketResolutionMode.Immediate"/> there is nothing to rank: the first
    /// acceptable offer to arrive is taken, and the rest are recorded in arrival order without ever
    /// having been weighed against it — which is precisely the behaviour that makes that mode depend
    /// on submission order, and precisely why it is not the default.
    /// </para>
    /// <para>
    /// Under <see cref="MarketResolutionMode.ResolutionPoint"/> the winner is selected rather than
    /// sorted. <see cref="PreferenceRanking.Compare"/> is not transitive — a materiality band cannot
    /// be, since A can be inside B's band and B inside C's while A and C are apart — so handing it to
    /// a sort would produce an order that depends on the sort's internals. Repeated selection over a
    /// list already in the stated key order does not.
    /// </para>
    /// </summary>
    private static (
        (Offer Offer, OfferPreference Preference)? Winner,
        bool TieBreakUsed,
        List<(Offer Offer, OfferPreference Preference, int Rank, string Narrative)> Ranked)
        Rank(
            List<(Offer Offer, OfferPreference Preference)> eligible,
            MarketResolutionMode mode,
            MarketContext context)
    {
        var ranked = new List<(Offer, OfferPreference, int, string)>();

        if (eligible.Count == 0)
        {
            return (null, false, ranked);
        }

        if (mode == MarketResolutionMode.Immediate)
        {
            for (var index = 0; index < eligible.Count; index++)
            {
                var (offer, preference) = eligible[index];
                ranked.Add((
                    offer,
                    preference,
                    index + 1,
                    index == 0
                        ? "Taken: the first acceptable offer to arrive, which is how this league resolves."
                        : "Arrived after an acceptable offer had already been taken, and was never weighed against it."));
            }

            return (eligible[0], false, ranked);
        }

        var remaining = new List<(Offer Offer, OfferPreference Preference)>(eligible);
        var tieBreakUsed = false;
        (Offer Offer, OfferPreference Preference)? winner = null;

        for (var rank = 1; remaining.Count > 0; rank++)
        {
            var leaders = LeadersOf(remaining);

            (Offer Offer, OfferPreference Preference) chosen;
            string narrative;

            if (leaders.Count == 1)
            {
                chosen = leaders[0];
                narrative = Explain(chosen, remaining, rank);
            }
            else if (rank == 1)
            {
                // The only place anything random happens, and only because the model has said in as
                // many words that it cannot tell these offers apart on any factor. Drawing over a
                // list that is already in the stated key order is what makes the draw reproducible.
                var index = context.Random.NextInt32(0, leaders.Count);
                chosen = leaders[index];
                tieBreakUsed = true;
                narrative =
                    $"Taken on a seeded draw between {leaders.Count} offers this player could not separate on money, term, fit or demand.";
            }
            else
            {
                // Below the top there is nothing at stake, so the tie falls to the stated key rather
                // than spending a draw on an ordering nobody acts on.
                chosen = leaders[0];
                narrative = $"Finished {rank}: inseparable from the offers beside it, ordered by team identifier.";
            }

            ranked.Add((chosen.Offer, chosen.Preference, rank, narrative));
            winner ??= chosen;
            remaining.RemoveAll(candidate => candidate.Offer.Id == chosen.Offer.Id);
        }

        return (winner, tieBreakUsed, ranked);
    }

    /// <summary>
    /// The offers nothing in the list beats. More than one means the model is genuinely indifferent
    /// between them, which is the only state a seeded draw is allowed to resolve.
    /// </summary>
    private static List<(Offer Offer, OfferPreference Preference)> LeadersOf(
        List<(Offer Offer, OfferPreference Preference)> candidates)
    {
        var leaders = new List<(Offer Offer, OfferPreference Preference)> { candidates[0] };

        for (var index = 1; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var comparison = PreferenceRanking.Compare(candidate.Preference, leaders[0].Preference);

            if (comparison.Sign > 0)
            {
                leaders.Clear();
                leaders.Add(candidate);
            }
            else if (comparison.IsIndifferent)
            {
                leaders.Add(candidate);
            }
        }

        return leaders;
    }

    private static string Explain(
        (Offer Offer, OfferPreference Preference) chosen,
        List<(Offer Offer, OfferPreference Preference)> remaining,
        int rank)
    {
        var runnerUp = remaining.FirstOrDefault(candidate => candidate.Offer.Id != chosen.Offer.Id);
        if (runnerUp.Preference is null)
        {
            return rank == 1
                ? "Taken: the only offer this player would accept."
                : $"Finished {rank}.";
        }

        var comparison = PreferenceRanking.Compare(chosen.Preference, runnerUp.Preference);
        var prefix = rank == 1 ? "Taken" : $"Finished {rank}";

        return $"{prefix}: {comparison.Explanation}";
    }

    private static string Narrate(
        MarketContext context,
        int liveCount,
        (Offer Offer, OfferPreference Preference)? winner,
        bool tieBreakUsed,
        int eligibleCount)
    {
        var player = context.Player.FullName;

        if (liveCount == 0)
        {
            return $"{player} has no offers on the table, so there is nothing to resolve.";
        }

        if (winner is null)
        {
            return eligibleCount == 0 && liveCount > 0
                ? $"{player} turned down all {liveCount} offer(s) on the table: none of them was one this player would sign."
                : $"{player} signed with nobody.";
        }

        var team = context.TeamFor(winner.Value.Offer.TeamId);
        var teamName = team?.Name ?? winner.Value.Offer.TeamId.Value;
        var against = liveCount - 1;

        var field = against <= 0
            ? "the only offer on the table"
            : $"{against} competing offer(s)";

        return tieBreakUsed
            ? $"{player} would sign with {teamName}, chosen by seeded draw against {field} this player could not separate."
            : $"{player} would sign with {teamName}, against {field}.";
    }
}
