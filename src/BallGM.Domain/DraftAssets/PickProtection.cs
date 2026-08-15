using BallGM.Domain.Common;

namespace BallGM.Domain.DraftAssets;

/// <summary>What becomes of an obligation once its protection schedule runs out.</summary>
public enum PickProtectionFallbackKind
{
    /// <summary>The obligation rolls one more draft and conveys there regardless of where it lands.</summary>
    ConveysUnprotected = 0,

    /// <summary>The obligation is replaced by an unprotected obligation on a later round.</summary>
    ConvertsToRound = 1,

    /// <summary>The obligation ends. The pick stays where it is, permanently.</summary>
    Extinguishes = 2,
}

/// <summary>
/// The stated end of a protection schedule. A schedule with no terminal outcome is the bug this
/// type exists to make impossible: an obligation that can neither convey nor die rolls forever.
/// </summary>
public sealed record PickProtectionFallback
{
    private const string MissingRoundCode = "pick_protection.fallback_round_missing";
    private const string UnexpectedRoundCode = "pick_protection.fallback_round_not_applicable";

    private PickProtectionFallback(PickProtectionFallbackKind kind, int? convertsToRound)
    {
        Kind = kind;
        ConvertsToRound = convertsToRound;
    }

    public PickProtectionFallbackKind Kind { get; }

    /// <summary>The round the obligation converts to, and only set when it does.</summary>
    public int? ConvertsToRound { get; }

    public static PickProtectionFallback ConveysUnprotected { get; } =
        new(PickProtectionFallbackKind.ConveysUnprotected, null);

    public static PickProtectionFallback Extinguishes { get; } =
        new(PickProtectionFallbackKind.Extinguishes, null);

    public static DomainOperationResult<PickProtectionFallback> ConvertsToLaterRound(int round)
    {
        if (round < 1)
        {
            return DomainOperationResult<PickProtectionFallback>.Failure(
                new DomainError(MissingRoundCode, $"A converting fallback must name a round of 1 or greater, but was {round}."));
        }

        return DomainOperationResult<PickProtectionFallback>.Success(
            new PickProtectionFallback(PickProtectionFallbackKind.ConvertsToRound, round));
    }

    /// <summary>
    /// Rebuilds a fallback from stored primitives — the shape a save or data pack arrives in. Kept
    /// non-throwing for the same reason the factories are: file content is untrusted, and a
    /// converting fallback with no round named must fail explainably rather than crash a load.
    /// </summary>
    public static DomainOperationResult<PickProtectionFallback> Rebuild(PickProtectionFallbackKind kind, int? convertsToRound)
    {
        if (kind == PickProtectionFallbackKind.ConvertsToRound)
        {
            return convertsToRound is null
                ? DomainOperationResult<PickProtectionFallback>.Failure(
                    new DomainError(MissingRoundCode, "A converting fallback must name the round it converts to."))
                : ConvertsToLaterRound(convertsToRound.Value);
        }

        if (convertsToRound is not null)
        {
            return DomainOperationResult<PickProtectionFallback>.Failure(
                new DomainError(
                    UnexpectedRoundCode,
                    $"A '{kind}' fallback cannot name a round to convert to."));
        }

        return DomainOperationResult<PickProtectionFallback>.Success(new PickProtectionFallback(kind, null));
    }
}

/// <summary>
/// The condition deciding whether an obligation conveys, as an explicit vocabulary rather than a
/// free-form string. Two forms exist, and they are the minimum set that is internally consistent:
/// unprotected, and protected through the top N selections with a rollover schedule that terminates
/// in a stated <see cref="PickProtectionFallback"/>.
/// <para>
/// <see cref="ProtectedSelections"/> is one entry per successive draft — <c>[4, 3]</c> is "top-4
/// protected this draft; if it does not convey, top-3 protected the next one" — and the fallback
/// says what happens after the last entry fails. Seasons are deliberately absent: the draft an
/// obligation currently sits on is the pick it is attached to, so a schedule cannot contradict the
/// asset it rides on.
/// </para>
/// <para>
/// Deliberately not modelled, and not half-built: range protections ("selections 5 through 30"),
/// record- or outcome-conditional protections, cash considerations, and lottery odds. Each of those
/// changes what a protection is evaluated <em>against</em>, not just its numbers, and adding them
/// speculatively here would balloon this milestone. They arrive with the lottery (Milestone 8) and
/// the trade engine (Milestone 5).
/// </para>
/// </summary>
public sealed record PickProtection
{
    private const string EmptyScheduleCode = "pick_protection.empty_schedule";
    private const string InvalidSelectionCode = "pick_protection.invalid_protected_selection";

    private PickProtection(IReadOnlyList<int> protectedSelections, PickProtectionFallback fallback)
    {
        ProtectedSelections = protectedSelections;
        Fallback = fallback;
    }

    /// <summary>
    /// The top-N level for each successive draft the obligation can roll through. Empty means the
    /// obligation is unprotected and conveys wherever the pick lands.
    /// </summary>
    public IReadOnlyList<int> ProtectedSelections { get; }

    public PickProtectionFallback Fallback { get; }

    public bool IsUnprotected => ProtectedSelections.Count == 0;

    /// <summary>The number of drafts this obligation can roll through before the fallback decides it.</summary>
    public int ScheduleLength => ProtectedSelections.Count;

    public static PickProtection Unprotected { get; } =
        new([], PickProtectionFallback.ConveysUnprotected);

    /// <summary>
    /// Builds a top-N protection with a rollover schedule. The schedule may hold or tighten from
    /// draft to draft; it is not required to shrink, because a ruleset or data pack is free to
    /// write a protection this build did not anticipate as long as it still terminates.
    /// </summary>
    public static DomainOperationResult<PickProtection> TopSelections(
        IEnumerable<int> protectedSelections,
        PickProtectionFallback fallback)
    {
        ArgumentNullException.ThrowIfNull(protectedSelections);
        ArgumentNullException.ThrowIfNull(fallback);

        var schedule = protectedSelections.ToArray();
        if (schedule.Length == 0)
        {
            return DomainOperationResult<PickProtection>.Failure(
                new DomainError(
                    EmptyScheduleCode,
                    "A top-selection protection must protect at least one draft. Use PickProtection.Unprotected for a pick that conveys regardless."));
        }

        var invalid = schedule.Where(level => level < 1).ToArray();
        if (invalid.Length > 0)
        {
            return DomainOperationResult<PickProtection>.Failure(
                new DomainError(
                    InvalidSelectionCode,
                    $"Every protected-selection level must be 1 or greater; found {invalid[0]}."));
        }

        return DomainOperationResult<PickProtection>.Success(new PickProtection(schedule, fallback));
    }

    /// <summary>
    /// The top-N level applying to the draft this obligation currently sits on, or <c>null</c> once
    /// the schedule is exhausted — which is the unprotected state a rolled-over obligation reaches
    /// after a <see cref="PickProtectionFallbackKind.ConveysUnprotected"/> fallback.
    /// </summary>
    public int? LevelAt(int scheduleIndex) =>
        scheduleIndex >= 0 && scheduleIndex < ProtectedSelections.Count
            ? ProtectedSelections[scheduleIndex]
            : null;
}
