using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Configuration;
using BallGM.Rules.Trades;

namespace BallGM.Rules.AI;

/// <summary>
/// Finds trades one team might pursue: for every other team in the league, a one-for-one swap where
/// each side's incoming player addresses a positional need <see cref="RosterNeedsCalculator"/> already
/// found, each side's outgoing player is spared from a position it has surplus at (so giving it up
/// creates no new hole), the two players read within a stated tolerance of each other on production,
/// and the resulting proposal is legal against <see cref="TradeValidator"/> — the same re-validation
/// the free-agency market already runs every competing offer through, because this model owns no
/// affordability or legality rule of its own.
/// <para>
/// Pure and total: no mutation, no randomness, nothing persisted. Deliberately narrow — see the
/// class-level remarks in <c>docs/architecture.md</c> → "Trade targeting: matching needs to surplus"
/// for what this slice does not attempt: multi-team trades, picks as an asset, competitive-direction
/// weighting, and any ranking between two candidates.
/// </para>
/// </summary>
public static class TradeTargetingModel
{
    private const string NeedsMatchCode = "ai_trade_target.needs_match";
    private const string ValueParityCode = "ai_trade_target.value_parity";

    /// <summary>How far apart two players' production readings may sit before a swap reads as lopsided.</summary>
    private const int ProductionParityTolerance = 15;

    public static IReadOnlyList<TradeTargetCandidate> FindCandidates(
        TeamId shoppingTeamId,
        TradeContext context,
        DevelopmentRules developmentRules,
        NegotiationRules negotiationRules)
    {
        ArgumentNullException.ThrowIfNull(shoppingTeamId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(developmentRules);
        ArgumentNullException.ThrowIfNull(negotiationRules);

        var teamsById = context.Teams.ToDictionary(team => team.Id);
        if (!teamsById.TryGetValue(shoppingTeamId, out var shoppingTeam))
        {
            return [];
        }

        var playersById = context.Players.ToDictionary(player => player.Id);

        // Both conditions, not just one: Contract.TermFor answers "does this contract's stated term
        // list include this season" without regard to termination — a released player's contract
        // keeps its remaining terms exactly so ChargeFor can still price the dead money they leave
        // behind (see LeagueSnapshot's own remarks on Players including a released player for the
        // same reason). A player can legitimately hold both that terminated contract and a fresh one
        // signed elsewhere at once, so keying this dictionary on TermFor alone collides on them the
        // moment both exist. LeagueSession.IsFreeAgent already reads "currently under contract" as
        // this same pair of conditions together; this is that reading, not a second one.
        var contractsByPlayer = context.Contracts
            .Where(contract => !contract.IsTerminated && contract.TermFor(context.CurrentSeason) is not null)
            .ToDictionary(contract => contract.PlayerId);

        var shoppingChart = DepthChartSupport.BuildChart(shoppingTeam, playersById, context.RosterLimits);
        if (shoppingChart is null)
        {
            return [];
        }

        var shoppingNeeds = RosterNeedsCalculator.Assess(
            shoppingTeamId, shoppingChart, playersById, context.RosterLimits, DepthChartSupport.NeutralCapSheet(shoppingTeamId, context.CurrentSeason));

        var candidates = new List<TradeTargetCandidate>();
        var validator = new TradeValidator();
        var seenPairs = new HashSet<(PlayerId, PlayerId)>();

        foreach (var counterpartyTeam in context.Teams.Where(team => team.Id != shoppingTeamId))
        {
            var counterpartyChart = DepthChartSupport.BuildChart(counterpartyTeam, playersById, context.RosterLimits);
            if (counterpartyChart is null)
            {
                continue;
            }

            var counterpartyNeeds = RosterNeedsCalculator.Assess(
                counterpartyTeam.Id, counterpartyChart, playersById, context.RosterLimits, DepthChartSupport.NeutralCapSheet(counterpartyTeam.Id, context.CurrentSeason));

            foreach (var shoppingNeed in shoppingNeeds.PositionalNeeds)
            {
                var incomingSlot = BestSpare(counterpartyChart, shoppingNeed.Position, playersById);
                if (incomingSlot is null)
                {
                    continue;
                }

                foreach (var counterpartyNeed in counterpartyNeeds.PositionalNeeds)
                {
                    var outgoingSlot = BestSpare(shoppingChart, counterpartyNeed.Position, playersById);
                    if (outgoingSlot is null || !seenPairs.Add((incomingSlot.PlayerId, outgoingSlot.PlayerId)))
                    {
                        continue;
                    }

                    var candidate = TryBuildCandidate(
                        shoppingTeamId,
                        counterpartyTeam.Id,
                        incomingSlot.PlayerId,
                        outgoingSlot.PlayerId,
                        shoppingNeed,
                        counterpartyNeed,
                        context,
                        playersById,
                        contractsByPlayer,
                        developmentRules,
                        negotiationRules,
                        validator);

                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }
                }
            }
        }

        return candidates;
    }

