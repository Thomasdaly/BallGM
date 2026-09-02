using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Players;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Draft;

/// <summary>
/// Turns a prospect's true rating and a team's scouting investment into what that team actually
/// knows — a <see cref="ScoutingRange"/> — without ever handing the caller the true number itself.
/// Pure and total for any valid input: the same true rating, rules, and investment always narrow to
/// the same band.
/// <para>
/// Confidence is <see cref="ScoutingRules.BaseConfidence"/> plus whatever
/// <see cref="ScoutingRules.InvestmentConfidence"/> adds for the points spent, clamped to 0-100. The
/// band's width shrinks linearly from <see cref="ScoutingRules.MaxRangeWidth"/> at zero confidence to
/// zero at full confidence, centred on the true value — so full confidence always reveals it exactly,
/// and <see cref="ScoutingRules.None"/> (confidence 100, width 0) does too, on every prospect, with no
/// investment needed at all.
/// </para>
/// </summary>
public static class ScoutingModel
{
    private const string NegativeInvestmentCode = "scouting.negative_investment";

    public static DomainOperationResult<ScoutingRange> Assess(PlayerRating trueRating, ScoutingRules rules, int investedPoints)
    {
        ArgumentNullException.ThrowIfNull(trueRating);
        ArgumentNullException.ThrowIfNull(rules);

        if (investedPoints < 0)
        {
            return DomainOperationResult<ScoutingRange>.Failure(new DomainError(
                NegativeInvestmentCode,
                $"Scouting investment cannot be negative, but was {investedPoints}."));
        }

        var bonus = rules.InvestmentConfidence.ValueFor(investedPoints) ?? 0;
        var confidence = Math.Clamp(rules.BaseConfidence + (int)bonus, 0, 100);

        var width = rules.MaxRangeWidth * (100 - confidence) / 100;
        var lowerHalf = width / 2;
        var upperHalf = width - lowerHalf;

        var lower = Math.Clamp(trueRating.Overall - lowerHalf, PlayerRating.MinimumOverall, PlayerRating.MaximumOverall);
        var upper = Math.Clamp(trueRating.Overall + upperHalf, PlayerRating.MinimumOverall, PlayerRating.MaximumOverall);

        return ScoutingRange.Create(lower, upper, confidence);
    }
}
