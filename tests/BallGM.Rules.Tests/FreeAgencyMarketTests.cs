using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;

namespace BallGM.Rules.Tests;

/// <summary>
/// The market half of Milestone 6: what happens when more than one team wants the same player.
/// Every test states a league, puts offers on the table, and asks who gets him — and the unhappy
/// paths are here as deliberately as the happy one, because a market that only works when everyone
/// can afford everyone is not a market.
/// </summary>
public sealed class FreeAgencyMarketTests
{
    private readonly FreeAgencyMarketResolver _resolver = new();
    private readonly FreeAgencyMarketExecutor _executor = new();

    [Fact]
    public void Market_ResolvesCompetingOffersTogetherAndTakesTheBetterOne()
    {
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();

        negotiation.PlaceOffer(league.Offer(0, 18_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 24_000_000), new SeasonDay(0));

        var assessment = _resolver.Assess(negotiation, league.Context()).Value;

        Assert.True(assessment.WouldSign);
        Assert.Equal(league.Team(1).Id, assessment.Winner!.Offer.TeamId);
        Assert.Equal(2, assessment.Standings.Count);
        Assert.All(assessment.Standings, standing => Assert.True(standing.IsSignable));

        // Both offers are ranked, not just the winner: a GM who lost needs to see where they came.
        Assert.Equal([1, 2], assessment.Ordered.Select(standing => standing.Rank));
    }

