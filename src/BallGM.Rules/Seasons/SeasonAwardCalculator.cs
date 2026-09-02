using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>The result of deciding one award: who won, or that nobody could — always explained.</summary>
public sealed record AwardResult(string AwardCode, string AwardName, PlayerId? WinnerId, RuleFinding Finding);

/// <summary>
/// Decides every configured award from one season's stat lines. Pure and total: the same stat lines
/// and the same <see cref="AwardRules"/> always produce the same winners.
/// <para>
/// Each award goes to whoever leads the league in its stated <see cref="AwardStatBasis"/>, ties broken
/// by <see cref="PlayerId"/> ordinal — the same deterministic-ordering-over-a-draw preference the free
/// agency market's tie-break key uses, chosen here because unlike a market tie there is no
/// materiality band to be indifferent inside, and no seed this milestone has reason to spend on it.
/// </para>
/// </summary>
public static class SeasonAwardCalculator
{
    private const string NoStatLinesCode = "award.no_stat_lines";
    private const string StatLeaderCode = "award.stat_leader";

    public static IReadOnlyList<AwardResult> Calculate(IReadOnlyList<PlayerSeasonStatLine> statLines, AwardRules rules)
    {
        ArgumentNullException.ThrowIfNull(statLines);
        ArgumentNullException.ThrowIfNull(rules);

        return rules.Awards.Select(award => Decide(award, statLines)).ToArray();
    }

    private static AwardResult Decide(AwardDefinition award, IReadOnlyList<PlayerSeasonStatLine> statLines)
    {
        if (statLines.Count == 0)
        {
            return new AwardResult(
                award.Code, award.Name, null,
                new RuleFinding(NoStatLinesCode, $"No player recorded a stat line this season, so the {award.Name} has no winner."));
        }

        var winner = statLines
            .OrderByDescending(line => StatFor(line, award.StatBasis))
            .ThenBy(line => line.PlayerId.Value, StringComparer.Ordinal)
            .First();

        return new AwardResult(
            award.Code, award.Name, winner.PlayerId,
            new RuleFinding(StatLeaderCode, $"'{winner.PlayerId.Value}' led the league in {award.StatBasis} ({StatFor(winner, award.StatBasis)}) and wins the {award.Name}."));
    }

    private static int StatFor(PlayerSeasonStatLine line, AwardStatBasis basis) => basis switch
    {
        AwardStatBasis.TotalPoints => line.TotalPoints,
        AwardStatBasis.TotalRebounds => line.TotalRebounds,
        AwardStatBasis.TotalAssists => line.TotalAssists,
        AwardStatBasis.TotalMinutes => line.TotalMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown award stat basis."),
    };
}
