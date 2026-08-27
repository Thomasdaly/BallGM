using BallGM.Domain.Common;

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
/// <para>
/// A <see cref="RoundCount"/> of zero means the league holds no draft at all — players arrive by
/// open signing. That is a real league shape, not a degenerate configuration: a league with no draft
/// does not run a nought-round one, and no franchise in it can be handed a pick, because no draft
/// will ever select with it. <see cref="HasDraft"/> is the question every caller should ask.
/// </para>
/// </summary>
public sealed record DraftRules
{
    private const string InvalidRoundCountCode = "ruleset.invalid_draft_round_count";
    private const string InvalidHorizonCode = "ruleset.invalid_tradable_draft_horizon";
    private const string InvalidRetainedRoundCode = "ruleset.invalid_retained_round";
    private const string InvalidRetainedIntervalCode = "ruleset.invalid_retained_round_interval";
    private const string RestrictionsWithoutDraftCode = "ruleset.draft_restrictions_without_draft";

    private DraftRules(
        int roundCount,
        bool lotteryEnabled,
        int tradableFutureDraftHorizon,
        int retainedRoundNumber,
        int retainedRoundInterval)
    {
        RoundCount = roundCount;
        LotteryEnabled = lotteryEnabled;
        TradableFutureDraftHorizon = tradableFutureDraftHorizon;
        RetainedRoundNumber = retainedRoundNumber;
        RetainedRoundInterval = retainedRoundInterval;
    }

    /// <summary>A league that holds no draft. Every restriction below is moot, so every one of them is zero.</summary>
    public static DraftRules NoDraft { get; } = new(0, false, 0, 0, 0);

    /// <summary>
    /// Builds the draft structure. Returns a structured failure rather than throwing, because every
    /// value here comes from an editable ruleset file: an inconsistent draft configuration is
    /// untrusted input, not a caller bug.
    /// </summary>
    public static DomainOperationResult<DraftRules> Create(
        int roundCount,
        bool lotteryEnabled,
        int tradableFutureDraftHorizon,
        int retainedRoundNumber,
        int retainedRoundInterval)
    {
        var errors = new List<DomainError>();

        if (roundCount < 0)
        {
            errors.Add(new DomainError(
                InvalidRoundCountCode,
                $"The draft round count cannot be negative, but was {roundCount}. Zero means this league holds no draft."));
            return DomainOperationResult<DraftRules>.Failure(errors.ToArray());
        }

        if (roundCount == 0)
        {
            // No draft, so there is nothing to restrict. Restriction values set anyway are a
            // contradiction in the file rather than something to quietly ignore.
            if (tradableFutureDraftHorizon != 0 || retainedRoundNumber != 0 || retainedRoundInterval != 0 || lotteryEnabled)
            {
                errors.Add(new DomainError(
                    RestrictionsWithoutDraftCode,
                    "This ruleset holds no draft (a round count of zero) but still configures draft restrictions or a lottery. Leave them out, or give the league a draft."));

                return DomainOperationResult<DraftRules>.Failure(errors.ToArray());
            }

            return DomainOperationResult<DraftRules>.Success(NoDraft);
        }

        if (tradableFutureDraftHorizon <= 0)
        {
            errors.Add(new DomainError(
                InvalidHorizonCode,
                $"The tradable future-draft horizon must be positive in a league that holds a draft, but was {tradableFutureDraftHorizon}."));
        }

        if (retainedRoundNumber < 1 || retainedRoundNumber > roundCount)
        {
            errors.Add(new DomainError(
                InvalidRetainedRoundCode,
                $"The retained round must be a round the draft actually has (1 to {roundCount}), but was {retainedRoundNumber}."));
        }

        if (retainedRoundInterval < 1)
        {
            errors.Add(new DomainError(
                InvalidRetainedIntervalCode,
                $"The retained-round interval must be at least 1, but was {retainedRoundInterval}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<DraftRules>.Failure(errors.ToArray())
            : DomainOperationResult<DraftRules>.Success(new DraftRules(
                roundCount,
                lotteryEnabled,
                tradableFutureDraftHorizon,
                retainedRoundNumber,
                retainedRoundInterval));
    }

    public int RoundCount { get; }

    /// <summary>Whether this league drafts at all. False means no pick can be registered or traded.</summary>
    public bool HasDraft => RoundCount > 0;

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
