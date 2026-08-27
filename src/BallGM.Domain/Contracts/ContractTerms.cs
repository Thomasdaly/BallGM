using BallGM.Domain.Common;

namespace BallGM.Domain.Contracts;

/// <summary>
/// The shape rules a run of contract seasons has to satisfy, in one place: at least one season,
/// consecutive years, no season twice, and no season guaranteeing more than it pays.
/// <para>
/// Shared by <see cref="Contract"/> and by <c>BallGM.Domain.Negotiations.Offer</c>, because an offer
/// is a proposed contract and the two must not be able to disagree about what a legal run of seasons
/// looks like. An offer that passes a check the contract it becomes would fail is an offer that
/// cannot be accepted, which is a worse bug than an offer refused early. The rule codes say
/// <c>contract.</c> in both cases on purpose: they name the shape, not the container it arrived in.
/// </para>
/// </summary>
public static class ContractTerms
{
    public const string NoSeasonsCode = "contract.no_seasons";
    public const string DuplicateSeasonCode = "contract.duplicate_season";
    public const string NonContiguousSeasonsCode = "contract.seasons_not_contiguous";
    public const string GuaranteeExceedsCompensationCode = "contract.guarantee_exceeds_compensation";

    /// <summary>
    /// Orders the terms by season and checks the run. Structural nulls throw — a caller bug — while
    /// everything a data pack or an offer screen can legitimately produce comes back as a failure.
    /// </summary>
    public static DomainOperationResult<IReadOnlyList<ContractSeasonTerm>> Normalize(
        IEnumerable<ContractSeasonTerm> terms,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var termList = terms.ToList();
        if (termList.Any(term => term is null))
        {
            throw new ArgumentException("Contract terms cannot contain null seasons.", parameterName);
        }

        if (termList.Count == 0)
        {
            return DomainOperationResult<IReadOnlyList<ContractSeasonTerm>>.Failure(
                new DomainError(NoSeasonsCode, "A contract must cover at least one season."));
        }

        termList = termList.OrderBy(term => term.Season.Year).ToList();
        var errors = new List<DomainError>();

        for (var index = 1; index < termList.Count; index++)
        {
            var previousYear = termList[index - 1].Season.Year;
            var year = termList[index].Season.Year;

            if (year == previousYear)
            {
                errors.Add(new DomainError(
                    DuplicateSeasonCode,
                    $"A contract cannot carry two terms for season {year}."));
            }
            else if (year != previousYear + 1)
            {
                errors.Add(new DomainError(
                    NonContiguousSeasonsCode,
                    $"Contract seasons must run consecutively: season {year} does not follow season {previousYear}."));
            }
        }

        foreach (var term in termList.Where(term => term.GuaranteedAmount > term.Compensation))
        {
            errors.Add(new DomainError(
                GuaranteeExceedsCompensationCode,
                $"Season {term.Season.Year} guarantees {term.GuaranteedAmount.SmallestUnits}, which is more than its compensation of {term.Compensation.SmallestUnits}."));
        }

        return errors.Count > 0
            ? DomainOperationResult<IReadOnlyList<ContractSeasonTerm>>.Failure(errors.ToArray())
            : DomainOperationResult<IReadOnlyList<ContractSeasonTerm>>.Success(termList);
    }
}
