using BallGM.Domain.Common;
using BallGM.Domain.Leagues;

namespace BallGM.Domain.Contracts;

/// <summary>
/// What one contract owes for one season: the compensation, how much of it is guaranteed if the
/// player is released, and whether the season is optional. Cross-term rules (ordering, contiguity)
/// belong to <see cref="Contract"/>; this type only guards the single-season shape.
/// </summary>
public sealed record ContractSeasonTerm
{
    public ContractSeasonTerm(
        Season season,
        Money compensation,
        Money guaranteedAmount,
        ContractOptionKind option = ContractOptionKind.None,
        ContractOptionStatus optionStatus = ContractOptionStatus.NotApplicable)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(compensation);
        ArgumentNullException.ThrowIfNull(guaranteedAmount);

        if (!Enum.IsDefined(option))
        {
            throw new ArgumentOutOfRangeException(nameof(option), option, "Contract option kind must be a defined value.");
        }

        if (!Enum.IsDefined(optionStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(optionStatus), optionStatus, "Contract option status must be a defined value.");
        }

        Season = season;
        Compensation = compensation;
        GuaranteedAmount = guaranteedAmount;
        Option = option;

        // A season with an option is pending until somebody decides it; a season without one can
        // never be exercised or declined. Callers state the intent, this normalises the pairing.
        OptionStatus = option == ContractOptionKind.None
            ? ContractOptionStatus.NotApplicable
            : optionStatus == ContractOptionStatus.NotApplicable
                ? ContractOptionStatus.Pending
                : optionStatus;
    }

    public Season Season { get; }

    public Money Compensation { get; }

    /// <summary>
    /// The part of <see cref="Compensation"/> still owed after a release. This is the amount that
    /// becomes dead money; the rest simply disappears from the payroll.
    /// </summary>
    public Money GuaranteedAmount { get; }

    public ContractOptionKind Option { get; }

    public ContractOptionStatus OptionStatus { get; }

    public bool IsFullyGuaranteed => GuaranteedAmount.SmallestUnits == Compensation.SmallestUnits;

    /// <summary>An undecided option season is not yet a commitment, so it carries no cap charge.</summary>
    public bool IsPendingOption => OptionStatus == ContractOptionStatus.Pending;

    public bool IsDeclinedOption => OptionStatus == ContractOptionStatus.Declined;

    internal ContractSeasonTerm WithOptionStatus(ContractOptionStatus status) =>
        new(Season, Compensation, GuaranteedAmount, Option, status);
}
