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
/// <summary>
/// What a summer's trade market adds to one search (<c>docs/franchise-realism.md</c> → F5): teams and
/// players already part of a trade this summer are off the table, and each side has a direction, which
/// sets what it counts as getting better.
/// </summary>
public sealed record TradeTargetingOptions(
    IReadOnlySet<TeamId> ExcludedTeams,
    IReadOnlySet<PlayerId> ExcludedPlayers,
    IReadOnlyDictionary<TeamId, CompetitiveDirection> Directions,
    DateOnly AsOf);

public static class TradeTargetingModel
{
    private const string MutualGainCode = "ai_trade_target.both_sides_gain";

    /// <summary>The years younger a rebuilding team needs the player it gets back to be, for a swap that does not help it now.</summary>
    public const int RebuildingYouthYears = 3;

    private const string NeedsMatchCode = "ai_trade_target.needs_match";
    private const string ValueParityCode = "ai_trade_target.value_parity";

    /// <summary>How far apart two players' production readings may sit before a swap reads as lopsided.</summary>
    private const int ProductionParityTolerance = 15;

    public static IReadOnlyList<TradeTargetCandidate> FindCandidates(
        TeamId shoppingTeamId,
        TradeContext context,
        DevelopmentRules developmentRules,
        NegotiationRules negotiationRules,
        TradeTargetingOptions? options = null,
        Func<Team, IReadOnlyDictionary<PlayerId, Player>, DepthChart?>? chartFor = null)
    {
        chartFor ??= (team, players) => DepthChartSupport.BuildChart(team, players, context.RosterLimits);
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

        var shoppingChart = chartFor(shoppingTeam, playersById);
        if (shoppingChart is null)
        {
            return [];
        }

        var shoppingNeeds = RosterNeedsCalculator.Assess(
            shoppingTeamId, shoppingChart, shoppingTeam.RosterCount, playersById, context.RosterLimits, DepthChartSupport.NeutralCapSheet(shoppingTeamId, context.CurrentSeason));

        var candidates = new List<TradeTargetCandidate>();
        var validator = new TradeValidator();
        var seenPairs = new HashSet<(PlayerId, PlayerId)>();

        foreach (var counterpartyTeam in context.Teams.Where(team => team.Id != shoppingTeamId && options?.ExcludedTeams.Contains(team.Id) != true))
        {
            var counterpartyChart = chartFor(counterpartyTeam, playersById);
            if (counterpartyChart is null)
            {
                continue;
            }

            var counterpartyNeeds = RosterNeedsCalculator.Assess(
                counterpartyTeam.Id, counterpartyChart, counterpartyTeam.RosterCount, playersById, context.RosterLimits, DepthChartSupport.NeutralCapSheet(counterpartyTeam.Id, context.CurrentSeason));

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

                    if (options is not null &&
                        (options.ExcludedPlayers.Contains(incomingSlot.PlayerId) || options.ExcludedPlayers.Contains(outgoingSlot.PlayerId) ||
                         !BothSidesGain(shoppingTeam, counterpartyTeam, playersById[outgoingSlot.PlayerId], playersById[incomingSlot.PlayerId], playersById, options)))
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

        if (options is null)
        {
            return candidates;
        }

        // Best for both first: the trade a summer market makes is the one that helps the two teams most.
        return candidates
            .OrderByDescending(candidate => Gain(shoppingTeam, candidate, playersById) + Gain(teamsById[candidate.CounterpartyTeamId], candidate, playersById))
            .ThenBy(candidate => candidate.Proposal.Id.Value, StringComparer.Ordinal)
            .Select(candidate => candidate with
            {
                Rationale = [.. candidate.Rationale, new RuleFinding(MutualGainCode,
                    "Both teams come out of it better by their own direction: a stronger rotation now, or — for a rebuilding team — a younger player with a ceiling at least as high.", shoppingTeamId)],
            })
            .ToList();
    }

    /// <summary>
    /// Whether a one-for-one swap makes both teams better by their own lights: a team stronger as it would
    /// field the players (see <see cref="RotationStrength"/>) — or, for a rebuilding team, a player at least
    /// <see cref="RebuildingYouthYears"/> years younger coming back whose ceiling is no lower than the
    /// current level of the player going out. A swap that helps neither, or only one, is not a trade a
    /// front office makes, which is what keeps a summer from becoming a carousel.
    /// </summary>
    private static bool BothSidesGain(Team shopping, Team counterparty, Player outgoing, Player incoming, IReadOnlyDictionary<PlayerId, Player> playersById, TradeTargetingOptions options)
    {
        bool Gains(Team team, Player giving, Player getting)
        {
            if (RotationStrength(team, playersById, giving, getting) > RotationStrength(team, playersById, null, null))
            {
                return true;
            }

            return options.Directions.GetValueOrDefault(team.Id) == CompetitiveDirection.Rebuilding &&
                getting.AgeOn(options.AsOf) + RebuildingYouthYears <= giving.AgeOn(options.AsOf) &&
                getting.Rating.PotentialAbility >= giving.Rating.Overall;
        }

        return Gains(shopping, outgoing, incoming) && Gains(counterparty, incoming, outgoing);
    }

    /// <summary>
    /// A team's strength as a coach would field it, with one player swapped for another if given: the
    /// best player at each position counts twice (he starts), the next three best once (the bench that
    /// plays). Position-aware on purpose — filling a weak starting spot is worth more than adding depth.
    /// </summary>
    private static int RotationStrength(Team team, IReadOnlyDictionary<PlayerId, Player> playersById, Player? leaving, Player? arriving)
    {
        // The best player at each position starts; the next BenchCounted best of the rest count once.
        // Written without LINQ: the trade search asks this four times for every pair it weighs.
        Span<int> starter = stackalloc int[PositionCount];
        Span<int> bench = stackalloc int[BenchCounted];
        starter.Fill(-1);
        bench.Fill(-1);

        static void Consider(Player player, Span<int> starter, Span<int> bench)
        {
            var overall = player.Rating.Overall;
            var position = (int)player.Position;
            if (overall > starter[position])
            {
                (overall, starter[position]) = (starter[position], overall);
            }

            for (var index = 0; index < bench.Length && overall >= 0; index++)
            {
                if (overall > bench[index])
                {
                    (overall, bench[index]) = (bench[index], overall);
                }
            }
        }

        foreach (var id in team.PlayerIds)
        {
            if (id != leaving?.Id && playersById.TryGetValue(id, out var player))
            {
                Consider(player, starter, bench);
            }
        }

        if (arriving is not null)
        {
            Consider(arriving, starter, bench);
        }

        var strength = 0;
        foreach (var overall in starter)
        {
            strength += overall < 0 ? 0 : 2 * overall;
        }

        foreach (var overall in bench)
        {
            strength += Math.Max(0, overall);
        }

        return strength;
    }

    /// <summary>One slot per <see cref="Position"/> value, indexed by the value itself.</summary>
    private static readonly int PositionCount = Enum.GetValues<Position>().Max(position => (int)position) + 1;

    /// <summary>How many bench players beyond the starters count toward a team's strength.</summary>
    private const int BenchCounted = 3;

    private static int Gain(Team team, TradeTargetCandidate candidate, IReadOnlyDictionary<PlayerId, Player> playersById)
    {
        var arriving = candidate.Proposal.Movements.FirstOrDefault(movement => movement.ToTeamId == team.Id && movement.PlayerId is not null)?.PlayerId;
        var leaving = candidate.Proposal.Movements.FirstOrDefault(movement => movement.FromTeamId == team.Id && movement.PlayerId is not null)?.PlayerId;
        return arriving is null || leaving is null
            ? 0
            : RotationStrength(team, playersById, playersById[leaving], playersById[arriving]) - RotationStrength(team, playersById, null, null);
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
                $"Team '{shoppingTeamId.Value}' would get '{incomingPlayerId.Value}' to fill its need at {ExplanationText.Position(shoppingNeed.Position)}, and send '{outgoingPlayerId.Value}' to fill team '{counterpartyTeamId.Value}''s need at {ExplanationText.Position(counterpartyNeed.Position)}.",
                shoppingTeamId),
            new(
                ValueParityCode,
                $"Player '{incomingPlayerId.Value}' (production {incomingProduction}) and player '{outgoingPlayerId.Value}' (production {outgoingProduction}) are within {ProductionParityTolerance} points of each other, close enough for this model to call it a fair swap.",
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
