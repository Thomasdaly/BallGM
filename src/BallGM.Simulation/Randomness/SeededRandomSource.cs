using BallGM.Domain.Randomness;

namespace BallGM.Simulation.Randomness;

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

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var value = _state;

        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;

        return value ^ (value >> 31);
    }
}
