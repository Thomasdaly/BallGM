using BallGM.Domain.AI;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Rules.AI;

/// <summary>
/// Reads a team's depth chart, roster count, and cap sheet for gaps a front office would act on: a
/// position with nobody at all, a starter who is not starter quality, a position with no backup, a
/// roster short of the league minimum, a payroll under the floor. Pure and total, the same division
/// of labour as <see cref="OrganisationalDirectionClassifier"/> and <see cref="AssetValuationModel"/> —
/// this reports the gap, it does not decide what to do about it.
/// </summary>
public static class RosterNeedsCalculator
{
    private const string NoPlayerAtPositionCode = "ai_needs.no_player_at_position";
    private const string WeakStarterCode = "ai_needs.weak_starter";
    private const string NoBackupCode = "ai_needs.no_backup";
    private const string BelowRosterMinimumCode = "ai_needs.below_roster_minimum";
    private const string BelowPayrollFloorCode = "ai_needs.below_payroll_floor";

    /// <summary>The Overall below which a starter reads as a need rather than a settled spot.</summary>
    private const int WeakStarterOverallThreshold = 55;

    public static RosterNeedsAssessment Assess(
        TeamId teamId,
        DepthChart depthChart,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        RosterSizeLimits rosterSizeLimits,
        TeamCapSheet capSheet)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(depthChart);
        ArgumentNullException.ThrowIfNull(playersById);
        ArgumentNullException.ThrowIfNull(rosterSizeLimits);
        ArgumentNullException.ThrowIfNull(capSheet);

        var positionalNeeds = new List<PositionalNeed>();

        foreach (var position in Enum.GetValues<Position>())
        {
            var need = AssessPosition(teamId, depthChart, playersById, position);
            if (need is not null)
            {
                positionalNeeds.Add(need);
            }
        }

        var notes = new List<RuleFinding>();

        if (depthChart.PlayerCount < rosterSizeLimits.MinimumPlayers)
        {
            notes.Add(new RuleFinding(
                BelowRosterMinimumCode,
                $"Team '{teamId.Value}' carries {depthChart.PlayerCount} players, below this league's roster minimum of {rosterSizeLimits.MinimumPlayers}.",
                teamId));
        }

        var floorStanding = capSheet.StandingFor(CapThresholdKind.PayrollFloor);
        if (floorStanding is not null && floorStanding.IsBreached)
        {
            notes.Add(new RuleFinding(
                BelowPayrollFloorCode,
                $"Team '{teamId.Value}' sits below this league's payroll floor: {floorStanding.Explanation}",
                teamId));
        }

        return new RosterNeedsAssessment(teamId, positionalNeeds, notes);
    }

    private static PositionalNeed? AssessPosition(
        TeamId teamId,
        DepthChart depthChart,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        Position position)
    {
        var slots = depthChart.At(position);
        if (slots.Count == 0)
        {
            return new PositionalNeed(
                position,
                NeedSeverity.Starter,
                NoPlayerAtPositionCode,
                $"Team '{teamId.Value}' has nobody listed at {position} at all.");
        }

        var starterSlot = slots.FirstOrDefault(slot => slot.IsStarter);
        var starterOverall = starterSlot is not null && playersById.TryGetValue(starterSlot.PlayerId, out var starter)
            ? starter.Rating.Overall
            : 0;

        if (starterOverall < WeakStarterOverallThreshold)
        {
            return new PositionalNeed(
                position,
                NeedSeverity.Starter,
                WeakStarterCode,
                $"Team '{teamId.Value}''s {position} starter rates {starterOverall} Overall, below the {WeakStarterOverallThreshold} this reading treats as starter quality.");
        }

        if (slots.Count == 1)
        {
            return new PositionalNeed(
                position,
                NeedSeverity.Depth,
                NoBackupCode,
                $"Team '{teamId.Value}' has no backup at {position} behind its starter.");
        }

        return null;
    }
}
