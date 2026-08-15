using System.Security.Cryptography;

namespace BallGM.Domain.Common;

/// <summary>
/// Generates ULID-shaped identifiers: a 48-bit millisecond timestamp followed by 80 bits of
/// randomness, Crockford base32 encoded to 26 characters. Chosen so entity identifiers
/// (<see cref="Domain.Teams.TeamId"/>, <see cref="Domain.Players.PlayerId"/>, and similar) sort
/// in creation order without a coordinating sequence, which matters for auditable transaction
/// ledgers and for data packs/mod tooling that mint identifiers offline.
/// </summary>
public static class SortableId
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TimestampChars = 10;
    private const int RandomnessChars = 16;

    public static string NewId() => NewId(DateTimeOffset.UtcNow);

    public static string NewId(DateTimeOffset timestamp)
    {
        var milliseconds = timestamp.ToUnixTimeMilliseconds();
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp, "Timestamp cannot be before the Unix epoch.");
        }

        Span<byte> randomness = stackalloc byte[10];
        RandomNumberGenerator.Fill(randomness);

        Span<char> chars = stackalloc char[TimestampChars + RandomnessChars];
        WriteTimestamp((ulong)milliseconds, chars[..TimestampChars]);
        WriteChunk(randomness[..5], chars.Slice(TimestampChars, 8));
        WriteChunk(randomness[5..], chars.Slice(TimestampChars + 8, 8));

        return new string(chars);
    }

    private static void WriteTimestamp(ulong milliseconds, Span<char> destination)
    {
        for (var i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = CrockfordAlphabet[(int)(milliseconds & 0x1F)];
            milliseconds >>= 5;
        }
    }

    private static void WriteChunk(ReadOnlySpan<byte> fiveBytes, Span<char> eightChars)
    {
        var value =
            ((ulong)fiveBytes[0] << 32) |
            ((ulong)fiveBytes[1] << 24) |
            ((ulong)fiveBytes[2] << 16) |
            ((ulong)fiveBytes[3] << 8) |
            fiveBytes[4];

        for (var i = eightChars.Length - 1; i >= 0; i--)
        {
            eightChars[i] = CrockfordAlphabet[(int)(value & 0x1F)];
            value >>= 5;
        }
    }
}
