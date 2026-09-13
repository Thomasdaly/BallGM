using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.AI;

/// <summary>
/// Values a player or a draft pick as a front office would read it, decomposed into named factors
/// rather than a single figure — see <see cref="ValuationContribution"/> for why. Pure and total: no
/// randomness, no mutation, nothing read that is not handed in.
/// <para>
/// A player values on four axes — production (<see cref="PlayerRating.Overall"/>), trajectory (where
/// the player sits against this league's own development curve), cost (salary against this league's
/// own compensation ceiling), and control (seasons of contract remaining). A pick values on two — round,
/// and how many seasons out the draft it belongs to is. Deliberately not built here: a selection-number-
/// level pick value (no order exists until a lottery has run), protection-adjusted value, and any
/// comparison or ranking between two valuations — that belongs with whichever slice first consumes one
/// (trade targeting or free-agent targeting), the same way <c>PreferenceRanking</c> arrived only once
/// M6b's market needed to compare offers, not when M6a's offers were first given a shape.
/// </para>
/// </summary>
public static class AssetValuationModel
{
    private const string ProductionCode = "ai_valuation.production";
    private const string TrajectoryAscendingCode = "ai_valuation.trajectory_ascending";
    private const string TrajectoryPeakCode = "ai_valuation.trajectory_peak";
    private const string TrajectoryDecliningCode = "ai_valuation.trajectory_declining";
    private const string TrajectoryUnconfiguredCode = "ai_valuation.trajectory_unconfigured";
    private const string CostCode = "ai_valuation.cost";
    private const string CostUnpricedCode = "ai_valuation.cost_unpriced";
    private const string ControlCode = "ai_valuation.control";
    private const string ControlUnsignedCode = "ai_valuation.control_unsigned";
    private const string RoundCode = "ai_valuation.pick_round";
    private const string YearsOutCode = "ai_valuation.pick_years_out";

    /// <summary>Reading points lost per season of age past this league's peak window end, for the trajectory factor.</summary>
    private const int TrajectoryDeclinePerYear = 10;

    /// <summary>Reading points lost per season of age below this league's peak window start, for the trajectory factor.</summary>
    private const int TrajectoryAscentPerYear = 5;

    /// <summary>Seasons of contract control treated as maximally valuable; more adds nothing further.</summary>
    private const int MaximumMeaningfulControlYears = 4;

    /// <summary>Reading points lost per round after the first, for the pick-round factor.</summary>
    private const int RoundPenalty = 20;

    /// <summary>Reading points lost per season a pick is out from the current draft, for the pick-distance factor.</summary>
    private const int YearsOutPenalty = 15;

    private static readonly int NeutralReading = ValuationContribution.Clamp(50);

    public static PlayerValuation ValuePlayer(
        Player player,
        Contract? contract,
        Season currentSeason,
        DevelopmentRules developmentRules,
        NegotiationRules negotiationRules,
        Money? softCap)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(currentSeason);
        ArgumentNullException.ThrowIfNull(developmentRules);
        ArgumentNullException.ThrowIfNull(negotiationRules);

        IReadOnlyList<ValuationContribution> contributions =
        [
            ProductionContribution(player),
            TrajectoryContribution(player, currentSeason, developmentRules),
            CostContribution(player, contract, currentSeason, negotiationRules, softCap),
            ControlContribution(contract, currentSeason),
        ];

