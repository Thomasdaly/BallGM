namespace BallGM.Domain.Common;

/// <summary>Whole-number shares of a whole total, in proportion to some weights: the largest-remainder method.</summary>
public static class Apportion
{
    /// <summary>
    /// Whole shares of <paramref name="total"/> in proportion to numerator / denominator, summing
    /// exactly to it: floor every share, then hand the leftover units, one each, to the largest
    /// remainders (ties to the earlier index). Callers pass numerators that sum to exactly
    /// total × denominator, so flooring only ever leaves a shortfall, never a surplus.
    /// </summary>
    public static int[] LargestRemainder(long[] numerators, long denominator, int total)
    {
        ArgumentNullException.ThrowIfNull(numerators);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        var count = numerators.Length;
        var shares = new int[count];
        var remainders = new long[count];
        var assigned = 0;

        for (var index = 0; index < count; index++)
        {
            shares[index] = (int)(numerators[index] / denominator);
            remainders[index] = numerators[index] % denominator;
            assigned += shares[index];
        }

        for (var leftover = total - assigned; leftover > 0; leftover--)
        {
            var best = -1;
            for (var index = 0; index < count; index++)
            {
                if (remainders[index] >= 0 && (best < 0 || remainders[index] > remainders[best]))
                {
                    best = index;
                }
            }

            shares[best]++;
            remainders[best] = -1;
        }

        return shares;
    }
}
