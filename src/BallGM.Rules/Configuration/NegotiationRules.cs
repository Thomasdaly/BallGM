using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;

namespace BallGM.Rules.Configuration;

/// <summary>
/// What a league permits in a contract offer, and how a team may pay for it. The negotiation
/// counterpart to <see cref="TradeRules"/>, and configured the same way: every value is a field in
/// the ruleset file, named for what it does rather than after any real-world agreement's term.
/// <para>
/// Every field is optional by absence, and the whole section may be left out. A league that
/// configures none of this is not a league where nobody may sign — signing is a capability, and it
/// is the <em>routes</em> that gate it. It is an open market: no term limit, no escalation limit,
/// no maximum salary, no league minimum, no over-the-cap allowance (and none needed, because a
/// league with no soft cap has no cap to be over), and offers that do not expire. That is exactly
/// the shape of the uncapped conformance league, and the free-agency market there permits any team
/// to pay anyone anything.
/// </para>
/// <para>
/// Deliberately deferred rather than half-built, and each dated in
/// <c>docs/negotiation-mechanisms.md</c>: the incumbent retention allowance and its tiers, the
/// post-room and periodic allowances, the injury-replacement allowance, traded-salary credit,
/// matching rights and offer sheets, movement-consent clauses, signing bonuses, and performance
/// escalators. Every one of them is a variation on <em>eligibility</em>, so they arrive as more
/// rows in this configuration and more routes in the route table, not as branches inside one.
/// </para>
/// </summary>
public sealed record NegotiationRules
{
    private const string NonPositiveTermCode = "ruleset.non_positive_contract_term";
    private const string IncumbentTermWithoutBaseCode = "ruleset.incumbent_term_without_base_term";
    private const string IncumbentTermBelowBaseCode = "ruleset.incumbent_term_below_base_term";
    private const string NegativeEscalationCode = "ruleset.negative_escalation_percent";
    private const string CeilingWithoutSoftCapCode = "ruleset.ceiling_requires_soft_cap";
    private const string AllowanceWithoutSoftCapCode = "ruleset.allowance_requires_soft_cap";
    private const string AllowanceSettingWithoutAllowanceCode = "ruleset.allowance_setting_without_allowance";
    private const string AllowanceLimitNotConfiguredCode = "ruleset.allowance_limit_threshold_not_configured";
    private const string NonPositiveExpiryCode = "ruleset.non_positive_offer_expiry";
    private const string NonPositiveAllowanceCode = "ruleset.non_positive_allowance";
    private const string HalfSigningWindowCode = "ruleset.half_stated_signing_window";
    private const string SigningWindowInvertedCode = "ruleset.signing_window_closes_before_it_opens";
    private const string NegativeSigningWindowDayCode = "ruleset.negative_signing_window_day";
    private const string NonPositiveShortTermCode = "ruleset.non_positive_short_term_contract_days";

    private NegotiationRules(
        int? maximumContractSeasons,
        int? maximumIncumbentContractSeasons,
        int? maximumAnnualEscalationPercent,
        int? maximumAnnualDeescalationPercent,
        CompensationCeilingScale compensationCeiling,
        CompensationFloorScale compensationFloor,
        Money? standardOverCapAllowance,
        CapThresholdKind? standardOverCapAllowanceUnavailableAbove,
        bool allowanceMaySplitAcrossPlayers,
        MarketResolutionMode marketResolution,
        int? offerExpiryDays,
        int? inSeasonSigningWindowOpensDay,
        int? inSeasonSigningWindowClosesDay,
        int? shortTermContractDays)
    {
        MaximumContractSeasons = maximumContractSeasons;
        MaximumIncumbentContractSeasons = maximumIncumbentContractSeasons;
        MaximumAnnualEscalationPercent = maximumAnnualEscalationPercent;
        MaximumAnnualDeescalationPercent = maximumAnnualDeescalationPercent;
        CompensationCeiling = compensationCeiling;
        CompensationFloor = compensationFloor;
        StandardOverCapAllowance = standardOverCapAllowance;
        StandardOverCapAllowanceUnavailableAbove = standardOverCapAllowanceUnavailableAbove;
        AllowanceMaySplitAcrossPlayers = allowanceMaySplitAcrossPlayers;
        MarketResolution = marketResolution;
        OfferExpiryDays = offerExpiryDays;
        InSeasonSigningWindowOpensDay = inSeasonSigningWindowOpensDay;
        InSeasonSigningWindowClosesDay = inSeasonSigningWindowClosesDay;
        ShortTermContractDays = shortTermContractDays;
    }