        return new PlayerValuation(player.Id, contributions);
    }

    public static PickValuation ValuePick(DraftPick pick, Season currentSeason)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(currentSeason);

        var roundReading = PickValuationContribution.Clamp(100 + RoundPenalty - pick.Round * RoundPenalty);
        var roundFinding = new PickValuationContribution(
            PickValuationFactorKind.Round,
            roundReading,
            RoundCode,
            $"Pick '{pick.Id.Value}' is a round {pick.Round} selection.");

        var yearsOut = Math.Max(0, pick.DraftSeason.Year - currentSeason.Year);
        var yearsOutReading = PickValuationContribution.Clamp(100 - yearsOut * YearsOutPenalty);
        var yearsOutFinding = new PickValuationContribution(
            PickValuationFactorKind.YearsOut,
            yearsOutReading,
            YearsOutCode,
            $"Pick '{pick.Id.Value}' is {yearsOut} season(s) out from the {currentSeason.Year} draft, and uncertainty grows with distance.");

        return new PickValuation(pick.Id, [roundFinding, yearsOutFinding]);
    }

    private static ValuationContribution ProductionContribution(Player player)
    {
        var overall = player.Rating.Overall;
        return new ValuationContribution(
            ValuationFactorKind.Production,
            ValuationContribution.Clamp(overall),
            ProductionCode,
            $"Player '{player.Id.Value}' rates {overall} Overall.");
    }

    private static ValuationContribution TrajectoryContribution(Player player, Season currentSeason, DevelopmentRules developmentRules)
    {
        var referenceDate = new DateOnly(currentSeason.Year, 10, 1);
        var age = player.AgeOn(referenceDate);

        if (!developmentRules.IsConfigured)
        {
            return new ValuationContribution(
                ValuationFactorKind.Trajectory,
                NeutralReading,
                TrajectoryUnconfiguredCode,
                $"Player '{player.Id.Value}' is age {age}; this league configures no development curve, so trajectory reads neutral.");
        }

        if (age < developmentRules.PeakAgeStart)
        {
            var yearsToPeak = developmentRules.PeakAgeStart - age;
            return new ValuationContribution(
                ValuationFactorKind.Trajectory,
                ValuationContribution.Clamp(100 - yearsToPeak * TrajectoryAscentPerYear),
                TrajectoryAscendingCode,
                $"Player '{player.Id.Value}' is age {age}, {yearsToPeak} season(s) from this league's peak age of {developmentRules.PeakAgeStart}, so still ascending.");
        }

        if (age > developmentRules.PeakAgeEnd)
        {
            var yearsPastPeak = age - developmentRules.PeakAgeEnd;
            return new ValuationContribution(
                ValuationFactorKind.Trajectory,
                ValuationContribution.Clamp(100 - yearsPastPeak * TrajectoryDeclinePerYear),
                TrajectoryDecliningCode,
                $"Player '{player.Id.Value}' is age {age}, {yearsPastPeak} season(s) past this league's peak age of {developmentRules.PeakAgeEnd}, so declining.");
        }

        return new ValuationContribution(
            ValuationFactorKind.Trajectory,
            ValuationContribution.MaximumReading,
            TrajectoryPeakCode,
            $"Player '{player.Id.Value}' is age {age}, within this league's peak window of {developmentRules.PeakAgeStart}-{developmentRules.PeakAgeEnd}.");
    }

    private static ValuationContribution CostContribution(
        Player player,
        Contract? contract,
        Season currentSeason,
        NegotiationRules negotiationRules,
        Money? softCap)
    {
        var term = contract?.TermFor(currentSeason);
        if (term is null || !negotiationRules.CompensationCeiling.IsConfigured || softCap is null)
        {
            return new ValuationContribution(
                ValuationFactorKind.Cost,
                NeutralReading,
                CostUnpricedCode,
                $"Player '{player.Id.Value}' has no priced {currentSeason.Year} contract to compare against this league's ceiling, so cost reads neutral.");
        }

        var ceiling = negotiationRules.CompensationCeiling.CeilingFor(player.SeasonsOfService, softCap);
        if (ceiling is null || ceiling.SmallestUnits == 0)
        {
            return new ValuationContribution(
                ValuationFactorKind.Cost,
                NeutralReading,
                CostUnpricedCode,
                $"Player '{player.Id.Value}' has no ceiling figure for their service to compare their salary against, so cost reads neutral.");
        }

        var headroomPercent = 100 - (int)(term.Compensation.SmallestUnits * 100 / ceiling.SmallestUnits);
        return new ValuationContribution(
            ValuationFactorKind.Cost,
            ValuationContribution.Clamp(headroomPercent),
            CostCode,
            $"Player '{player.Id.Value}' is paid {term.Compensation.SmallestUnits} against a ceiling of {ceiling.SmallestUnits} for their service, leaving {headroomPercent}% of headroom.");
    }

    private static ValuationContribution ControlContribution(Contract? contract, Season currentSeason)
    {
        if (contract is null || contract.IsTerminated)
        {
            return new ValuationContribution(
                ValuationFactorKind.Control,
                ValuationContribution.MinimumReading,
                ControlUnsignedCode,
                "No live contract, so this team holds no control over this player.");
        }

        var yearsRemaining = Math.Max(0, contract.LastSeason.Year - currentSeason.Year + 1);
        return new ValuationContribution(
            ValuationFactorKind.Control,
            ValuationContribution.Clamp(yearsRemaining * 100 / MaximumMeaningfulControlYears),
            ControlCode,
            $"Contract '{contract.Id.Value}' runs through {contract.LastSeason.Year}, {yearsRemaining} season(s) of control from {currentSeason.Year}.");
    }
}