    private static TradeTargetCandidate? TryBuildCandidate(
        TeamId shoppingTeamId,
        TeamId counterpartyTeamId,
        PlayerId incomingPlayerId,
        PlayerId outgoingPlayerId,
        PositionalNeed shoppingNeed,
        PositionalNeed counterpartyNeed,
        TradeContext context,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        IReadOnlyDictionary<PlayerId, Contract> contractsByPlayer,
        DevelopmentRules developmentRules,
        NegotiationRules negotiationRules,
        TradeValidator validator)
    {
        var incomingPlayer = playersById[incomingPlayerId];
        var outgoingPlayer = playersById[outgoingPlayerId];

        var incomingValuation = AssetValuationModel.ValuePlayer(
            incomingPlayer,
            contractsByPlayer.GetValueOrDefault(incomingPlayerId),
            context.CurrentSeason,
            developmentRules,
            negotiationRules,
            context.CapThresholds.SoftCap);

        var outgoingValuation = AssetValuationModel.ValuePlayer(
            outgoingPlayer,
            contractsByPlayer.GetValueOrDefault(outgoingPlayerId),
            context.CurrentSeason,
            developmentRules,
            negotiationRules,
            context.CapThresholds.SoftCap);

        var incomingProduction = incomingValuation.Factor(ValuationFactorKind.Production)!.Reading;
        var outgoingProduction = outgoingValuation.Factor(ValuationFactorKind.Production)!.Reading;
        var gap = Math.Abs(incomingProduction - outgoingProduction);
        if (gap > ProductionParityTolerance)
        {
            return null;
        }

        var tradeId = new TradeId($"AI-TARGET-{shoppingTeamId.Value}-{counterpartyTeamId.Value}-{outgoingPlayerId.Value}-FOR-{incomingPlayerId.Value}");
        var movements = new[]
        {
            TradeAssetMovement.Player(incomingPlayerId, counterpartyTeamId, shoppingTeamId),
            TradeAssetMovement.Player(outgoingPlayerId, shoppingTeamId, counterpartyTeamId),
        };

        var proposalResult = TradeProposal.Create(
            tradeId, context.CurrentSeason, [shoppingTeamId, counterpartyTeamId], movements, LeagueStateToken.From(context.Ledger));
        if (proposalResult.IsFailure)
        {
            return null;
        }

        var assessmentResult = validator.Validate(proposalResult.Value, context);
        if (assessmentResult.IsFailure || !assessmentResult.Value.IsLegal)
        {
            return null;
        }

        var rationale = new List<RuleFinding>
        {
            new(
                NeedsMatchCode,
                $"Team '{shoppingTeamId.Value}' would receive '{incomingPlayerId.Value}' for {shoppingNeed.Position} ({shoppingNeed.Explanation}) and send '{outgoingPlayerId.Value}' for {counterpartyNeed.Position}, which addresses team '{counterpartyTeamId.Value}''s own stated need there.",
                shoppingTeamId),
            new(
                ValueParityCode,
                $"Player '{incomingPlayerId.Value}' ({incomingProduction} production) and player '{outgoingPlayerId.Value}' ({outgoingProduction} production) sit within this model's {ProductionParityTolerance}-point parity tolerance.",
                shoppingTeamId),
        };

        return new TradeTargetCandidate(proposalResult.Value, shoppingTeamId, counterpartyTeamId, rationale, assessmentResult.Value);
    }

    /// <summary>
    /// The highest-Overall player at <paramref name="position"/> beyond the starter, or <c>null</c>
    /// when the team has nobody there or nobody beyond its starter — trading either away would leave
    /// a hole this model exists to avoid creating.
    /// </summary>
    private static DepthChartSlot? BestSpare(DepthChart chart, Position position, IReadOnlyDictionary<PlayerId, Player> playersById)
    {
        var slots = chart.At(position);
        if (slots.Count <= 1)
        {
            return null;
        }

        return slots
            .Where(slot => !slot.IsStarter)
            .OrderByDescending(slot => playersById.TryGetValue(slot.PlayerId, out var player) ? player.Rating.Overall : 0)
            .ThenBy(slot => slot.PlayerId.Value, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
