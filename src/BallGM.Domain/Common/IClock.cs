namespace BallGM.Domain.Common;

/// <summary>
/// The current time, injected rather than read from <c>DateTimeOffset.UtcNow</c> at the call site.
/// Anything that stamps a domain record — the transaction ledger above all — takes one of these so
/// a test can assert on exact timestamps and a fixture can produce the same league on every launch.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
