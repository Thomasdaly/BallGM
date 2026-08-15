namespace BallGM.Infrastructure.Rulesets;

/// <summary>
/// Flat, primitive-only serialization shape for a league ruleset file — deliberately carries no
/// validation of its own. <see cref="LeagueRulesetSerializer"/> is the trust boundary: it maps
/// this DTO onto the real domain/rules types, which is where every invariant actually lives.
/// </summary>
public sealed record LeagueRulesetEnvelope
{
    public LeagueRulesetEnvelope(
        int schemaVersion,
        string name,
        int regularSeasonGameCount,
        int minimumRosterPlayers,
        int maximumRosterPlayers,
        long softCap,
        long luxuryTax,
        long firstApron,
        long secondApron,
        long hardCap,
        int draftRoundCount,
        bool draftLotteryEnabled,
        int tradableFutureDraftHorizon,
        int retainedRoundNumber,
        int retainedRoundInterval,
        int salaryMatchPercent,
        long salaryMatchAllowance,
        string injuredPlayerTradeEligibility,
        bool secondApronBlocksSalaryIncrease)
    {
        SchemaVersion = schemaVersion;
        Name = name;
        RegularSeasonGameCount = regularSeasonGameCount;
        MinimumRosterPlayers = minimumRosterPlayers;
        MaximumRosterPlayers = maximumRosterPlayers;
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
    }

    public int SchemaVersion { get; }

    public string Name { get; }

    public int RegularSeasonGameCount { get; }

    public int MinimumRosterPlayers { get; }

    public int MaximumRosterPlayers { get; }

    public long SoftCap { get; }

    public long LuxuryTax { get; }

    public long FirstApron { get; }

    public long SecondApron { get; }

    public long HardCap { get; }

    public int DraftRoundCount { get; }

    public bool DraftLotteryEnabled { get; }

    /// <summary>How many future drafts ahead of the current season picks may be traded in.</summary>
    public int TradableFutureDraftHorizon { get; }

    /// <summary>The round a franchise must keep hold of. Named for what it does, not after any real-world rule.</summary>
    public int RetainedRoundNumber { get; }

    /// <summary>How many consecutive future drafts the retention requirement is measured over.</summary>
    public int RetainedRoundInterval { get; }

    /// <summary>How much salary a team over the cap may take back, as a percentage of what it sends.</summary>
    public int SalaryMatchPercent { get; }

    /// <summary>A flat amount allowed on top of the percentage.</summary>
    public long SalaryMatchAllowance { get; }

    /// <summary>Stored as a name rather than a number so the file stays readable if the enum grows.</summary>
    public string InjuredPlayerTradeEligibility { get; } = string.Empty;

    /// <summary>Whether a team finishing above the second apron may take on more salary than it sends.</summary>
    public bool SecondApronBlocksSalaryIncrease { get; }
}
