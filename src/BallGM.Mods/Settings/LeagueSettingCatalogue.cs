namespace BallGM.Mods.Settings;

/// <summary>
/// Every league setting the game has, one entry per ruleset field: the granular half of league
/// setup. A preset (a ruleset pack) fills all of them in; a setup overrides any one of them by key;
/// a settings screen lists them by <see cref="LeagueSetting.Group"/> with the prose here beside each.
/// <para>
/// This is the answer to Football Manager's "advanced rules" editor, whose rules are a tree of
/// opaque database records that only make sense to someone who already knows them. Here each setting
/// is a named field with a sentence saying what it does, a range, and an explicit "off". Nothing in
/// the catalogue is a rule: the ruleset reader stays the authority on what is legal, and the
/// catalogue only catches the mistakes worth catching early (a word where a number goes, a typo in a
/// key) with a better message than the reader can give.
/// </para>
/// <para>
/// <c>LeagueSettingCatalogueTests</c> holds the catalogue to the ruleset format in both directions:
/// a ruleset field with no entry, or an entry naming no field, fails the build — so a new rule cannot
/// be added to the format without saying here what it means.
/// </para>
/// </summary>
public static class LeagueSettingCatalogue
{
    private const string LeagueGroup = "League";
    private const string RosterGroup = "Rosters";
    private const string CapGroup = "Salary cap and thresholds";
    private const string TaxGroup = "Luxury tax";
    private const string TradeGroup = "Trades";
    private const string ContractGroup = "Contracts";
    private const string AllowanceGroup = "Over-the-cap allowances";
    private const string MarketGroup = "Free agency and signings";
    private const string CalendarGroup = "Calendar";
    private const string ScheduleGroup = "Schedule";
    private const string StandingsGroup = "Standings";
    private const string PostseasonGroup = "Postseason";
    private const string DraftGroup = "Draft";
    private const string DraftClassGroup = "Draft class and scouting";
    private const string DevelopmentGroup = "Development and ageing";
    private const string RetirementGroup = "Retirement";
    private const string AwardsGroup = "Awards";
    private const string GameGroup = "Game rules";
    private const string LeadersGroup = "Stat leaders";

    /// <summary>The names the ruleset reader accepts for a cap line, in the order the lines rise.</summary>
    public static IReadOnlyList<string> ThresholdNames { get; } = ["PayrollFloor", "SoftCap", "LuxuryTax", "FirstApron", "SecondApron", "HardCap"];

    public static IReadOnlyList<string> TieBreakNames { get; } = ["HeadToHeadRecord", "DivisionRecord", "ConferenceRecord", "PointDifferential", "PointsScored"];

    public static IReadOnlyList<string> InjuredTradeNames { get; } = ["Allowed", "AllowedWithWarning", "Blocked"];

    public static IReadOnlyList<string> MarketResolutionNames { get; } = ["ResolutionPoint", "Immediate"];

    public static IReadOnlyList<string> AwardStatBasisNames { get; } = ["TotalPoints", "TotalRebounds", "TotalAssists", "TotalMinutes"];

    /// <summary>Ruleset fields that are not settings: the format's own version number.</summary>
    public static IReadOnlyList<string> NotSettings { get; } = ["schemaVersion"];

    public static IReadOnlyList<LeagueSetting> All { get; } = Build();

    private static readonly Dictionary<string, LeagueSetting> ByKey = All.ToDictionary(setting => setting.Key, StringComparer.Ordinal);

    public static LeagueSetting? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>The groups in the order a settings screen shows them.</summary>
    public static IReadOnlyList<string> Groups { get; } = All.Select(setting => setting.Group).Distinct().ToList();

    /// <summary>The known key closest to <paramref name="key"/>, for a "did you mean" — or null when nothing is close.</summary>
    public static string? Suggest(string key)
    {
        var best = All
            .Select(setting => (setting.Key, Distance: Distance(key.ToLowerInvariant(), setting.Key.ToLowerInvariant())))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .First();
        return best.Distance <= Math.Max(3, key.Length / 3) ? best.Key : null;
    }

