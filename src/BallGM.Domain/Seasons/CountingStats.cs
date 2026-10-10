namespace BallGM.Domain.Seasons;

/// <summary>
/// Every counting statistic the simulation credits a player with, summed over some run of games —
/// one game, a season, a career. The one shape every stat surface reads, so a figure the engine
/// counts cannot be dropped on its way to a screen by a read model that happened to pick a subset.
/// Rates are never stored here: they are derived from these totals by <c>StatFormulas</c>.
/// </summary>
public sealed record CountingStats(
    int Games,
    int Starts,
    int Minutes,
    int Points,
    int FieldGoalsMade,
    int FieldGoalsAttempted,
    int ThreePointsMade,
    int ThreePointsAttempted,
    int FreeThrowsMade,
    int FreeThrowsAttempted,
    int OffensiveRebounds,
    int DefensiveRebounds,
    int Assists,
    int PlusMinus,
    int Steals,
    int Turnovers,
    int Blocks)
{
    public static CountingStats None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public int Rebounds => OffensiveRebounds + DefensiveRebounds;

    public int TwoPointsMade => FieldGoalsMade - ThreePointsMade;

    public int TwoPointsAttempted => FieldGoalsAttempted - ThreePointsAttempted;

    /// <summary>One game's line as totals over one game.</summary>
    public static CountingStats Of(PlayerStatLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new CountingStats(
            1,
            line.Started ? 1 : 0,
            line.Minutes,
            line.Points,
            line.FieldGoalsMade,
            line.FieldGoalsAttempted,
            line.ThreePointsMade,
            line.ThreePointsAttempted,
            line.FreeThrowsMade,
            line.FreeThrowsAttempted,
            line.OffensiveRebounds,
            line.DefensiveRebounds,
            line.Assists,
            line.PlusMinus,
            line.Steals,
            line.Turnovers,
            line.Blocks);
    }

    /// <summary>The totals over every supplied game line.</summary>
    public static CountingStats Sum(IEnumerable<PlayerStatLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Sum(lines.Select(Of));
    }

    /// <summary>The totals over several runs of games.</summary>
    public static CountingStats Sum(IEnumerable<CountingStats> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return runs.Aggregate(None, (total, run) => total + run);
    }

    /// <summary>The one place every field is added: a new counting stat is added here and in <see cref="Of"/>, nowhere else.</summary>
    public static CountingStats operator +(CountingStats left, CountingStats right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new CountingStats(
            left.Games + right.Games,
            left.Starts + right.Starts,
            left.Minutes + right.Minutes,
            left.Points + right.Points,
            left.FieldGoalsMade + right.FieldGoalsMade,
            left.FieldGoalsAttempted + right.FieldGoalsAttempted,
            left.ThreePointsMade + right.ThreePointsMade,
            left.ThreePointsAttempted + right.ThreePointsAttempted,
            left.FreeThrowsMade + right.FreeThrowsMade,
            left.FreeThrowsAttempted + right.FreeThrowsAttempted,
            left.OffensiveRebounds + right.OffensiveRebounds,
            left.DefensiveRebounds + right.DefensiveRebounds,
            left.Assists + right.Assists,
            left.PlusMinus + right.PlusMinus,
            left.Steals + right.Steals,
            left.Turnovers + right.Turnovers,
            left.Blocks + right.Blocks);
    }
}
