using BallGM.Domain.Franchises;

namespace BallGM.Domain.DraftAssets;

public sealed record PickEncumbranceId
{
    public PickEncumbranceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Something riding on a pick that has not resolved yet. Encumbrances live on
/// <see cref="PickOwnership"/> — the mutable side of the asset — never on <see cref="DraftPick"/>,
/// which is identity and cannot change.
/// </summary>
public abstract record PickEncumbrance
{
    private protected PickEncumbrance(PickEncumbranceId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    public PickEncumbranceId Id { get; }
}

/// <summary>
/// A promise to hand this pick to another franchise, subject to a protection. The obligation — not
/// the pick — is what rolls: when a protected pick fails to convey, this obligation moves to the
/// following draft's pick for the same original franchise and round, with
/// <see cref="ScheduleIndex"/> advanced one step. The pick it left behind is simply kept.
/// </summary>
public sealed record PickObligation : PickEncumbrance
{
    public PickObligation(
        PickEncumbranceId id,
        FranchiseId beneficiaryFranchiseId,
        PickProtection protection,
        int scheduleIndex = 0)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(beneficiaryFranchiseId);
        ArgumentNullException.ThrowIfNull(protection);

        if (scheduleIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduleIndex),
                scheduleIndex,
                "An obligation's schedule index cannot be negative.");
        }

        BeneficiaryFranchiseId = beneficiaryFranchiseId;
        Protection = protection;
        ScheduleIndex = scheduleIndex;
    }

    /// <summary>Who receives the pick if it conveys.</summary>
    public FranchiseId BeneficiaryFranchiseId { get; }

    public PickProtection Protection { get; }

    /// <summary>
    /// How far into the protection schedule this obligation already is. Zero on the draft it was
    /// written for; one after a single rollover; equal to the schedule length once the schedule is
    /// spent and the obligation is riding its fallback unprotected.
    /// </summary>
    public int ScheduleIndex { get; }

    /// <summary>The top-N level for the draft this obligation currently sits on, or <c>null</c> if unprotected.</summary>
    public int? CurrentProtectionLevel => Protection.LevelAt(ScheduleIndex);

    public bool HasRemainingSchedule => ScheduleIndex + 1 < Protection.ScheduleLength;

    /// <summary>The same obligation, one draft further along its schedule.</summary>
    public PickObligation RolledForward() =>
        new(Id, BeneficiaryFranchiseId, Protection, ScheduleIndex + 1);

    /// <summary>The same obligation with its protection spent, as the "conveys unprotected" fallback leaves it.</summary>
    public PickObligation Unprotected() =>
        new(Id, BeneficiaryFranchiseId, Protection, Protection.ScheduleLength);
}

/// <summary>
/// The right to take this pick's selection in exchange for another one. The right sits on the pick
/// that may be taken and names the franchise holding it, plus the pick offered in exchange — so a
/// swap is always a two-asset statement rather than a flag on one of them.
/// <para>
/// Multi-team routing (a swap whose counterpart is itself owed elsewhere) is deliberately not
/// modelled at this milestone; it is named in <c>docs/architecture.md</c> as deferred rather than
/// half-built.
/// </para>
/// </summary>
public sealed record SwapRight : PickEncumbrance
{
    public SwapRight(PickEncumbranceId id, FranchiseId holderFranchiseId, DraftPickId counterpartPickId)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(holderFranchiseId);
        ArgumentNullException.ThrowIfNull(counterpartPickId);

        HolderFranchiseId = holderFranchiseId;
        CounterpartPickId = counterpartPickId;
    }

    /// <summary>The franchise that may exercise the swap.</summary>
    public FranchiseId HolderFranchiseId { get; }

    /// <summary>The selection the holder gives up if it does.</summary>
    public DraftPickId CounterpartPickId { get; }
}