    /// <summary>
    /// A league that constrains nothing: any team may offer any player any amount, for any term, at
    /// any escalation. The uncapped conformance league loads to exactly this.
    /// </summary>
    public static NegotiationRules OpenMarket { get; } = new(
        null,
        null,
        null,
        null,
        CompensationCeilingScale.None,
        CompensationFloorScale.None,
        null,
        null,
        false,
        MarketResolutionMode.ResolutionPoint,
        null,
        null,
        null,
        null);

    /// <summary>Longest contract anyone may sign. Absent means the league does not limit term.</summary>
    public int? MaximumContractSeasons { get; }

    /// <summary>
    /// Longest contract a team may offer its own player, where a league lets an incumbent offer more
    /// years than a rival. Absent means incumbents are held to the same limit as everyone else.
    /// </summary>
    public int? MaximumIncumbentContractSeasons { get; }

    /// <summary>Largest season-over-season raise, as a percentage of the first season.</summary>
    public int? MaximumAnnualEscalationPercent { get; }

    /// <summary>Largest season-over-season cut, as a percentage of the first season.</summary>
    public int? MaximumAnnualDeescalationPercent { get; }

    public CompensationCeilingScale CompensationCeiling { get; }

    public CompensationFloorScale CompensationFloor { get; }

    /// <summary>
    /// One generically named fixed allowance a team may use above the soft cap. The single
    /// over-the-cap route this build ships; the rest are dated in the mechanism inventory.
    /// </summary>
    public Money? StandardOverCapAllowance { get; }

    /// <summary>The line above which the allowance is unavailable, where a league sets one.</summary>
    public CapThresholdKind? StandardOverCapAllowanceUnavailableAbove { get; }

    /// <summary>Whether the allowance may be spent on more than one player in a season.</summary>
    public bool AllowanceMaySplitAcrossPlayers { get; }

    public MarketResolutionMode MarketResolution { get; }

    /// <summary>How long an offer stands before it expires. Absent means offers do not expire.</summary>
    public int? OfferExpiryDays { get; }

    /// <summary>
    /// The season day the in-season signing window opens on. Only expressible now that a calendar
    /// exists — before Milestone 7 there was no day for a window to open on. Absent means this
    /// league does not restrict when a signing may happen, which the signing validator reports as a
    /// note rather than passing silently.
    /// </summary>
    public int? InSeasonSigningWindowOpensDay { get; }

    /// <summary>The season day the window closes on, exclusive. Absent alongside the opening day.</summary>
    public int? InSeasonSigningWindowClosesDay { get; }

    /// <summary>
    /// How many days a short-term contract runs for.
    /// <para>
    /// A field on the existing rules rather than a new kind of contract, deliberately. A short-term
    /// deal is an ordinary contract with a stated length in days; making it a second
    /// <c>Contract</c> subtype would give the cap ledger, the trade validator, and every signing
    /// route a second shape to handle for the sake of one number. Absent means this league has no
    /// short-term contract at all.
    /// </para>
    /// </summary>
    public int? ShortTermContractDays { get; }

    /// <summary>Whether this league restricts when in the season a signing may happen.</summary>
    public bool HasInSeasonSigningWindow =>
        InSeasonSigningWindowOpensDay is not null && InSeasonSigningWindowClosesDay is not null;

    public bool HasShortTermContracts => ShortTermContractDays is not null;

    public bool HasTermLimit => MaximumContractSeasons is not null;

    public bool HasEscalationLimit =>
        MaximumAnnualEscalationPercent is not null || MaximumAnnualDeescalationPercent is not null;

    public bool HasStandardOverCapAllowance => StandardOverCapAllowance is not null;

    /// <summary>The longest term available to this team for this player.</summary>
    public int? MaximumSeasonsFor(bool isIncumbentTeam) =>
        isIncumbentTeam
            ? MaximumIncumbentContractSeasons ?? MaximumContractSeasons
            : MaximumContractSeasons;

