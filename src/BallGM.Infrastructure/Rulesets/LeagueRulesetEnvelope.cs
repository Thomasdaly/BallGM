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
        int retainedRoundInterval)
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
}
