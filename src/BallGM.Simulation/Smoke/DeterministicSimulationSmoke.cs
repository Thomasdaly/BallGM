using BallGM.Domain.Randomness;

namespace BallGM.Simulation.Smoke;

public sealed class DeterministicSimulationSmoke
{
    public IReadOnlyList<int> GenerateOutcomeSignature(IRandomSource randomSource, int samples)
    {
        ArgumentNullException.ThrowIfNull(randomSource);

        if (samples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), samples, "Samples must be positive.");
        }

        var signature = new int[samples];

        for (var index = 0; index < signature.Length; index++)
        {
            signature[index] = randomSource.NextInt32(0, 10_000);
        }

        return Array.AsReadOnly(signature);
    }
}
