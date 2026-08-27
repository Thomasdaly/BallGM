using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

/// <summary>
/// A small league for signing tests, assembled through the real aggregate factories. Every figure is
/// round on purpose: these tests are about which rule fires, not about arithmetic nobody can check
/// by eye. One roster, one free agent, and whatever payroll the test asks for.
/// </summary>
internal sealed class SigningTestLeague
{
    internal static readonly Season CurrentSeason = new(2031);

    private static readonly DateTimeOffset LedgerStart = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly List<Contract> _contracts = [];

    private SigningTestLeague(
        RosterSizeLimits rosterLimits,
        CapThresholds thresholds,
        NegotiationRules negotiationRules,
        Team team,
        Player freeAgent)
    {
        RosterLimits = rosterLimits;
        CapThresholds = thresholds;
        NegotiationRules = negotiationRules;
        Team = team;
        FreeAgent = freeAgent;
        Ledger = new TransactionLedger(new SteppingTestClock(LedgerStart, TimeSpan.FromMinutes(1)));
    }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public NegotiationRules NegotiationRules { get; }

    public Team Team { get; }

    public Player FreeAgent { get; }

    public TransactionLedger Ledger { get; }

    public IReadOnlyCollection<Contract> Contracts => _contracts;

    /// <summary>The thresholds every test uses unless it is specifically about a league without them.</summary>
    public static CapThresholds StandardThresholds { get; } = CapThresholds.Create(
        payrollFloor: new Money(80_000_000),
        softCap: new Money(100_000_000),
        luxuryTax: new Money(120_000_000),
        firstApron: new Money(130_000_000),
        secondApron: new Money(140_000_000),
        hardCap: new Money(150_000_000)).Value;

    /// <summary>
    /// A conventional league: five-season contracts, an 8% raise limit, a ceiling and a floor that
    /// both rise with service, and one over-cap allowance withdrawn above the first apron.
    /// </summary>
    public static NegotiationRules StandardRules(CapThresholds? thresholds = null) => NegotiationRules.Create(
        thresholds ?? StandardThresholds,
        maximumContractSeasons: 5,
        maximumIncumbentContractSeasons: 6,
        maximumAnnualEscalationPercent: 8,
        maximumAnnualDeescalationPercent: 8,
        CompensationCeilingScale.Create([new ScaleBand(0, 25), new ScaleBand(7, 30), new ScaleBand(10, 35)]).Value,
        CompensationFloorScale.Create([new ScaleBand(0, 1_000_000), new ScaleBand(3, 2_000_000)]).Value,
        standardOverCapAllowance: new Money(12_000_000),
        standardOverCapAllowanceUnavailableAbove: CapThresholdKind.FirstApron,
        allowanceMaySplitAcrossPlayers: true,
        MarketResolutionMode.ResolutionPoint,
        offerExpiryDays: 3).Value;

    /// <summary>
    /// Builds the league. <paramref name="rosteredSalaries"/> is one contract per rostered player, so
    /// a test says what the payroll is by saying who is on it.
    /// </summary>
    public static SigningTestLeague Build(
        long[] rosteredSalaries,
        int minimumRoster = 3,
        int maximumRoster = 6,
        int freeAgentSeasonsOfService = 4,
        CapThresholds? thresholds = null,
        NegotiationRules? negotiationRules = null)
    {
        var limits = new RosterSizeLimits(minimumRoster, maximumRoster);
        var capThresholds = thresholds ?? StandardThresholds;
        var rules = negotiationRules ?? StandardRules(capThresholds);

        var franchise = Franchise.Create(new FranchiseId("FRANCHISE-A"), "Testfield Athletic").Value;
        var playerIds = new List<PlayerId>();
        var league = new SigningTestLeague(
            limits,
            capThresholds,
            rules,
            BuildTeam(franchise, limits, rosteredSalaries.Length, playerIds),
            BuildFreeAgent(freeAgentSeasonsOfService));

        for (var index = 0; index < rosteredSalaries.Length; index++)
        {
            league._contracts.Add(Contract.Create(
                new ContractId($"CONTRACT-{index}"),
                league.Team.Id,
                playerIds[index],
                [new ContractSeasonTerm(CurrentSeason, new Money(rosteredSalaries[index]), new Money(rosteredSalaries[index]))]).Value);
        }

        return league;
    }

    public SigningContext Context() => new(
        CurrentSeason,
        Team,
        FreeAgent,
        _contracts,
        Ledger,
        RosterLimits,
        CapThresholds,
        NegotiationRules);

    /// <summary>An offer of the given first-season salary, flat across the seasons unless a step is given.</summary>
    public Offer Offer(long firstSeasonCompensation, int seasons = 2, long stepPerSeason = 0) =>
        Domain.Negotiations.Offer.Create(
            new OfferId("OFFER-1"),
            Team.Id,
            FreeAgent.Id,
            Enumerable.Range(0, seasons).Select(index =>
            {
                var compensation = new Money(firstSeasonCompensation + (stepPerSeason * index));
                return new ContractSeasonTerm(new Season(CurrentSeason.Year + index), compensation, compensation);
            })).Value;

    private static Team BuildTeam(Franchise franchise, RosterSizeLimits limits, int rosterSize, List<PlayerId> playerIds)
    {
        for (var index = 0; index < rosterSize; index++)
        {
            playerIds.Add(new PlayerId($"PLAYER-{index}"));
        }

        return Team.Create(new TeamId("TEAM-A"), franchise.Id, "Testfield Trawlers", limits, playerIds).Value;
    }

    private static Player BuildFreeAgent(int seasonsOfService) => Player.Create(
        new PlayerId("PLAYER-FREE"),
        "Available Player",
        Position.SmallForward,
        new PlayerRating(80),
        new DateOnly(2004, 3, 1),
        seasonsOfService).Value;
}
