namespace BallGM.Domain.Randomness;

/// <summary>
/// The deterministic <see cref="IRandomSource"/>: the same seed produces the same sequence, on every
/// platform and every run. A splitmix64 state advance, which is chosen for being reproducible and
/// dependency-free rather than for statistical excellence.
/// <para>
/// It sits next to the interface in Domain rather than in the simulation project because everything
/// that composes a seeded run needs to construct one — the free-agency market's tie-break arrives
/// through the Infrastructure composition root, which does not reference the simulation project and
/// should not have to. A pure arithmetic primitive with no dependencies is not simulation-specific.
/// </para>
/// </summary>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private ulong _state = (uint)seed;

    public int NextInt32(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                "Maximum must be greater than minimum.");
        }

        var range = (ulong)((long)maxExclusive - minInclusive);
        var rejectionThreshold = (0UL - range) % range;
        ulong sample;

        do
        {
            sample = NextUInt64();
        }
        while (sample < rejectionThreshold);

        return checked((int)(minInclusive + (long)(sample % range)));
    }

    private const ulong BasisPointRange = IRandomSource.BasisPointScale;
    private const ulong BasisPointRejectionThreshold = unchecked(0UL - BasisPointRange) % BasisPointRange;

    /// <summary>
    /// A draw in [0, 10 000) — exactly <c>NextInt32(0, 10_000)</c>, the same rejection threshold and
    /// the same sequence, but with a constant range the JIT turns into multiplications. A match draws
    /// one of these per probability decision, thousands a game.
    /// </summary>
    public int NextBasisPoint()
    {
        ulong sample;

        do
        {
            sample = NextUInt64();
        }
        while (sample < BasisPointRejectionThreshold);

        return (int)(sample % BasisPointRange);
    }

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var value = _state;

        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;

        return value ^ (value >> 31);
    }
}
