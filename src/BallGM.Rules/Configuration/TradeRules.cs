using BallGM.Domain.Common;
using BallGM.Domain.Trades;

namespace BallGM.Rules.Configuration;

/// <summary>
/// What a league allows in a trade. Every value here is configuration in the ruleset file rather
/// than a rule compiled into the build, and named for what it does rather than after any real-world
/// agreement's term for it.
/// <para>
/// Deliberately the smallest coherent set: a team over the cap must send out salary comparable to
/// what it takes back, no team may finish above the hard cap, the strictest band may be barred from
/// adding salary at all, and injured players are tradeable or not by configuration. Explicitly
/// deferred rather than half-built — trade exceptions and traded-player exceptions, sign-and-trade,
/// cash considerations, aggregation windows and waiting periods after a signing, and no-trade
/// clauses. Each of those needs state this build does not keep yet.
/// </para>
/// </summary>
public sealed record TradeRules
{
    private const string InvalidPercentCode = "ruleset.invalid_salary_match_percent";
    private const string UnknownEligibilityCode = "ruleset.unknown_injured_player_eligibility";
    private const string AllowanceWithoutMatchingCode = "ruleset.salary_match_allowance_without_matching";

    private TradeRules(
        int? salaryMatchPercent,
        Money salaryMatchAllowance,
        InjuredPlayerTradeEligibility injuredPlayerEligibility,
        bool secondApronBlocksSalaryIncrease)
    {
        SalaryMatchPercent = salaryMatchPercent;
        SalaryMatchAllowance = salaryMatchAllowance;
        InjuredPlayerEligibility = injuredPlayerEligibility;
        SecondApronBlocksSalaryIncrease = secondApronBlocksSalaryIncrease;
    }

    /// <summary>
    /// How much salary a team over the cap may take back, as a percentage of what it sends out. 100
    /// means "no more than you send"; a larger figure gives the deal room to breathe.
    /// <c>null</c> means this league has no salary-matching rule at all — see
    /// <see cref="HasSalaryMatching"/>.
    /// </summary>
    public int? SalaryMatchPercent { get; }

    /// <summary>Whether this league restricts how much salary a team may take back in a trade.</summary>
    public bool HasSalaryMatching => SalaryMatchPercent is not null;

    /// <summary>A flat amount allowed on top of the percentage, so small trades are not blocked by rounding.</summary>
    public Money SalaryMatchAllowance { get; }

    public InjuredPlayerTradeEligibility InjuredPlayerEligibility { get; }

    /// <summary>Whether a team finishing above the second apron may take on more salary than it sends.</summary>
    public bool SecondApronBlocksSalaryIncrease { get; }

    /// <summary>
    /// Builds the trade rules.
    /// <para>
    /// <paramref name="salaryMatchPercent"/> distinguishes absence from zero, and the two readings
    /// are different rules: <c>null</c> — the field left out of the file — means this league does
    /// not match salary at all, while a value that is present and below 100 stays what it always
    /// was, a typo. Nothing coherent is said by a ruleset demanding a team send out more than it
    /// takes back on every trade, so that reading is rejected exactly as before; what changes is
    /// that "the rule does not apply here" now has a way to say so, rather than arriving as the
    /// zero an omitted JSON number deserializes to.
    /// </para>
    /// </summary>
    public static DomainOperationResult<TradeRules> Create(
        int? salaryMatchPercent,
        Money? salaryMatchAllowance,
        InjuredPlayerTradeEligibility injuredPlayerEligibility,
        bool secondApronBlocksSalaryIncrease)
    {
        var allowance = salaryMatchAllowance ?? Money.Zero;

        if (salaryMatchPercent is null && allowance.SmallestUnits != 0)
        {
            return DomainOperationResult<TradeRules>.Failure(new DomainError(
                AllowanceWithoutMatchingCode,
                "This ruleset configures a salary-match allowance but no salary-match percentage, so there is no rule for the allowance to soften. Set a percentage, or leave the allowance out."));
        }

        // Below 100% a team would have to send out more than it takes back on every single trade,
        // which no coherent ruleset means to say — it is a typo in a data pack, not a rule.
        if (salaryMatchPercent is < 100)
        {
            return DomainOperationResult<TradeRules>.Failure(new DomainError(
                InvalidPercentCode,
                $"The salary-match percentage must be at least 100, but was {salaryMatchPercent}. Leave the field out entirely if this league does not match salary."));
        }

        if (!Enum.IsDefined(injuredPlayerEligibility))
        {
            return DomainOperationResult<TradeRules>.Failure(new DomainError(
                UnknownEligibilityCode,
                $"'{injuredPlayerEligibility}' is not an injured-player trade eligibility this build knows."));
        }

        return DomainOperationResult<TradeRules>.Success(new TradeRules(
            salaryMatchPercent,
            allowance,
            injuredPlayerEligibility,
            secondApronBlocksSalaryIncrease));
    }

    /// <summary>
    /// Parses the eligibility as it appears in a ruleset file. Stored as a name rather than a number
    /// so a save stays readable — and stays valid — if the enum gains members later.
    /// </summary>
    public static DomainOperationResult<InjuredPlayerTradeEligibility> ParseEligibility(string value)
    {
        return Enum.TryParse<InjuredPlayerTradeEligibility>(value, out var parsed) && Enum.IsDefined(parsed)
            ? DomainOperationResult<InjuredPlayerTradeEligibility>.Success(parsed)
            : DomainOperationResult<InjuredPlayerTradeEligibility>.Failure(new DomainError(
                UnknownEligibilityCode,
                $"'{value}' is not an injured-player trade eligibility this build knows. Expected one of: {string.Join(", ", Enum.GetNames<InjuredPlayerTradeEligibility>())}."));
    }
}
