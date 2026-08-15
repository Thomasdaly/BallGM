namespace BallGM.Rules.Configuration;

/// <summary>
/// How a league's draft is structured, and what it forbids a franchise from trading away.
/// <para>
/// The restriction fields are named for what they do rather than after any real-world league's
/// rule, and the horizon is configured rather than compiled in:
/// <see cref="RetainedRoundNumber"/> names the round a franchise must keep hold of,
/// <see cref="RetainedRoundInterval"/> says how often — an interval of 2 means "a franchise must
/// still control its own pick in that round at least once in any two consecutive future drafts" —
/// and <see cref="TradableFutureDraftHorizon"/> caps how far ahead picks may be dealt at all.
/// A league that wants none of this sets the interval to 1.
/// </para>
/// </summary>
public sealed record DraftRules
{
    public DraftRules(
        int roundCount,
        bool lotteryEnabled,
        int tradableFutureDraftHorizon,
        int retainedRoundNumber,
        int retainedRoundInterval)
    {
        if (roundCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roundCount), roundCount, "Draft round count must be positive.");
        }

        if (tradableFutureDraftHorizon <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tradableFutureDraftHorizon),
                tradableFutureDraftHorizon,
                "The tradable future-draft horizon must be positive.");
        }

        if (retainedRoundNumber < 1 || retainedRoundNumber > roundCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedRoundNumber),
                retainedRoundNumber,
                $"The retained round must be a round the draft actually has (1 to {roundCount}).");
        }

        if (retainedRoundInterval < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedRoundInterval),
                retainedRoundInterval,
                "The retained-round interval must be at least 1.");
        }

        RoundCount = roundCount;
        LotteryEnabled = lotteryEnabled;
        TradableFutureDraftHorizon = tradableFutureDraftHorizon;
        RetainedRoundNumber = retainedRoundNumber;
        RetainedRoundInterval = retainedRoundInterval;
    }

    public int RoundCount { get; }

    public bool LotteryEnabled { get; }

    /// <summary>How many future drafts ahead of the current season a pick may be traded in.</summary>
    public int TradableFutureDraftHorizon { get; }

    /// <summary>The round the retention restriction applies to.</summary>
    public int RetainedRoundNumber { get; }

    /// <summary>
    /// The width of the window the restriction is checked over. Any run of this many consecutive
    /// future drafts must contain at least one in which the franchise still controls its own pick in
    /// <see cref="RetainedRoundNumber"/>.
    /// </summary>
    public int RetainedRoundInterval { get; }
}
