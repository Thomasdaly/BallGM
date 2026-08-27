namespace BallGM.Infrastructure.Rulesets;

/// <summary>
/// Serialization shape for a league ruleset file — deliberately carries no
/// validation of its own. <see cref="LeagueRulesetSerializer"/> is the trust boundary: it maps
/// this DTO onto the real domain/rules types, which is where every invariant actually lives.
/// <para>
/// The optional fields are nullable here rather than defaulted, and that is the whole point of
/// schema version 4: a JSON number that is absent deserializes to zero, and "this league has no
/// soft cap" is not "this league's soft cap is zero". Nullability is how the file says which rules
/// the league does not have, so the runtime types never have to guess.
/// </para>
/// </summary>
public sealed record LeagueRulesetEnvelope
{
    public LeagueRulesetEnvelope(
        int schemaVersion,
        string name,
        int regularSeasonGameCount,
        int minimumRosterPlayers,
        int maximumRosterPlayers,
        long? payrollFloor,
        long? softCap,
        long? luxuryTax,
        long? firstApron,
        long? secondApron,
        long? hardCap,
        int? draftRoundCount,
        bool draftLotteryEnabled,
        int? tradableFutureDraftHorizon,
        int? retainedRoundNumber,
        int? retainedRoundInterval,
        int? salaryMatchPercent,
        long? salaryMatchAllowance,
        string injuredPlayerTradeEligibility,
        bool secondApronBlocksSalaryIncrease,
        int? maximumContractSeasons,
        int? maximumIncumbentContractSeasons,
        int? maximumAnnualEscalationPercent,
        int? maximumAnnualDeescalationPercent,
        IReadOnlyList<CompensationCeilingTierEnvelope>? compensationCeilingTiers,
        IReadOnlyList<CompensationFloorBandEnvelope>? compensationFloorScale,
        long? standardOverCapAllowance,
        string? standardOverCapAllowanceUnavailableAbove,
        bool allowanceMaySplitAcrossPlayers,
        string? marketResolution,
        int? offerExpiryDays)
    {
        SchemaVersion = schemaVersion;
        Name = name;
        RegularSeasonGameCount = regularSeasonGameCount;
        MinimumRosterPlayers = minimumRosterPlayers;
        MaximumRosterPlayers = maximumRosterPlayers;
        PayrollFloor = payrollFloor;
        SoftCap = softCap;
        LuxuryTax = luxuryTax;
        FirstApron = firstApron;
        SecondApron = secondApron;
        HardCap = hardCap;
        DraftRoundCount = draftRoundCount;
        DraftLotteryEnabled = draftLotteryEnabled;
        TradableFutureDraftHorizon = tradableFutureDraftHorizon;
        RetainedRoundNumber = retainedRoundNumber;
        RetainedRoundInterval = retainedRoundInterval;
        SalaryMatchPercent = salaryMatchPercent;
        SalaryMatchAllowance = salaryMatchAllowance;
        InjuredPlayerTradeEligibility = injuredPlayerTradeEligibility;
        SecondApronBlocksSalaryIncrease = secondApronBlocksSalaryIncrease;
        MaximumContractSeasons = maximumContractSeasons;
        MaximumIncumbentContractSeasons = maximumIncumbentContractSeasons;
        MaximumAnnualEscalationPercent = maximumAnnualEscalationPercent;
        MaximumAnnualDeescalationPercent = maximumAnnualDeescalationPercent;
        CompensationCeilingTiers = compensationCeilingTiers;
        CompensationFloorScale = compensationFloorScale;
        StandardOverCapAllowance = standardOverCapAllowance;
        StandardOverCapAllowanceUnavailableAbove = standardOverCapAllowanceUnavailableAbove;
        AllowanceMaySplitAcrossPlayers = allowanceMaySplitAcrossPlayers;
        MarketResolution = marketResolution;
        OfferExpiryDays = offerExpiryDays;
    }

    public int SchemaVersion { get; }

    public string Name { get; }

    public int RegularSeasonGameCount { get; }

    public int MinimumRosterPlayers { get; }

    public int MaximumRosterPlayers { get; }

