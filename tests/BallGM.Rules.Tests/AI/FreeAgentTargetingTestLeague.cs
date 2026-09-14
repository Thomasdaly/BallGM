using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;
using SteppingTestClock = BallGM.Rules.Tests.SteppingTestClock;

namespace BallGM.Rules.Tests.AI;

/// <summary>A small league for free-agent-targeting tests: rostered teams plus an unsigned free-agent pool.</summary>
internal sealed class FreeAgentTargetingTestLeague
{
    internal static readonly Season CurrentSeason = new(2031);

    private static readonly DateTimeOffset LedgerStart = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, Team> _teams = [];
    private readonly List<Player> _rosterPlayers = [];
    private readonly List<Player> _freeAgents = [];
    private readonly List<Contract> _contracts = [];

    private FreeAgentTargetingTestLeague(RosterSizeLimits rosterLimits, CapThresholds thresholds, NegotiationRules negotiationRules)
    {
        RosterLimits = rosterLimits;
        CapThresholds = thresholds;
        NegotiationRules = negotiationRules;
        Ledger = new TransactionLedger(new SteppingTestClock(LedgerStart, TimeSpan.FromMinutes(1)));
    }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public NegotiationRules NegotiationRules { get; }

    public TransactionLedger Ledger { get; }

    public static FreeAgentTargetingTestLeague Build(
        int minimumRoster = 2,
        int maximumRoster = 12,
        long? floor = 1_000_000,
        int? ceilingPercentOfSoftCap = 25,
        long softCap = 100_000_000,
        int? maximumContractSeasons = 5)
    {
        var thresholds = CapThresholds.Create(softCap: new Money(softCap)).Value;

        var floorScale = floor is null
            ? CompensationFloorScale.None
            : CompensationFloorScale.Create([new ScaleBand(0, floor.Value)]).Value;

        var ceilingScale = ceilingPercentOfSoftCap is null
            ? CompensationCeilingScale.None
            : CompensationCeilingScale.Create([new ScaleBand(0, ceilingPercentOfSoftCap.Value)]).Value;

        var negotiationRules = Configuration.NegotiationRules.Create(
            thresholds,
            maximumContractSeasons: maximumContractSeasons,
            maximumIncumbentContractSeasons: maximumContractSeasons,
            maximumAnnualEscalationPercent: null,
            maximumAnnualDeescalationPercent: null,
            ceilingScale,
            floorScale,
            standardOverCapAllowance: null,
            standardOverCapAllowanceUnavailableAbove: null,
            allowanceMaySplitAcrossPlayers: false,
            MarketResolutionMode.ResolutionPoint,
            offerExpiryDays: null).Value;

        return new FreeAgentTargetingTestLeague(new RosterSizeLimits(minimumRoster, maximumRoster), thresholds, negotiationRules);
    }

    public FreeAgentTargetingTestLeague WithTeam(string key, params (Position Position, int Overall, long Salary)[] roster)
    {
        var franchise = Franchise.Create(new FranchiseId($"FRANCHISE-{key}"), $"{key} Athletic").Value;
        var playerIds = new List<PlayerId>();

        for (var index = 0; index < roster.Length; index++)
        {
            var (position, overall, salary) = roster[index];
            var player = Player.Create(
                new PlayerId($"PLAYER-{key}-{index}"),
                $"{key} Player {index}",
                position,
                new PlayerRating(overall),
                new DateOnly(2000, 1, 1),
                seasonsOfService: 4).Value;

            _rosterPlayers.Add(player);
            playerIds.Add(player.Id);

            _contracts.Add(Contract.Create(
                new ContractId($"CONTRACT-{key}-{index}"),
                new TeamId($"TEAM-{key}"),
                player.Id,
                [new ContractSeasonTerm(CurrentSeason, new Money(salary), new Money(salary))]).Value);
        }

        _teams[key] = Team.Create(
            new TeamId($"TEAM-{key}"),
            franchise.Id,
            $"{key} Team",
            RosterLimits,
            playerIds).Value;

        return this;
    }

    public FreeAgentTargetingTestLeague WithFreeAgent(string key, Position position, int overall, int seasonsOfService = 4)
    {
        _freeAgents.Add(Player.Create(
            new PlayerId($"FA-{key}"),
            $"Free Agent {key}",
            position,
            new PlayerRating(overall),
            new DateOnly(2000, 1, 1),
            seasonsOfService).Value);

        return this;
    }

    public TeamId TeamId(string key) => new($"TEAM-{key}");

    public PlayerId FreeAgentId(string key) => new($"FA-{key}");

    public IReadOnlyList<Player> FreeAgents => _freeAgents;

    public MarketContext Context() => new(
        CurrentSeason,
        new SeasonDay(0),
        _freeAgents[0],
        _teams.Values.ToList(),
        _rosterPlayers.Concat(_freeAgents).ToList(),
        _contracts,
        Ledger,
        RosterLimits,
        CapThresholds,
        NegotiationRules,
        new SeededRandomSource(1));
}
