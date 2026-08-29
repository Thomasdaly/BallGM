namespace BallGM.Domain.Randomness;

/// <summary>
/// Derives one seed from another plus a name.
/// <para>
/// This is what makes a season reproducible without a running random stream. If every game drew
/// from one long sequence, simulating game 400 would depend on how many games had been simulated
/// before it — so a save resumed mid-season, or a season advanced a week at a time rather than in
/// one go, would produce different results from the same seed. Deriving each game's seed from the
/// season seed and the game's own identifier removes the dependency entirely: game 400 is the same
/// game whether it is the first thing simulated after a load or the four-hundredth in a single run.
/// </para>
/// <para>
/// Pure integer arithmetic over the UTF-8 bytes of the name, so the same inputs give the same
/// answer on every platform. It sits beside <see cref="SeededRandomSource"/> for the same reason
/// that does: everything composing a seeded run has to be able to reach it, and a dependency-free
/// arithmetic primitive is not simulation-specific.
/// </para>
/// </summary>
public static class SeedMixer
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static int Mix(int seed, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var hash = FnvOffsetBasis ^ (uint)seed;

        foreach (var value in System.Text.Encoding.UTF8.GetBytes(name))
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        // A final avalanche so that names differing in one byte do not produce neighbouring seeds,
        // which would otherwise make two adjacent games' random sequences visibly correlated.
        hash ^= hash >> 33;
        hash *= 0xFF51AFD7ED558CCDUL;
        hash ^= hash >> 33;

        return unchecked((int)(uint)(hash & 0x7FFFFFFFUL));
    }
}
