using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;

namespace BallGM.Rules.AI;

/// <summary>
/// Recommends which prospect one team should take with one selection: a positional need
/// <see cref="RosterNeedsCalculator"/> already found, filled by the best-scouted prospect available
/// there — falling back to the best-scouted prospect in the whole pool when nobody left fills a
/// stated need, because a team on the clock still has to select someone.
/// <para>
/// Reads <see cref="ScoutingModel"/>'s output, never <see cref="Prospect.TrueRating"/> directly — an
/// AI decision that can see through its own league's scouting uncertainty is not an explainable
/// decision, it is the model cheating at a game the human player cannot. Every candidate is assessed
/// at zero scouting investment, the honest "nobody has looked yet" reading, because no caller tracks
/// per-team, per-prospect investment yet — see <c>docs/architecture.md</c> → "Draft decisions: picking
/// from what scouting actually knows" for why that is named rather than approximated here.
/// </para>
/// <para>
/// Pure and total: no mutation, no randomness beyond what <see cref="ScoutingModel"/> itself is
/// (none, at a fixed zero investment), nothing persisted, no wiring into <c>DraftDay</c>.
/// </para>
/// </summary>
public static class DraftDecisionModel
{
    private const string NeedsMatchCode = "ai_draft_target.needs_match";
    private const string BestAvailableCode = "ai_draft_target.best_available_no_need_match";
    private const string ScoutedQualityCode = "ai_draft_target.scouted_quality";

    /// <summary>How much scouting investment this model assumes for every prospect — none tracked yet, so none spent.</summary>
    private const int AssumedInvestedPoints = 0;

    public static DraftPickRecommendation? Recommend(
        TeamId teamId,
        Team team,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        IReadOnlyCollection<Prospect> pool,
        RosterSizeLimits rosterLimits,
        ScoutingRules scoutingRules,
        Season currentSeason)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(playersById);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(rosterLimits);
        ArgumentNullException.ThrowIfNull(scoutingRules);
        ArgumentNullException.ThrowIfNull(currentSeason);

        if (pool.Count == 0)
        {
            return null;
        }

        var scoutedPool = pool
            .Select(prospect => (Prospect: prospect, Range: ScoutingModel.Assess(prospect.TrueRating, scoutingRules, AssumedInvestedPoints).Value))
            .ToList();

        var neededPositions = FindNeededPositions(teamId, team, playersById, rosterLimits, currentSeason);

        var needMatches = scoutedPool.Where(entry => neededPositions.Contains(entry.Prospect.Position)).ToList();
        var consideredPool = needMatches.Count > 0 ? needMatches : scoutedPool;
        var matchedAPosition = needMatches.Count > 0;

        var chosen = consideredPool
            .OrderByDescending(entry => Midpoint(entry.Range))
            .ThenByDescending(entry => entry.Range.Confidence)
            .ThenBy(entry => entry.Prospect.Id.Value, StringComparer.Ordinal)
            .First();

        var rationale = new List<RuleFinding>
        {
            matchedAPosition
                ? new RuleFinding(
                    NeedsMatchCode,
                    $"Team '{teamId.Value}' needs a {chosen.Prospect.Position}, and '{chosen.Prospect.Id.Value}' plays there.",
                    teamId)
                : new RuleFinding(
                    BestAvailableCode,
                    $"No prospect remaining fills a position team '{teamId.Value}' needs, so the best-scouted talent in the class was taken instead.",
                    teamId),
            new RuleFinding(
                ScoutedQualityCode,
                $"'{chosen.Prospect.Id.Value}' scouts {chosen.Range.LowerBound}-{chosen.Range.UpperBound} Overall at {chosen.Range.Confidence}% confidence, a midpoint reading of {Midpoint(chosen.Range)} — the true rating is never read.",
                teamId),
        };

        return new DraftPickRecommendation(teamId, chosen.Prospect.Id, rationale);
    }

    private static int Midpoint(ScoutingRange range) => (range.LowerBound + range.UpperBound) / 2;

    private static HashSet<Position> FindNeededPositions(
        TeamId teamId, Team team, IReadOnlyDictionary<PlayerId, Player> playersById, RosterSizeLimits rosterLimits, Season currentSeason)
    {
        var chart = DepthChartSupport.BuildChart(team, playersById, rosterLimits);
        if (chart is null)
        {
            return [];
        }

        var needs = RosterNeedsCalculator.Assess(
            teamId, chart, playersById, rosterLimits, DepthChartSupport.NeutralCapSheet(teamId, currentSeason));

        return needs.PositionalNeeds.Select(need => need.Position).ToHashSet();
    }
}
