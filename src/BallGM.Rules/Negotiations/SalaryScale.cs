using BallGM.Domain.Players;

namespace BallGM.Rules.Negotiations;

/// <summary>
/// Where a player ranks in the league he would be paid in, for his asking price
/// (<c>docs/franchise-realism.md</c> → F3). A real market pays by rank, not by an absolute number: the
/// league's dozen best players are on the maximum whatever scale their ratings are written in, a
/// rotation player earns a modest share, and the bottom of every roster plays for the minimum. Keyed on
/// rank, a data pack on its own rating scale gets the same salary shape, and a league whose ratings
/// drift over a long save keeps paying its stars like stars.
/// </summary>
public sealed class SalaryScale
{
    /// <summary>
    /// Share of the way from the minimum to the maximum (per cent) by how far from the top a player
    /// ranks (per mille of the league): the top 2% ask the maximum, the top 6% four fifths of it, the top
    /// 12% nearly three fifths, a top-quarter player about a third, and the bottom 40% the minimum —
    /// the real league's salary shape, where about a dozen players are on the maximum and the median
    /// player earns a small multiple of the minimum.
    /// </summary>
    private static readonly (int TopPerMille, int Share)[] Knots = [(20, 100), (60, 80), (120, 58), (250, 30), (400, 12), (600, 3), (650, 0)];

    private readonly int[] _descending;

    private SalaryScale(int[] descending) => _descending = descending;

    /// <summary>The scale of a league from its players' ratings (rostered players: the league as it plays).</summary>
    public static SalaryScale From(IEnumerable<Player> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        return new SalaryScale(players.Select(player => player.Rating.Overall).OrderDescending().ToArray());
    }

    public int Count => _descending.Length;

    /// <summary>
    /// The player's share (per cent) of the way from the minimum to the maximum: interpolated on how
    /// many players in the league rate strictly above him, so equal ratings ask equal prices.
    /// </summary>
    public int ShareFor(int overall)
    {
        if (_descending.Length == 0)
        {
            return 0;
        }

        var above = Array.FindIndex(_descending, value => value <= overall);
        above = above < 0 ? _descending.Length : above;
        var topPerMille = above * 1_000 / _descending.Length;
        var knots = Knots;
        if (topPerMille <= knots[0].TopPerMille)
        {
            return knots[0].Share;
        }

        for (var index = 1; index < knots.Length; index++)
        {
            var (x1, y1) = knots[index];
            if (topPerMille <= x1)
            {
                var (x0, y0) = knots[index - 1];
                return y0 + ((topPerMille - x0) * (y1 - y0) / (x1 - x0));
            }
        }

        return 0;
    }
}