    [Fact]
    public void Market_ReportsEveryPreferenceFactorSeparatelyAndNeverAsOneScore()
    {
        var league = MarketTestLeague.Build([[10_000_000]]);
        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 22_000_000), new SeasonDay(0));

        var assessment = _resolver.Assess(negotiation, league.Context()).Value;
        var preference = assessment.Standings.Single().Preference;

        Assert.Equal(
            [
                PreferenceFactorKind.Money,
                PreferenceFactorKind.Term,
                PreferenceFactorKind.TeamFit,
                PreferenceFactorKind.MarketDemand,
            ],
            preference.Contributions.Select(contribution => contribution.Factor));

        // Every factor carries its own code and its own sentence, so an outcome can always be
        // explained by naming a factor rather than by quoting a total.
        Assert.All(preference.Contributions, contribution =>
        {
            Assert.InRange(contribution.Score, PreferenceContribution.MinimumScore, PreferenceContribution.MaximumScore);
            Assert.NotEmpty(contribution.RuleCode);
            Assert.NotEmpty(contribution.Explanation);
        });
    }

    [Fact]
    public void Market_LeavesOutAnOfferThatStoodLongerThanThisLeagueAllows()
    {
        // Offers expire after three days in the standard rules, so the first team's day-nought offer
        // is gone by day three and the second team's day-two offer is not.
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();

        negotiation.PlaceOffer(league.Offer(0, 24_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 19_000_000), new SeasonDay(2));

        var assessment = _resolver.Assess(negotiation, league.Context(day: 3)).Value;

        Assert.Single(assessment.ExpiringOffers);
        Assert.Equal(league.Team(0).Id, assessment.ExpiringOffers[0].TeamId);

        // The bigger offer expired, so the smaller one wins — which is the whole point of an expiry.
        Assert.Equal(league.Team(1).Id, assessment.Winner!.Offer.TeamId);
        Assert.Contains(assessment.Warnings, warning => warning.RuleCode == FreeAgencyMarketResolver.OfferExpiredCode);
    }

    [Fact]
    public void Market_ResolvesOnNobodyWhenEveryOfferIsBelowWhatThePlayerWillTake()
    {
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();

        negotiation.PlaceOffer(league.Offer(0, 3_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 4_000_000), new SeasonDay(0));

        var assessment = _resolver.Assess(negotiation, league.Context()).Value;

        Assert.False(assessment.WouldSign);
        Assert.All(assessment.Standings, standing =>
        {
            Assert.True(standing.WasExcluded);

            // Legal, and refused anyway: the two are different answers and the board shows them apart.
            Assert.True(standing.IsSignable);
            Assert.False(standing.Preference.MeetsReservation);
            Assert.Contains(standing.Exclusions, finding => finding.RuleCode == FreeAgencyMarketResolver.BelowAskingPriceCode);
        });
    }

    [Fact]
    public void Market_LeavesOutAnOfferNoSigningRoutePermits()
    {
        // One team is deep into the tax with no allowance room left; the other has room to spend.
        var league = MarketTestLeague.Build([[95_000_000, 40_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();

        negotiation.PlaceOffer(league.Offer(0, 30_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 20_000_000), new SeasonDay(0));

        var assessment = _resolver.Assess(negotiation, league.Context()).Value;

        var refused = assessment.Standings.Single(standing => standing.Offer.TeamId == league.Team(0).Id);
        Assert.False(refused.IsSignable);
        Assert.Contains(refused.Exclusions, finding => finding.RuleCode == FreeAgencyMarketResolver.OfferIllegalCode);

        // The bigger offer loses to a rule rather than to taste, and the player signs elsewhere.
        Assert.Equal(league.Team(1).Id, assessment.Winner!.Offer.TeamId);
    }

    [Fact]
    public void Market_SettlesAnUnseparableFieldWithASeededDrawAndSaysSo()
    {
        var assessment = TiedMarket(seed: 7);

        Assert.True(assessment.TieBreakUsed);
        Assert.True(assessment.WouldSign);
        Assert.Contains("seeded draw", assessment.Narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Market_ProducesTheSameOutcomeFromTheSameSeedEveryRun()
    {
        // Determinism is the pillar this whole model is built around: the same league, the same
        // offers and the same seed have to resolve the same way on every run and every platform.
        var winners = Enumerable
            .Range(0, 12)
            .Select(_ => TiedMarket(seed: 7).Winner!.Offer.TeamId.Value)
            .Distinct()
            .ToList();

        Assert.Single(winners);
    }

    [Fact]
    public void Market_LetsTheSeedRatherThanTheListOrderDecideAnUnseparableField()
    {
        // Not a claim that every seed differs — the point is that the seed is what decides, so a
        // spread of them has to be able to reach both teams. If one team always won, the draw would
        // be decoration over a fixed order.
        var winners = Enumerable
            .Range(1, 40)
            .Select(seed => TiedMarket(seed).Winner!.Offer.TeamId.Value)
            .Distinct()
            .ToList();

        Assert.True(winners.Count > 1, "No seed in the sample picked a different winner, so the draw decides nothing.");
    }

    [Fact]
    public void Market_OrdersOffersByTheStatedKeyRatherThanByArrival()
    {
        // The two offers are identical in everything the player weighs, so nothing but the ordering
        // key can separate them — and the key is the team identifier, not who submitted first.
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);

        var later = league.OpenNegotiation();
        later.PlaceOffer(league.Offer(1, 22_000_000), new SeasonDay(0));
        later.PlaceOffer(league.Offer(0, 22_000_000), new SeasonDay(0));

        var earlier = league.OpenNegotiation();
        earlier.PlaceOffer(league.Offer(0, 22_000_000), new SeasonDay(0));
        earlier.PlaceOffer(league.Offer(1, 22_000_000), new SeasonDay(0));

        var first = _resolver.Assess(later, league.Context(random: new SeededRandomSource(3))).Value;
        var second = _resolver.Assess(earlier, league.Context(random: new SeededRandomSource(3))).Value;

        Assert.Equal(second.Winner!.Offer.TeamId, first.Winner!.Offer.TeamId);
    }

    [Fact]
    public void Market_ResolvingOnArrivalTakesTheFirstAcceptableOfferEvenWhenABetterOneFollows()
    {
        var rules = ImmediateRules();
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]], negotiationRules: rules);
        var negotiation = league.OpenNegotiation();

        negotiation.PlaceOffer(league.Offer(1, 18_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(0, 25_000_000), new SeasonDay(1));

        var assessment = _resolver.Assess(negotiation, league.Context(day: 1)).Value;

        Assert.Equal(MarketResolutionMode.Immediate, assessment.Mode);
        Assert.Equal(league.Team(1).Id, assessment.Winner!.Offer.TeamId);
        Assert.False(assessment.TieBreakUsed);
        Assert.Contains(assessment.Notes, note => note.RuleCode == FreeAgencyMarketResolver.ImmediateResolutionCode);
    }

    [Fact]
    public void Market_InAnUncappedLeagueWeighsOffersOnlyAgainstEachOther()
    {
        var league = MarketTestLeague.Build(
            [[10_000_000], [10_000_000]],
            thresholds: CapThresholds.Uncapped,
            negotiationRules: NegotiationRules.OpenMarket);

        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 4_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 9_000_000), new SeasonDay(0));

        var assessment = _resolver.Assess(negotiation, league.Context()).Value;

        // No floor and no ceiling means no asking price, so nothing is refused for being too small —
        // and offers that would be laughed at in a capped league are the whole market here.
        Assert.Contains(assessment.Notes, note => note.RuleCode == FreeAgencyMarketResolver.NoCompensationRangeCode);
        Assert.Contains(assessment.Notes, note => note.RuleCode == FreeAgencyMarketResolver.NoExpiryCode);
        Assert.All(assessment.Standings, standing => Assert.True(standing.Preference.MeetsReservation));
        Assert.Equal(league.Team(1).Id, assessment.Winner!.Offer.TeamId);
    }

    [Fact]
    public void Market_AssessmentChangesNothingAboutTheNegotiation()
    {
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 22_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 24_000_000), new SeasonDay(0));

        var before = negotiation.History.Count;
        _resolver.Assess(negotiation, league.Context(day: 5));
        _resolver.Assess(negotiation, league.Context(day: 5));

        // Day five is past the expiry, so a resolver that recorded what it found would have appended
        // two expiries by now. Assessment asks the question; it is not what answers it.
        Assert.Equal(before, negotiation.History.Count);
        Assert.Equal(NegotiationState.Open, negotiation.State);
    }

    [Fact]
    public void Market_ExecutionSignsTheWinnerAndRecordsTheWholeThing()
    {
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 18_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 24_000_000), new SeasonDay(0));

        var execution = _executor.Resolve(negotiation, league.Context()).Value;

        Assert.True(execution.PlayerSigned);
        Assert.Equal(league.Team(1).Id, execution.Contract!.TeamId);
        Assert.Equal(NegotiationState.Signed, negotiation.State);
        Assert.Equal(execution.Contract.Id, negotiation.SignedContractId);
        Assert.Contains(negotiation.History, entry => entry.Kind == NegotiationEventKind.MarketResolved);
        Assert.Contains(negotiation.History, entry => entry.Kind == NegotiationEventKind.ContractSigned);
        Assert.Single(execution.LedgerEntries);
        Assert.Contains(league.FreeAgent.Id, league.Team(1).PlayerIds);
    }

    [Fact]
    public void Market_ExecutionClosesTheNegotiationWhenNobodyIsAcceptable()
    {
        var league = MarketTestLeague.Build([[10_000_000]]);
        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 3_000_000), new SeasonDay(0));

        var execution = _executor.Resolve(negotiation, league.Context()).Value;

        Assert.False(execution.PlayerSigned);
        Assert.Null(execution.Route);
        Assert.Empty(execution.LedgerEntries);
        Assert.Equal(NegotiationState.Closed, negotiation.State);
        Assert.Null(negotiation.AcceptedOfferId);
    }

    [Fact]
    public void Market_ExecutionRecordsExpiriesInTheHistoryRatherThanJustSkippingThem()
    {
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 24_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 19_000_000), new SeasonDay(2));

        _executor.Resolve(negotiation, league.Context(day: 3));

        var expiry = negotiation.History.Single(entry => entry.Kind == NegotiationEventKind.OfferExpired);
        Assert.Equal(league.Team(0).Id, expiry.TeamId);
    }

    [Fact]
    public void Market_LeavesTheNegotiationExactlyAsItWasWhenTheSigningIsRefused()
    {
        // The free agent is on the winning team's roster with no contract, so every rule the
        // validator checks passes and the roster refuses them at the moment of signing.
        var league = MarketTestLeague.Build([[10_000_000]]);
        league.PlaceFreeAgentOnRosterOf(0);

        var negotiation = league.OpenNegotiation();
        negotiation.PlaceOffer(league.Offer(0, 22_000_000), new SeasonDay(0));

        var historyBefore = negotiation.History.Count;
        var result = _executor.Resolve(negotiation, league.Context());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == FreeAgencyMarketExecutor.RolledBackCode);
        Assert.Equal(NegotiationState.Open, negotiation.State);
        Assert.Equal(historyBefore, negotiation.History.Count);
        Assert.Null(negotiation.AcceptedOfferId);
    }

    [Fact]
    public void Market_RefusesToResolveANegotiationThatIsAlreadyOver()
    {
        var league = MarketTestLeague.Build([[10_000_000]]);
        var negotiation = league.OpenNegotiation();
        negotiation.Close("Nobody wanted him.", new SeasonDay(0));

        var result = _resolver.Assess(negotiation, league.Context());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == FreeAgencyMarketResolver.NotOpenCode);
    }

    /// <summary>
    /// Two teams with identical squads making identical offers: nothing the model weighs separates
    /// them, which is the only state a seeded draw is allowed to decide.
    /// </summary>
    private MarketAssessment TiedMarket(int seed)
    {
        var league = MarketTestLeague.Build([[10_000_000], [10_000_000]]);
        var negotiation = league.OpenNegotiation();

        negotiation.PlaceOffer(league.Offer(0, 22_000_000), new SeasonDay(0));
        negotiation.PlaceOffer(league.Offer(1, 22_000_000), new SeasonDay(0));

        return _resolver.Assess(negotiation, league.Context(random: new SeededRandomSource(seed))).Value;
    }

    private static NegotiationRules ImmediateRules() => NegotiationRules.Create(
        SigningTestLeague.StandardThresholds,
        maximumContractSeasons: 5,
        maximumIncumbentContractSeasons: 6,
        maximumAnnualEscalationPercent: 8,
        maximumAnnualDeescalationPercent: 8,
        CompensationCeilingScale.Create([new ScaleBand(0, 25)]).Value,
        CompensationFloorScale.Create([new ScaleBand(0, 1_000_000), new ScaleBand(3, 2_000_000)]).Value,
        standardOverCapAllowance: new Money(12_000_000),
        standardOverCapAllowanceUnavailableAbove: CapThresholdKind.FirstApron,
        allowanceMaySplitAcrossPlayers: true,
        MarketResolutionMode.Immediate,
        offerExpiryDays: 3).Value;
}
