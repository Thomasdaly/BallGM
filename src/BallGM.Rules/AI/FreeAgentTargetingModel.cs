using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.Negotiations;
using BallGM.Rules.Signings;

namespace BallGM.Rules.AI;

/// <summary>
/// Finds free agents one team might pursue: for every free agent at a position
/// <see cref="RosterNeedsCalculator"/> already found the team needs, an offer at the player's own
/// asking price — reusing <see cref="PreferenceModel.AskingPrice"/> rather than inventing a second
/// notion of what a player is worth, because the team's opening bid and the player's own read of a
/// fair deal are the same figure in a market with no negotiation history yet — re-run through
/// <see cref="SigningValidator"/>, the same real engine the offer screen uses, so only a legal offer
/// surfaces.
/// <para>
/// Pure and total: no mutation, no randomness, nothing persisted, nothing submitted into a
/// <see cref="Negotiation"/>. Deliberately narrow — see <c>docs/architecture.md</c> →
/// "Free-agent targeting: offering at the asking price" for what this slice does not attempt: a
/// counteroffer loop, bidding against a rival team's live offer, and a league with no configured
/// compensation range at all.
/// </para>
/// </summary>
public static class FreeAgentTargetingModel
{
    private const string NeedsMatchCode = "ai_fa_target.needs_match";
    private const string AskingPriceOfferCode = "ai_fa_target.offer_at_asking_price";

    /// <summary>The contract length this model offers, capped by the league's own term limit where one is configured.</summary>
    private const int DefaultOfferTermSeasons = 2;

    private static readonly PreferenceModel Preferences = new();

    /// <summary>
    /// <paramref name="context"/>.Player is ignored and overridden for each free agent considered —
    /// callers pass the shared market context (teams, contracts, ledger, rules) the way
    /// <c>MarketContext</c> is already built for one player at a time; this model supplies each one.
    /// </summary>
    public static IReadOnlyList<FreeAgentTargetCandidate> FindCandidates(
        TeamId shoppingTeamId,
        IReadOnlyCollection<Player> freeAgents,
        MarketContext context)
    {
        ArgumentNullException.ThrowIfNull(shoppingTeamId);
        ArgumentNullException.ThrowIfNull(freeAgents);
        ArgumentNullException.ThrowIfNull(context);

        var shoppingTeam = context.TeamFor(shoppingTeamId);
        if (shoppingTeam is null)
        {
            return [];
        }

        var playersById = context.Players.ToDictionary(player => player.Id);
        var shoppingChart = DepthChartSupport.BuildChart(shoppingTeam, playersById, context.RosterLimits);
        if (shoppingChart is null)
        {
            return [];
        }

        var shoppingNeeds = RosterNeedsCalculator.Assess(
            shoppingTeamId, shoppingChart, playersById, context.RosterLimits, DepthChartSupport.NeutralCapSheet(shoppingTeamId, context.CurrentSeason));

        var neededPositions = shoppingNeeds.PositionalNeeds.ToDictionary(need => need.Position);
        if (neededPositions.Count == 0)
        {
            return [];
        }

        var validator = new SigningValidator();
        var candidates = new List<FreeAgentTargetCandidate>();

        foreach (var freeAgent in freeAgents)
        {
            if (!neededPositions.TryGetValue(freeAgent.Position, out var need))
            {
                continue;
            }

            var candidate = TryBuildCandidate(shoppingTeamId, shoppingTeam, freeAgent, need, context, validator);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static FreeAgentTargetCandidate? TryBuildCandidate(
        TeamId shoppingTeamId,
        Team shoppingTeam,
        Player freeAgent,
        PositionalNeed need,
        MarketContext context,
        SigningValidator validator)
    {
        var playerContext = context with { Player = freeAgent };
        var ask = Preferences.AskingPrice(playerContext);
        if (ask is null)
        {
            // An open market — no configured floor or ceiling — has no figure this model can defend
            // as an offer. See the class-level remarks for why that is named rather than guessed at.
            return null;
        }

        var termSeasons = context.NegotiationRules.MaximumContractSeasons is int max && max < DefaultOfferTermSeasons
            ? max
            : DefaultOfferTermSeasons;

        var terms = Enumerable.Range(0, termSeasons)
            .Select(offset => new ContractSeasonTerm(new Season(context.CurrentSeason.Year + offset), ask, ask))
            .ToList();

        var offerResult = Offer.Create(
            new OfferId($"AI-TARGET-{shoppingTeamId.Value}-{freeAgent.Id.Value}"), shoppingTeamId, freeAgent.Id, terms);
        if (offerResult.IsFailure)
        {
            return null;
        }

        var signingContext = playerContext.SigningContextFor(shoppingTeam);
        var assessmentResult = validator.Validate(offerResult.Value, signingContext);
        if (assessmentResult.IsFailure || !assessmentResult.Value.IsLegal)
        {
            return null;
        }

        var rationale = new List<RuleFinding>
        {
            new(
                NeedsMatchCode,
                $"Team '{shoppingTeamId.Value}' needs a {need.Position} ({need.Explanation}), and '{freeAgent.Id.Value}' plays there.",
                shoppingTeamId),
            new(
                AskingPriceOfferCode,
                $"Offer prices at '{freeAgent.Id.Value}''s asking price of {ask.SmallestUnits} for {termSeasons} season(s) — where this league's configured range places their quality.",
                shoppingTeamId),
        };

        return new FreeAgentTargetCandidate(offerResult.Value, shoppingTeamId, rationale, assessmentResult.Value);
    }
}
