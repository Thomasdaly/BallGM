namespace BallGM.Domain.Randomness;

public interface IRandomSource
{
    int NextInt32(int minInclusive, int maxExclusive);
}