    /// <summary>
    /// Builds the negotiation rules, checked against the thresholds the same file configures.
    /// Cross-section checks live here because they are genuinely cross-section: a ceiling expressed
    /// as a share of the soft cap is not expressible in a league with no soft cap, and a ruleset
    /// that states both is a contradiction — refused for the same reason a draftless league that
    /// sets a retained round is refused, rather than loaded and quietly reinterpreted.
    /// </summary>
    public static DomainOperationResult<NegotiationRules> Create(
        CapThresholds capThresholds,
        int? maximumContractSeasons,
        int? maximumIncumbentContractSeasons,
        int? maximumAnnualEscalationPercent,
        int? maximumAnnualDeescalationPercent,
        CompensationCeilingScale? compensationCeiling,
        CompensationFloorScale? compensationFloor,
        Money? standardOverCapAllowance,
        CapThresholdKind? standardOverCapAllowanceUnavailableAbove,
        bool allowanceMaySplitAcrossPlayers,
        MarketResolutionMode marketResolution,
        int? offerExpiryDays,
        int? inSeasonSigningWindowOpensDay = null,
        int? inSeasonSigningWindowClosesDay = null,
        int? shortTermContractDays = null)
    {
        ArgumentNullException.ThrowIfNull(capThresholds);

        var ceiling = compensationCeiling ?? CompensationCeilingScale.None;
        var floor = compensationFloor ?? CompensationFloorScale.None;
        var errors = new List<DomainError>();

        if (maximumContractSeasons is <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveTermCode,
                $"The maximum contract term is {maximumContractSeasons} seasons. A contract covers at least one season; leave the field out if this league does not limit term."));
        }

        if (maximumIncumbentContractSeasons is <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveTermCode,
                $"The maximum incumbent contract term is {maximumIncumbentContractSeasons} seasons, which cannot be fewer than one."));
        }

        // An incumbent limit on its own says "everyone may sign forever, except your own players",
        // which is the privilege running backwards. It is a file that has lost a field, not a rule.
        if (maximumIncumbentContractSeasons is not null && maximumContractSeasons is null)
        {
            errors.Add(new DomainError(
                IncumbentTermWithoutBaseCode,
                "This ruleset limits how long an incumbent team may sign its own player for, but sets no general term limit, so the incumbent limit is the only restriction in a league that otherwise allows any term. Set a general limit too, or leave both out."));
        }

        if (maximumIncumbentContractSeasons is { } incumbent && maximumContractSeasons is { } general && incumbent < general)
        {
            errors.Add(new DomainError(
                IncumbentTermBelowBaseCode,
                $"The incumbent term limit of {incumbent} seasons is shorter than the general limit of {general}. An incumbent allowance that offers less than everyone else is not an allowance."));
        }

        if (maximumAnnualEscalationPercent is < 0)
        {
            errors.Add(new DomainError(
                NegativeEscalationCode,
                $"The maximum annual escalation is {maximumAnnualEscalationPercent}%, which cannot be negative. Zero means no raises are permitted; leave the field out if this league does not limit raises."));
        }

        if (maximumAnnualDeescalationPercent is < 0)
        {
            errors.Add(new DomainError(
                NegativeEscalationCode,
                $"The maximum annual de-escalation is {maximumAnnualDeescalationPercent}%, which cannot be negative."));
        }

        if (ceiling.IsConfigured && capThresholds.SoftCap is null)
        {
            errors.Add(new DomainError(
                CeilingWithoutSoftCapCode,
                "This ruleset sets a compensation ceiling as a share of the soft cap, but configures no soft cap for it to be a share of. Configure a soft cap, or leave the ceiling table out — a league with no cap has no maximum salary."));
        }

        if (standardOverCapAllowance is { } allowance)
        {
            if (allowance.SmallestUnits <= 0)
            {
                errors.Add(new DomainError(
                    NonPositiveAllowanceCode,
                    $"The standard over-cap allowance is {allowance.SmallestUnits}. An allowance of nothing is not an allowance; leave the field out if this league has none."));
            }

            if (capThresholds.SoftCap is null)
            {
                errors.Add(new DomainError(
                    AllowanceWithoutSoftCapCode,
                    "This ruleset configures an over-the-cap allowance but no soft cap, so there is no cap for it to be over. In a league with no cap every signing is already unrestricted."));
            }
        }
        else
        {
            if (standardOverCapAllowanceUnavailableAbove is not null)
            {
                errors.Add(new DomainError(
                    AllowanceSettingWithoutAllowanceCode,
                    "This ruleset says which line makes the over-cap allowance unavailable, but configures no allowance for that line to withdraw."));
            }

            if (allowanceMaySplitAcrossPlayers)
            {
                errors.Add(new DomainError(
                    AllowanceSettingWithoutAllowanceCode,
                    "This ruleset says the over-cap allowance may be split across players, but configures no allowance to split."));
            }
        }

        if (standardOverCapAllowanceUnavailableAbove is { } limitKind &&
            capThresholds.Configured.All(entry => entry.Kind != limitKind))
        {
            errors.Add(new DomainError(
                AllowanceLimitNotConfiguredCode,
                $"The over-cap allowance is configured to be unavailable above the {limitKind}, which this league does not configure. A team cannot be above a line that does not exist."));
        }

        if (offerExpiryDays is <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveExpiryCode,
                $"Offers are configured to expire after {offerExpiryDays} days. Leave the field out if offers in this league do not expire."));
        }

        if (!Enum.IsDefined(marketResolution))
        {
            errors.Add(new DomainError(
                "ruleset.unknown_market_resolution",
                $"'{marketResolution}' is not a market resolution mode this build knows."));
        }

        // Half a window is not a rule anyone can enforce: an opening day with no closing day says
        // signing starts and never stops, which is the same as having no window while looking as
        // though the league stated one. Both or neither.
        if (inSeasonSigningWindowOpensDay is null != (inSeasonSigningWindowClosesDay is null))
        {
            errors.Add(new DomainError(
                HalfSigningWindowCode,
                "This ruleset states one end of the in-season signing window and not the other. State both days, or leave both out — a window with one edge is not a window."));
        }

        if (inSeasonSigningWindowOpensDay is < 0 || inSeasonSigningWindowClosesDay is < 0)
        {
            errors.Add(new DomainError(
                NegativeSigningWindowDayCode,
                "The in-season signing window is stated in season days, counted from the opening day, so neither end can be negative."));
        }

        if (inSeasonSigningWindowOpensDay is { } opensOn &&
            inSeasonSigningWindowClosesDay is { } closesOn &&
            closesOn <= opensOn)
        {
            errors.Add(new DomainError(
                SigningWindowInvertedCode,
                $"The in-season signing window opens on day {opensOn} and closes on day {closesOn}, so it is never open."));
        }

        if (shortTermContractDays is <= 0)
        {
            errors.Add(new DomainError(
                NonPositiveShortTermCode,
                $"A short-term contract is configured to run for {shortTermContractDays} days. Leave the field out if this league has no short-term contract."));
        }

        return errors.Count > 0
            ? DomainOperationResult<NegotiationRules>.Failure(errors.ToArray())
            : DomainOperationResult<NegotiationRules>.Success(new NegotiationRules(
                maximumContractSeasons,
                maximumIncumbentContractSeasons,
                maximumAnnualEscalationPercent,
                maximumAnnualDeescalationPercent,
                ceiling,
                floor,
                standardOverCapAllowance,
                standardOverCapAllowanceUnavailableAbove,
                allowanceMaySplitAcrossPlayers,
                marketResolution,
                offerExpiryDays,
                inSeasonSigningWindowOpensDay,
                inSeasonSigningWindowClosesDay,
                shortTermContractDays));
    }

    /// <summary>
    /// Parses a market resolution mode as it appears in a ruleset file, defaulting when the field is
    /// absent. Stored as a name rather than a number so the file stays readable if the enum grows.
    /// </summary>
    public static DomainOperationResult<MarketResolutionMode> ParseMarketResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DomainOperationResult<MarketResolutionMode>.Success(MarketResolutionMode.ResolutionPoint);
        }

        return Enum.TryParse<MarketResolutionMode>(value, out var parsed) && Enum.IsDefined(parsed)
            ? DomainOperationResult<MarketResolutionMode>.Success(parsed)
            : DomainOperationResult<MarketResolutionMode>.Failure(new DomainError(
                "ruleset.unknown_market_resolution",
                $"'{value}' is not a market resolution mode this build knows. Expected one of: {string.Join(", ", Enum.GetNames<MarketResolutionMode>())}."));
    }

    /// <summary>
    /// Parses the threshold a league withdraws the over-cap allowance above. The caller decides what
    /// an absent value means before calling: the result kernel does not carry a null success, and a
    /// method that pretends otherwise turns "the field was left out" into a crash at the boundary.
    /// </summary>
    public static DomainOperationResult<CapThresholdKind> ParseAllowanceLimit(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return Enum.TryParse<CapThresholdKind>(value, out var parsed) && Enum.IsDefined(parsed)
            ? DomainOperationResult<CapThresholdKind>.Success(parsed)
            : DomainOperationResult<CapThresholdKind>.Failure(new DomainError(
                "ruleset.unknown_allowance_limit_threshold",
                $"'{value}' is not a cap threshold this build knows. Expected one of: {string.Join(", ", Enum.GetNames<CapThresholdKind>())}."));
    }
}