    /// <summary>The minimum total payroll a team must reach. Absent in a league that does not require one.</summary>
    public long? PayrollFloor { get; }

    /// <summary>Absent in a league with no cap system. Absent is not zero — see the type remarks.</summary>
    public long? SoftCap { get; }

    public long? LuxuryTax { get; }

    public long? FirstApron { get; }

    public long? SecondApron { get; }

    public long? HardCap { get; }

    /// <summary>Absent or zero in a league that holds no draft.</summary>
    public int? DraftRoundCount { get; }

    public bool DraftLotteryEnabled { get; }

    /// <summary>How many future drafts ahead of the current season picks may be traded in.</summary>
    public int? TradableFutureDraftHorizon { get; }

    /// <summary>The round a franchise must keep hold of. Named for what it does, not after any real-world rule.</summary>
    public int? RetainedRoundNumber { get; }

    /// <summary>How many consecutive future drafts the retention requirement is measured over.</summary>
    public int? RetainedRoundInterval { get; }

    /// <summary>
    /// How much salary a team over the cap may take back, as a percentage of what it sends. Absent
    /// in a league with no salary-matching rule; a value below 100 is still read as a typo.
    /// </summary>
    public int? SalaryMatchPercent { get; }

    /// <summary>A flat amount allowed on top of the percentage.</summary>
    public long? SalaryMatchAllowance { get; }

    /// <summary>Stored as a name rather than a number so the file stays readable if the enum grows.</summary>
    public string InjuredPlayerTradeEligibility { get; } = string.Empty;

    /// <summary>Whether a team finishing above the second apron may take on more salary than it sends.</summary>
    public bool SecondApronBlocksSalaryIncrease { get; }

    /// <summary>Longest contract anyone may sign. Absent in a league that does not limit term.</summary>
    public int? MaximumContractSeasons { get; }

    /// <summary>Longest contract a team may offer its own player, where that differs.</summary>
    public int? MaximumIncumbentContractSeasons { get; }

    /// <summary>Largest permitted season-over-season raise, as a percentage of the first season.</summary>
    public int? MaximumAnnualEscalationPercent { get; }

    /// <summary>Largest permitted season-over-season cut, as a percentage of the first season.</summary>
    public int? MaximumAnnualDeescalationPercent { get; }

    /// <summary>Maximum salary by seasons of service, as a percentage of the soft cap.</summary>
    public IReadOnlyList<CompensationCeilingTierEnvelope>? CompensationCeilingTiers { get; }

    /// <summary>Minimum salary by seasons of service, in smallest units.</summary>
    public IReadOnlyList<CompensationFloorBandEnvelope>? CompensationFloorScale { get; }

    /// <summary>The one fixed over-the-cap allowance this build models. Absent where a league has none.</summary>
    public long? StandardOverCapAllowance { get; }

    /// <summary>Named threshold above which the allowance is withdrawn, if any.</summary>
    public string? StandardOverCapAllowanceUnavailableAbove { get; }

    /// <summary>Whether the allowance may be spent across more than one player in a season.</summary>
    public bool AllowanceMaySplitAcrossPlayers { get; }

    /// <summary>
    /// When a free agent decides. Unlike the limits around it, absence here is a default rather than
    /// "no such rule": every league resolves offers somehow, and this is a mode, not a line.
    /// </summary>
    public string? MarketResolution { get; }

    /// <summary>How long an offer stands. Absent means offers in this league do not expire.</summary>
    public int? OfferExpiryDays { get; }
}

/// <summary>
/// One row of the compensation ceiling table: the lowest service figure it covers, and the share of
/// the soft cap that applies from there up. Maps onto <see cref="BallGM.Domain.Common.ScaleBand"/>.
/// </summary>
public sealed record CompensationCeilingTierEnvelope(long MinimumSeasonsOfService, long PercentOfSoftCap);

/// <summary>
/// One row of the compensation floor table: the lowest service figure it covers, and the minimum
/// salary in smallest units that applies from there up.
/// </summary>
public sealed record CompensationFloorBandEnvelope(long MinimumSeasonsOfService, long Amount);
