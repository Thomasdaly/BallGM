using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.AI;

/// <summary>
/// Shared plumbing between the trade- and free-agent-targeting models: building the same notion of
/// depth the season engine builds, and a cap sheet carrying nothing for a caller that needs
/// <see cref="RosterNeedsCalculator.Assess"/>'s positional needs without a real cap projection behind
/// them.
/// </summary>
internal static class DepthChartSupport
{
    /// <summary>
    /// Builds the same notion of depth the season engine builds — filtering the injured and retired
    /// the same way <c>RulesSeasonEngine.BuildContext</c> does, because a second, slightly different
    /// notion of "available" would be a second answer to a question the depth chart already answers.
    /// </summary>
    public static DepthChart? BuildChart(Team team, IReadOnlyDictionary<PlayerId, Player> playersById, RosterSizeLimits rosterLimits)
    {
        var available = team.PlayerIds
            .Select(playerId => playersById.GetValueOrDefault(playerId))
            .Where(player => player is not null && !player.IsInjured && !player.IsRetired)
            .Select(player => new AvailablePlayer(player!.Id, player.Position, player.Rating.Overall))
            .ToList();

        var result = new DepthChartBuilder().Build(team.Id, available, rosterLimits, team.PlayerIds.Count);
        return result.IsSuccess ? result.Value.Chart : null;
    }

    /// <summary>
    /// A cap sheet carrying no charges and no thresholds. <see cref="RosterNeedsCalculator"/> only
    /// reads a cap sheet for its payroll-floor note, which neither targeting model consumes — a real
    /// projection would need the full <see cref="CapLedger"/> pass for a reading nobody reads.
    /// </summary>
    public static TeamCapSheet NeutralCapSheet(TeamId teamId, Season season) =>
        new(teamId, season, Money.Zero, Money.Zero, Money.Zero, Money.Zero, [], []);
}
