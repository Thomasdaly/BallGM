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
        int? offerExpiryDays,
        int? preseasonDays = null,
        int? regularSeasonDays = null,
        int? offseasonDays = null,
        int? gamesVersusDivisionOpponent = null,
        int? gamesVersusConferenceOpponent = null,
        int? gamesVersusOtherConferenceOpponent = null,
        IReadOnlyList<string>? standingsTieBreaks = null,
        int? postseasonDays = null,
        int? postseasonQualifyingTeamsPerConference = null,
        IReadOnlyList<int>? postseasonSeriesLengths = null,
        string? postseasonHomeCourtSequence = null,
        int? playoffEligibilityCutoffDay = null,
        int? inSeasonSigningWindowOpensDay = null,
        int? inSeasonSigningWindowClosesDay = null,
        int? shortTermContractDays = null,
        int? draftClassSize = null,
        int? draftClassMinimumRating = null,
        int? draftClassMaximumRating = null,
        int? draftClassProspectAgeYears = null,
        int? scoutingBaseConfidence = null,
        int? scoutingMaxRangeWidth = null,
        IReadOnlyList<ScoutingInvestmentBandEnvelope>? scoutingInvestmentConfidence = null,
        IReadOnlyList<int>? draftLotteryWeights = null,
        int? developmentPeakAgeStart = null,
        int? developmentPeakAgeEnd = null,
        IReadOnlyList<AgeCurveBandEnvelope>? developmentGrowthCurve = null,
        IReadOnlyList<AgeCurveBandEnvelope>? developmentDeclineCurve = null,
        int? developmentVarianceRange = null,
        int? retirementMinimumVoluntaryAge = null,
        int? retirementMandatoryAge = null,
        IReadOnlyList<AgeCurveBandEnvelope>? retirementVoluntaryOddsByAge = null,
        IReadOnlyList<AwardEnvelope>? awards = null)
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
        PreseasonDays = preseasonDays;
        RegularSeasonDays = regularSeasonDays;
        OffseasonDays = offseasonDays;
        GamesVersusDivisionOpponent = gamesVersusDivisionOpponent;
        GamesVersusConferenceOpponent = gamesVersusConferenceOpponent;
        GamesVersusOtherConferenceOpponent = gamesVersusOtherConferenceOpponent;
        StandingsTieBreaks = standingsTieBreaks;
        PostseasonDays = postseasonDays;
        PostseasonQualifyingTeamsPerConference = postseasonQualifyingTeamsPerConference;
        PostseasonSeriesLengths = postseasonSeriesLengths;
        PostseasonHomeCourtSequence = postseasonHomeCourtSequence;
        PlayoffEligibilityCutoffDay = playoffEligibilityCutoffDay;
        InSeasonSigningWindowOpensDay = inSeasonSigningWindowOpensDay;
        InSeasonSigningWindowClosesDay = inSeasonSigningWindowClosesDay;
        ShortTermContractDays = shortTermContractDays;
        DraftClassSize = draftClassSize;
        DraftClassMinimumRating = draftClassMinimumRating;
        DraftClassMaximumRating = draftClassMaximumRating;
        DraftClassProspectAgeYears = draftClassProspectAgeYears;
        ScoutingBaseConfidence = scoutingBaseConfidence;
        ScoutingMaxRangeWidth = scoutingMaxRangeWidth;
        ScoutingInvestmentConfidence = scoutingInvestmentConfidence;
        DraftLotteryWeights = draftLotteryWeights;
        DevelopmentPeakAgeStart = developmentPeakAgeStart;
        DevelopmentPeakAgeEnd = developmentPeakAgeEnd;
        DevelopmentGrowthCurve = developmentGrowthCurve;
        DevelopmentDeclineCurve = developmentDeclineCurve;
        DevelopmentVarianceRange = developmentVarianceRange;
        RetirementMinimumVoluntaryAge = retirementMinimumVoluntaryAge;
        RetirementMandatoryAge = retirementMandatoryAge;
        RetirementVoluntaryOddsByAge = retirementVoluntaryOddsByAge;
        Awards = awards;
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

    /// <summary>Days before the regular season. Absent or zero means this league has no preseason.</summary>
    public int? PreseasonDays { get; }

    /// <summary>Days the regular season runs for. Absent means the shortest calendar that can be played.</summary>
    public int? RegularSeasonDays { get; }

    /// <summary>Days after the season ends. Absent or zero means the calendar stops when the season does.</summary>
    public int? OffseasonDays { get; }

    /// <summary>Games against each division rival. Stated with the other two weightings, or not at all.</summary>
    public int? GamesVersusDivisionOpponent { get; }

    /// <summary>Games against each same-conference opponent outside the division.</summary>
    public int? GamesVersusConferenceOpponent { get; }

    /// <summary>Games against each opponent in the other conference.</summary>
    public int? GamesVersusOtherConferenceOpponent { get; }

    /// <summary>
    /// The tie-break sequence, in the order the league applies it. Names rather than numbers so the
    /// file stays readable, and absent where the league states no tie-break at all — which is a
    /// league, not an omission, and is reported as a note on every table it affects.
    /// </summary>
    public IReadOnlyList<string>? StandingsTieBreaks { get; }

    /// <summary>Days the postseason runs for. The whole postseason section is absent in a league with none.</summary>
    public int? PostseasonDays { get; }

    /// <summary>Teams reaching the postseason from each conference. A power of two.</summary>
    public int? PostseasonQualifyingTeamsPerConference { get; }

    /// <summary>Games in each round's series, in round order.</summary>
    public IReadOnlyList<int>? PostseasonSeriesLengths { get; }

    /// <summary>How home advantage alternates inside a series, written as blocks — for example "2-2-1-1-1".</summary>
    public string? PostseasonHomeCourtSequence { get; }

    /// <summary>The last season day a signing keeps postseason eligibility. Absent where the league sets none.</summary>
    public int? PlayoffEligibilityCutoffDay { get; }

    /// <summary>The season day the in-season signing window opens. Stated with its closing day, or not at all.</summary>
    public int? InSeasonSigningWindowOpensDay { get; }

    /// <summary>The season day the in-season signing window closes.</summary>
    public int? InSeasonSigningWindowClosesDay { get; }

    /// <summary>How long a short-term contract runs, in days. Absent in a league that has no such contract.</summary>
    public int? ShortTermContractDays { get; }

    /// <summary>How many prospects a generated draft class contains. The whole draft-class section is absent where this league generates none of its own.</summary>
    public int? DraftClassSize { get; }

    /// <summary>The lowest true rating a generated prospect may carry.</summary>
    public int? DraftClassMinimumRating { get; }

    /// <summary>The highest true rating a generated prospect may carry.</summary>
    public int? DraftClassMaximumRating { get; }

    /// <summary>The age, in completed years, every generated prospect enters the draft at.</summary>
    public int? DraftClassProspectAgeYears { get; }

    /// <summary>Confidence (0-100) in a prospect nobody has scouted yet.</summary>
    public int? ScoutingBaseConfidence { get; }

    /// <summary>The width of the scouting range at zero confidence. Zero means this league models no scouting uncertainty.</summary>
    public int? ScoutingMaxRangeWidth { get; }

    /// <summary>Additional confidence bought by scouting investment, keyed by points invested.</summary>
    public IReadOnlyList<ScoutingInvestmentBandEnvelope>? ScoutingInvestmentConfidence { get; }

    /// <summary>The draft lottery's weighted-draw odds, worst team first. Absent means this league states no odds.</summary>
    public IReadOnlyList<int>? DraftLotteryWeights { get; }

    /// <summary>The first age a player is at their peak. The whole development section is absent where this league models no ageing.</summary>
    public int? DevelopmentPeakAgeStart { get; }

    /// <summary>The last age a player is at their peak.</summary>
    public int? DevelopmentPeakAgeEnd { get; }

    /// <summary>Rating points gained that season, keyed by age, below the peak range.</summary>
    public IReadOnlyList<AgeCurveBandEnvelope>? DevelopmentGrowthCurve { get; }

    /// <summary>Rating points lost that season, keyed by age, above the peak range.</summary>
    public IReadOnlyList<AgeCurveBandEnvelope>? DevelopmentDeclineCurve { get; }

    /// <summary>The seeded variance range applied on top of the curve each season.</summary>
    public int? DevelopmentVarianceRange { get; }

    /// <summary>The first age voluntary retirement may be drawn for. The whole retirement section is absent where this league models none.</summary>
    public int? RetirementMinimumVoluntaryAge { get; }

    /// <summary>The age retirement becomes certain rather than drawn. Absent or zero means no such age is set.</summary>
    public int? RetirementMandatoryAge { get; }

    /// <summary>Chance of voluntary retirement that season, out of 10,000, keyed by age.</summary>
    public IReadOnlyList<AgeCurveBandEnvelope>? RetirementVoluntaryOddsByAge { get; }

    /// <summary>The award set. Absent means this league hands out no awards at all.</summary>
    public IReadOnlyList<AwardEnvelope>? Awards { get; }
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

/// <summary>
/// One row of the scouting investment table: the lowest number of invested points it covers, and the
/// confidence bonus that applies from there up.
/// </summary>
public sealed record ScoutingInvestmentBandEnvelope(long MinimumInvestedPoints, long ConfidenceBonus);

/// <summary>
/// One row of an age-keyed table: the lowest age it covers, and the value that applies from there up.
/// Shared shape for the development growth/decline curves and the retirement odds-by-age table.
/// </summary>
public sealed record AgeCurveBandEnvelope(long MinimumAge, long Value);

/// <summary>One award: its code, its display name, and the stat basis name it is decided by.</summary>
public sealed record AwardEnvelope(string Code, string Name, string StatBasis);
