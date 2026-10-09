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
    int Assists)
{
    public static CountingStats None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

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
            line.Assists);
    }

    /// <summary>The totals over every supplied game line — accumulated in place, one record at the end.</summary>
    public static CountingStats Sum(IEnumerable<PlayerStatLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var total = default(Accumulator);
        foreach (var line in lines)
        {
            total.Add(1, line.Started ? 1 : 0, line.Minutes, line.Points, line.FieldGoalsMade, line.FieldGoalsAttempted,
                line.ThreePointsMade, line.ThreePointsAttempted, line.FreeThrowsMade, line.FreeThrowsAttempted,
                line.OffensiveRebounds, line.DefensiveRebounds, line.Assists);
        }

        return total.ToStats();
    }

    /// <summary>The totals over several runs of games.</summary>
    public static CountingStats Sum(IEnumerable<CountingStats> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var total = default(Accumulator);
        foreach (var run in runs)
        {
            total.Add(run.Games, run.Starts, run.Minutes, run.Points, run.FieldGoalsMade, run.FieldGoalsAttempted,
                run.ThreePointsMade, run.ThreePointsAttempted, run.FreeThrowsMade, run.FreeThrowsAttempted,
                run.OffensiveRebounds, run.DefensiveRebounds, run.Assists);
        }

        return total.ToStats();
    }

    public static CountingStats operator +(CountingStats left, CountingStats right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return Sum([left, right]);
    }

    /// <summary>Running totals for <see cref="Sum(IEnumerable{PlayerStatLine})"/>: no record per line, one at the end.</summary>
    private struct Accumulator
    {
        private int _games, _starts, _minutes, _points, _fgm, _fga, _tpm, _tpa, _ftm, _fta, _oreb, _dreb, _ast;

        public void Add(int games, int starts, int minutes, int points, int fgm, int fga, int tpm, int tpa, int ftm, int fta, int oreb, int dreb, int ast)
        {
            _games += games;
            _starts += starts;
            _minutes += minutes;
            _points += points;
            _fgm += fgm;
            _fga += fga;
            _tpm += tpm;
            _tpa += tpa;
            _ftm += ftm;
            _fta += fta;
            _oreb += oreb;
            _dreb += dreb;
            _ast += ast;
        }

        public readonly CountingStats ToStats() =>
            new(_games, _starts, _minutes, _points, _fgm, _fga, _tpm, _tpa, _ftm, _fta, _oreb, _dreb, _ast);
    }
}
