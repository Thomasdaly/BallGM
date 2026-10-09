namespace BallGM.Domain.Randomness;

public interface IRandomSource
{
    /// <summary>The range of <see cref="NextBasisPoint"/>: every probability in the simulation is in basis points.</summary>
    const int BasisPointScale = 10_000;

    int NextInt32(int minInclusive, int maxExclusive);

    /// <summary>A draw in [0, <see cref="BasisPointScale"/>): one probability decision. An implementation may provide a faster path; it must return exactly this.</summary>
    int NextBasisPoint() => NextInt32(0, BasisPointScale);
}