    private static int Distance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[right.Length];
    }

    private static List<LeagueSetting> Build() =>
    [
        Text("name", LeagueGroup, "League rules name", "The name these rules are known by, shown on the league screen and in every report. Not a rule.", optional: false),
        Int("regularSeasonGameCount", ScheduleGroup, "Regular-season games", "Games every team plays in the regular season. With opponent weightings stated, it must equal what the weightings add up to for the league's alignment.", 1, 200, "games", optional: false),

        Int("minimumRosterPlayers", RosterGroup, "Roster minimum", "The fewest players a team is obliged to carry. An obligation, not an invariant: a team can fall short and is then required to sign, and unfilled spots are reserved on the cap sheet as roster-slot holds.", 1, 30, "players", optional: false),
        Int("maximumRosterPlayers", RosterGroup, "Roster maximum", "The most players a team may carry. A league pack with a bigger roster than this will not load under these settings.", 1, 30, "players", optional: false),

        Money("payrollFloor", CapGroup, "Payroll floor", "The least a team must spend on salary. Off: no spending floor."),
        Money("softCap", CapGroup, "Soft cap", "The spending line a team may only cross using exceptions (over-the-cap allowances, re-signing its own players). Maximum salaries are a share of it. Off: no cap system at all — not a cap of nought."),
        Money("luxuryTax", CapGroup, "Luxury tax line", "Payroll above this line pays the tax bill set out under Luxury tax. Off: no tax."),
        Money("firstApron", CapGroup, "First apron", "A stricter line above the tax: allowances can be withdrawn above it, and trade matching tightens (see Trades)."),
        Money("secondApron", CapGroup, "Second apron", "The strictest restriction line: teams above it lose salary aggregation in trades and the reduced over-cap allowance."),
        Money("hardCap", CapGroup, "Hard cap", "A ceiling no transaction may take a payroll above, under any exception. Off: no hard cap. A league with only a hard cap may set the soft cap equal to it."),

        Int("draftRoundCount", DraftGroup, "Draft rounds", "Rounds in the annual draft. Off (or 0): the league holds no draft, and nothing about picks applies.", 0, 10, "rounds"),
        Bool("draftLotteryEnabled", DraftGroup, "Draft lottery", "Whether the draft order's top is drawn by weighted lottery rather than set by reverse standings. On requires lottery odds."),
        Int("tradableFutureDraftHorizon", DraftGroup, "Tradable draft horizon", "How many future drafts ahead picks may be traded in.", 0, 20, "drafts"),
        Int("retainedRoundNumber", DraftGroup, "Retained round", "A round a team must keep at least one pick of across the interval below — the rule that stops a team trading away every future first-rounder.", 1, 10, "round"),
        Int("retainedRoundInterval", DraftGroup, "Retained-round interval", "How many consecutive future drafts the retained-round requirement is measured over. 2 means no two consecutive drafts without one.", 1, 20, "drafts"),

        Percent("salaryMatchPercent", TradeGroup, "Salary matching — percentage", "How much incoming salary an over-the-cap team may take back, as a percentage of what it sends. Off: trades need no salary matching. Overridden band by band where Salary matching bands are stated.", 100, 1000),
        Money("salaryMatchAllowance", TradeGroup, "Salary matching — flat allowance", "A flat amount allowed on top of the matching percentage."),
        Choice("injuredPlayerTradeEligibility", TradeGroup, "Trading injured players", "Whether an injured player may be traded: allowed, allowed with a warning on the trade screen, or blocked.", InjuredTradeNames, optional: false),
        Bool("secondApronBlocksSalaryIncrease", TradeGroup, "Second apron blocks taking on salary", "Whether a team finishing a trade above the second apron may take back more salary than it sends."),
        Int("tradeDeadlineDay", TradeGroup, "Trade deadline", "The season day after which no trade is allowed until the season is over; the AI front offices make their deadline deals the day before. Off: trades all season.", 0, 400, "day"),
        Section("buyouts", ContractGroup, "Buyouts", "Releasing a player for an agreed share of what he is owed: his consent, the releasing team's wait before re-signing him, and the apron bar on signing a released high earner. Off: no buyouts."),
        Section("incumbentRights", MarketGroup, "Incumbent rights and restricted free agency", "Re-signing a team's own player over the cap after enough seasons with it, qualifying offers that make young players restricted, and the right to match an offer sheet. Off: no incumbent rights."),
        Section("rookieScale", DraftGroup, "Rookie scale", "What each first-round draft slot is paid, for how many guaranteed seasons, and the team options after them. Off: draft picks sign as ordinary free agents."),

        Int("maximumContractSeasons", ContractGroup, "Longest contract", "The longest contract any team may offer a player. Off: no term limit.", 1, 15, "seasons"),
        Int("maximumIncumbentContractSeasons", ContractGroup, "Longest contract with own player", "The longest contract a team may offer a player already on its roster, where that differs.", 1, 15, "seasons"),
        Percent("maximumAnnualEscalationPercent", ContractGroup, "Largest annual raise", "The most a salary may rise season to season, as a percentage of the first season's salary. Off: raises are unlimited.", 0, 100),
        Percent("maximumAnnualDeescalationPercent", ContractGroup, "Largest annual cut", "The most a salary may fall season to season, as a percentage of the first season's salary.", 0, 100),
        Table("compensationCeilingTiers", ContractGroup, "Maximum salary by service", "The maximum salary, as a percentage of the soft cap, from each seasons-of-service figure up. Rows ascend by service. Off: no maximum salary.", ["minimumSeasonsOfService", "percentOfSoftCap"]),
        Table("compensationFloorScale", ContractGroup, "Minimum salary by service", "The minimum salary, in smallest units, from each seasons-of-service figure up. Rows ascend by service. Off: no minimum salary — and no roster-slot holds, since there is no honest figure to reserve.", ["minimumSeasonsOfService", "amount"]),

        Money("standardOverCapAllowance", AllowanceGroup, "Standard over-cap allowance", "How much a team over the soft cap may still pay one or more free agents in a season. Off: none."),
        Choice("standardOverCapAllowanceUnavailableAbove", AllowanceGroup, "Standard allowance withdrawn above", "The cap line above which the standard allowance may not be used.", ThresholdNames),
        Bool("allowanceMaySplitAcrossPlayers", AllowanceGroup, "Allowance may be split", "Whether an allowance may be shared between several signings in a season rather than spent on one."),

        Choice("marketResolution", MarketGroup, "When free agents decide", "ResolutionPoint: offers gather and the player picks at a set moment. Immediate: the first acceptable offer signs. A mode, not a limit — absent means ResolutionPoint.", MarketResolutionNames),
        Int("offerExpiryDays", MarketGroup, "Offer expiry", "How many days an offer stands before it lapses. Off: offers do not expire.", 1, 365, "days"),
        Int("inSeasonSigningWindowOpensDay", MarketGroup, "In-season signing window opens", "The season day in-season signings open. Stated with its closing day, or not at all.", 0, 400, "day"),
        Int("inSeasonSigningWindowClosesDay", MarketGroup, "In-season signing window closes", "The season day in-season signings close.", 0, 400, "day"),
        Int("shortTermContractDays", MarketGroup, "Short-term contract length", "How long a short-term contract runs. Off: the league has no such contract.", 1, 120, "days"),
        Int("playoffEligibilityCutoffDay", MarketGroup, "Postseason eligibility cutoff", "The last season day a newly signed player keeps postseason eligibility. A warning at signing, not a refusal.", 0, 400, "day"),

        Int("preseasonDays", CalendarGroup, "Preseason length", "Days before the regular season opens. Off or 0: no preseason.", 0, 120, "days"),
        Int("regularSeasonDays", CalendarGroup, "Regular-season length", "Days the regular season runs over. Must be enough to fit the games.", 1, 400, "days"),
        Int("offseasonDays", CalendarGroup, "Offseason length", "Days after the season before the next one opens.", 0, 365, "days"),
        Int("postseasonDays", CalendarGroup, "Postseason length", "Days the postseason runs over. Off removes the whole postseason section, and the regular-season table decides the title.", 1, 200, "days"),

        Int("gamesVersusDivisionOpponent", ScheduleGroup, "Games v each division opponent", "Games against each team in a team's own division. The three weightings are stated together or not at all; off, the schedule is a balanced round robin.", 1, 20, "games"),
        Int("gamesVersusConferenceOpponent", ScheduleGroup, "Games v each conference opponent", "Games against each same-conference team outside the division.", 1, 20, "games"),
        Int("gamesVersusOtherConferenceOpponent", ScheduleGroup, "Games v each other-conference opponent", "Games against each team in the other conference.", 1, 20, "games"),

        ChoiceList("standingsTieBreaks", StandingsGroup, "Tie-break order", "How level teams are separated, first rule first. Off: no tie-break — level teams stay level and every table says so.", TieBreakNames),

        Int("postseasonQualifyingTeamsPerConference", PostseasonGroup, "Postseason teams per conference", "How many teams from each conference reach the postseason. A power of two.", 1, 32, "teams"),
        IntList("postseasonSeriesLengths", PostseasonGroup, "Series lengths by round", "Games in each round's series, first round first; odd numbers. One entry per round the qualifier count implies (and one more for a final between conferences).", 1, 9),
        Text("postseasonHomeCourtSequence", PostseasonGroup, "Home-court pattern", "How home advantage alternates within a series, as blocks: \"2-2-1-1-1\" means the higher seed hosts games 1, 2, 5 and 7."),

        Int("draftClassSize", DraftClassGroup, "Generated draft-class size", "How many prospects the league generates each year. Off: the league generates no classes of its own.", 1, 500, "prospects"),
        Int("draftClassMinimumRating", DraftClassGroup, "Weakest prospect", "The lowest true rating a generated prospect may carry.", 1, 100),
        Int("draftClassMaximumRating", DraftClassGroup, "Strongest prospect", "The highest true rating a generated prospect may carry.", 1, 100),
        Int("draftClassProspectAgeYears", DraftClassGroup, "Prospect age", "The age every generated prospect enters the draft at.", 14, 30, "years"),
        Int("scoutingBaseConfidence", DraftClassGroup, "Unscouted confidence", "How much is known (0–100) about a prospect nobody has scouted.", 0, 100),
        Int("scoutingMaxRangeWidth", DraftClassGroup, "Scouting uncertainty", "How wide the rating range shown for a prospect is at zero confidence. 0: scouting is exact.", 0, 100, "points"),
        Table("scoutingInvestmentConfidence", DraftClassGroup, "Confidence bought by scouting", "Extra confidence from each level of scouting investment up.", ["minimumInvestedPoints", "confidenceBonus"]),
        IntList("draftLotteryWeights", DraftGroup, "Lottery odds", "The weight of each lottery team's ball, worst team first. Teams past the end of the list pick in reverse standings order.", 1, 10_000),

        Int("developmentPeakAgeStart", DevelopmentGroup, "Peak starts", "The first age a player is at his peak. Off removes the whole development section: nobody grows or declines.", 14, 50, "years"),
        Int("developmentPeakAgeEnd", DevelopmentGroup, "Peak ends", "The last age a player is at his peak.", 14, 50, "years"),
        Table("developmentGrowthCurve", DevelopmentGroup, "Growth before the peak", "Rating points gained in a season, from each age up, below the peak.", ["minimumAge", "value"]),
        Table("developmentDeclineCurve", DevelopmentGroup, "Decline after the peak", "Rating points lost in a season, from each age up, after the peak.", ["minimumAge", "value"]),
        Int("developmentVarianceRange", DevelopmentGroup, "Development variance", "The seeded spread around the curve each season.", 0, 50, "points"),

        Int("retirementMinimumVoluntaryAge", RetirementGroup, "Earliest voluntary retirement", "The first age a player may choose to retire. Off removes the whole retirement section.", 14, 60, "years"),
        Int("retirementMandatoryAge", RetirementGroup, "Mandatory retirement age", "The age retirement becomes certain. Off or 0: none.", 0, 70, "years"),
        Table("retirementVoluntaryOddsByAge", RetirementGroup, "Retirement odds by age", "Chance of retiring that season, out of 10,000, from each age up.", ["minimumAge", "value"]),

        Table("awards", AwardsGroup, "Season awards", "The awards handed out, each a code, a name, and the stat it is decided by (" + string.Join(", ", AwardStatBasisNames) + "). Off: no awards.", ["code", "name", "statBasis"]),

        Section("capMechanics", TaxGroup, "Tax and apron mechanics", "The tax bill, apron-tiered salary matching, and the reduced over-cap allowance. Off: none of it, and every behaviour falls back to what the cap lines alone describe."),
        Money("capMechanics.taxBracketSize", TaxGroup, "Tax bracket width", "Each tax bracket covers this much payroll above the tax line."),
        IntList("capMechanics.taxRatesPercent", TaxGroup, "Tax rates", "The tax per currency unit in each bracket, as a percentage (150 = 1.50 per 1), first bracket first.", 0, 10_000),
        IntList("capMechanics.repeaterTaxRatesPercent", TaxGroup, "Repeater tax rates", "The same schedule for a team that has paid tax in enough recent seasons to be a repeater.", 0, 10_000),
        Percent("capMechanics.taxRateIncrementPercent", TaxGroup, "Rate rise beyond the table", "How much each bracket past the end of the table adds to the rate.", 0, 10_000),
        Table("capMechanics.salaryMatchingBands", TradeGroup, "Salary matching bands", "Tiered matching: up to each outgoing amount, take back this percentage plus this allowance. The last band leaves outgoingUpTo out.", ["outgoingUpTo", "percent", "allowance"]),
        Percent("capMechanics.aboveFirstApronMatchPercent", TradeGroup, "Matching above the first apron", "The stricter matching percentage for a team that finishes a trade above the first apron (100: no more than it sends).", 0, 1000),
        Bool("capMechanics.secondApronBlocksAggregation", TradeGroup, "No aggregation above the second apron", "Whether a team above the second apron may combine several players' salaries to match one."),
        Money("capMechanics.reducedOverCapAllowance", AllowanceGroup, "Reduced over-cap allowance", "A smaller allowance for teams the standard one has been withdrawn from."),
        Choice("capMechanics.reducedOverCapAllowanceUnavailableAbove", AllowanceGroup, "Reduced allowance withdrawn above", "The cap line above which even the reduced allowance may not be used.", ThresholdNames),

        Section("gameRules", GameGroup, "Game rules", "Fouls and the bonus. Off: the sport's usual six, four and three."),
        Int("gameRules.personalFoulLimit", GameGroup, "Personal foul limit", "The personal foul that puts a player out of the game.", 1, 20, "fouls"),
        Int("gameRules.bonusTeamFoulsPerPeriod", GameGroup, "Team fouls before the bonus", "Team fouls a period before every further defensive foul sends the other side to the line.", 1, 20, "fouls"),
        Int("gameRules.bonusTeamFoulsPerOvertime", GameGroup, "Team fouls before the bonus in overtime", "The same, for an overtime period.", 1, 20, "fouls"),

        Section("statLeaders", LeadersGroup, "Stat-leader qualifiers", "Who qualifies for the leaderboards and percentiles. Off: 70% of games and the usual makes."),
        BasisPoints("statLeaders.minimumGamesShareBasisPoints", LeadersGroup, "Games played to qualify", "Share of the team's games a player must appear in, in hundredths of a percent (7000 = 70%).", 0, 10_000),
        Int("statLeaders.minimumMinutesPerGame", LeadersGroup, "Minutes a game to qualify (per-36 boards)", "Minutes a game a player must average to appear on per-36 leaderboards.", 0, 48, "minutes"),
        Int("statLeaders.minimumFieldGoalsMadePerTeamGame", LeadersGroup, "Field goals to qualify", "Field goals made per team game, in hundredths (366 = 3.66), to rank for field-goal percentage.", 0, 2000),
        Int("statLeaders.minimumThreesMadePerTeamGame", LeadersGroup, "Threes to qualify", "Threes made per team game, in hundredths, to rank for three-point percentage.", 0, 2000),
        Int("statLeaders.minimumFreeThrowsMadePerTeamGame", LeadersGroup, "Free throws to qualify", "Free throws made per team game, in hundredths, to rank for free-throw percentage.", 0, 2000),
    ];

    private static LeagueSetting Int(string key, string group, string label, string description, long minimum, long maximum, string? unit = null, bool optional = true) =>
        new(key, group, label, SettingValueKind.Integer, description) { Minimum = minimum, Maximum = maximum, Unit = unit, Optional = optional };

    private static LeagueSetting Money(string key, string group, string label, string description) =>
        new(key, group, label, SettingValueKind.Money, description) { Minimum = 0, Unit = "currency" };

    private static LeagueSetting Percent(string key, string group, string label, string description, long minimum, long maximum) =>
        new(key, group, label, SettingValueKind.Percent, description) { Minimum = minimum, Maximum = maximum, Unit = "%" };

    private static LeagueSetting BasisPoints(string key, string group, string label, string description, long minimum, long maximum) =>
        new(key, group, label, SettingValueKind.BasisPoints, description) { Minimum = minimum, Maximum = maximum, Unit = "bp" };

    private static LeagueSetting Bool(string key, string group, string label, string description) =>
        new(key, group, label, SettingValueKind.Boolean, description) { Optional = false };

    private static LeagueSetting Choice(string key, string group, string label, string description, IReadOnlyList<string> choices, bool optional = true) =>
        new(key, group, label, SettingValueKind.Choice, description) { Choices = choices, Optional = optional };

    private static LeagueSetting ChoiceList(string key, string group, string label, string description, IReadOnlyList<string> choices) =>
        new(key, group, label, SettingValueKind.ChoiceList, description) { Choices = choices };

    private static LeagueSetting IntList(string key, string group, string label, string description, long minimum, long maximum) =>
        new(key, group, label, SettingValueKind.IntegerList, description) { Minimum = minimum, Maximum = maximum };

    private static LeagueSetting Table(string key, string group, string label, string description, IReadOnlyList<string> columns) =>
        new(key, group, label, SettingValueKind.Table, description) { Columns = columns };

    private static LeagueSetting Text(string key, string group, string label, string description, bool optional = true) =>
        new(key, group, label, SettingValueKind.Text, description) { Optional = optional };

    private static LeagueSetting Section(string key, string group, string label, string description) =>
        new(key, group, label, SettingValueKind.Section, description);
}
