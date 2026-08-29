namespace BallGM.Domain.Seasons;

/// <summary>
/// Games won and lost, and nothing derived stored alongside them.
/// <para>
/// Records are compared as a cross-multiplication rather than by a win percentage, deliberately:
/// two teams on different numbers of games would otherwise be ranked by a rounded fraction, and the
/// place a rounding lands is not a rule any league wrote down. Integers all the way through means
/// the order is the same on every platform, which is the same reason cap money is never a double.
/// </para>
/// </summary>
public readonly record struct TeamRecord(int Wins, int Losses)
{
    public static TeamRecord None { get; }

    public int Games => Wins + Losses;

    public TeamRecord Won() => new(Wins + 1, Losses);

    public TeamRecord Lost() => new(Wins, Losses + 1);

    /// <summary>
    /// Compares two records by winning ratio without dividing. Positive means this record is the
    /// better one. A record with no games played compares as worse than any record with a win and
    /// better than any record with only losses, which is what "nothing to judge yet" has to mean in
    /// a table that still has to be ordered.
    /// </summary>
    public int CompareTo(TeamRecord other)
    {
        if (Games == 0 && other.Games == 0)
        {
            return 0;
        }

        if (Games == 0)
        {
            return other.Wins > 0 ? -1 : 1;
        }

        if (other.Games == 0)
        {
            return Wins > 0 ? 1 : -1;
        }

        return ((long)Wins * other.Games).CompareTo((long)other.Wins * Games);
    }

    public override string ToString() => $"{Wins}-{Losses}";
}
