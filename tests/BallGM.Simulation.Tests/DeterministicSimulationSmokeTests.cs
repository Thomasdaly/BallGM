using BallGM.Domain.Randomness;
using BallGM.Simulation.Smoke;

namespace BallGM.Simulation.Tests;

public sealed class DeterministicSimulationSmokeTests
{
    [Fact]
    public void GenerateOutcomeSignatureReturnsSameValuesForSameSeed()
    {
        var simulation = new DeterministicSimulationSmoke();

        var firstRun = simulation.GenerateOutcomeSignature(new SeededRandomSource(seed: 42), samples: 8);
        var secondRun = simulation.GenerateOutcomeSignature(new SeededRandomSource(seed: 42), samples: 8);

        Assert.Equal(firstRun, secondRun);
        Assert.Equal(new[] { 5413, 2291, 3858, 5764, 3250, 9062, 4925, 5908 }, firstRun);
    }

    [Fact]
    public void NextInt32RejectsEmptyRange()
    {
        var random = new SeededRandomSource(seed: 42);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt32(7, 7));
    }
}
