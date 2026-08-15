using BallGM.Domain.Common;

namespace BallGM.Infrastructure.Time;

/// <summary>The wall clock. The only place in the codebase allowed to read the machine's time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// A clock stuck at one instant. Used by the fixture league so a generated ledger is byte-identical
/// on every launch, and by tests that assert on recorded timestamps.
/// </summary>
public sealed class FixedClock(DateTimeOffset instant) : IClock
{
    public DateTimeOffset UtcNow => instant;
}

/// <summary>
/// A clock that advances by a fixed step on every read, so a generated sequence of events gets
/// distinct, ordered, and still perfectly reproducible timestamps. Not thread-safe, and not meant
/// to be: it exists for single-threaded content generation, not for a running game.
/// </summary>
public sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : IClock
{
    private long _reads;

    public DateTimeOffset UtcNow => start + (step * _reads++);
}
