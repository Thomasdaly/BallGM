using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.Trades;

namespace BallGM.Rules.Tests;

/// <summary>
/// A small league assembled through the real aggregate factories, so a fixture that breaks an
/// invariant fails here rather than producing state the production code could never build. Every
/// figure is deliberately round, because these tests are about which rule fires, not about arithmetic
/// nobody can check by eye.
/// </summary>
internal sealed class TradeTestLeague
{
    internal static readonly Season CurrentSeason = new(2031);

    private static readonly DateTimeOffset LedgerStart = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, Team> _teams = [];
    private readonly Dictionary<string, Franchise> _franchises = [];
    private readonly List<Player> _players = [];
    private readonly List<Contract> _contracts = [];

    private TradeTestLeague(RosterSizeLimits rosterLimits, CapThresholds thresholds, TradeRules tradeRules, DraftRules draftRules)
    {
        RosterLimits = rosterLimits;
        CapThresholds = thresholds;
        TradeRules = tradeRules;
        DraftRules = draftRules;
        LeagueId = new LeagueId("LEAGUE-TEST");
        DraftAssets = new DraftAssetBook(LeagueId);
        Ledger = new TransactionLedger(new SteppingTestClock(LedgerStart, TimeSpan.FromMinutes(1)));
    }

    public LeagueId LeagueId { get; }

    public DraftAssetBook DraftAssets { get; }

    public TransactionLedger Ledger { get; }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public TradeRules TradeRules { get; private set; }

    public DraftRules DraftRules { get; }

    public IReadOnlyCollection<Contract> Contracts => _contracts;

    public static TradeTestLeague Build(
        int minimumRoster = 2,
        int maximumRoster = 5,
        int? salaryMatchPercent = 125,
        long? salaryMatchAllowance = 1_000_000,
        InjuredPlayerTradeEligibility injuredEligibility = InjuredPlayerTradeEligibility.AllowedWithWarning,
        bool secondApronBlocksSalaryIncrease = true,
        CapThresholds? capThresholds = null,
        bool holdsDraft = true)
    {
        var thresholds = capThresholds ?? CapThresholds.Create(
            softCap: new Money(100_000_000),
            luxuryTax: new Money(120_000_000),
            firstApron: new Money(130_000_000),
            secondApron: new Money(140_000_000),
            hardCap: new Money(150_000_000)).Value;

        var tradeRules = Configuration.TradeRules.Create(
            salaryMatchPercent,
            salaryMatchAllowance is null ? null : new Money(salaryMatchAllowance.Value),
            injuredEligibility,
            secondApronBlocksSalaryIncrease).Value;

        var draftRules = holdsDraft
            ? DraftRules.Create(
                roundCount: 2,
                lotteryEnabled: true,
                tradableFutureDraftHorizon: 4,
                retainedRoundNumber: 1,
                retainedRoundInterval: 2).Value
            : DraftRules.NoDraft;

        return new TradeTestLeague(new RosterSizeLimits(minimumRoster, maximumRoster), thresholds, tradeRules, draftRules);
    }

    /// <summary>Adds a team whose players each carry a contract at the salary given for them.</summary>
    public TradeTestLeague WithTeam(string key, params long[] playerSalaries)
    {
        var franchise = Franchise.Create(new FranchiseId($"FRANCHISE-{key}"), $"{key} Athletic").Value;
        var playerIds = new List<PlayerId>();

        for (var index = 0; index < playerSalaries.Length; index++)
        {
            var player = Player.Create(
                new PlayerId($"PLAYER-{key}-{index}"),
                $"{key} Player {index}",
                Position.PointGuard,
                new PlayerRating(70),
                new DateOnly(2000, 1, 1),
                seasonsOfService: 4).Value;

            _players.Add(player);
            playerIds.Add(player.Id);
        }

        var team = Team.Create(
            new TeamId($"TEAM-{key}"),
            franchise.Id,
            $"{key} Team",
            RosterLimits,
            playerIds).Value;

        _franchises[key] = franchise;
        _teams[key] = team;

        for (var index = 0; index < playerSalaries.Length; index++)
        {
            _contracts.Add(Contract.Create(
                new ContractId($"CONTRACT-{key}-{index}"),
                team.Id,
                playerIds[index],
                [new ContractSeasonTerm(CurrentSeason, new Money(playerSalaries[index]), new Money(playerSalaries[index]))]).Value);
        }

        // One first- and second-round pick per future draft, so pick movement and the retention
        // restriction both have something to work with.
        for (var year = CurrentSeason.Year; year <= CurrentSeason.Year + DraftRules.TradableFutureDraftHorizon; year++)
        {
            for (var round = 1; round <= DraftRules.RoundCount; round++)
            {
                var pick = DraftPick.Create(
                    new DraftPickId($"PICK-{key}-{year}-R{round}"),
                    LeagueId,
                    new Season(year),
                    round,
                    franchise.Id).Value;

                DraftAssets.Register(pick);
            }
        }

        return this;
    }

    /// <summary>Marks one of a team's players as injured, so eligibility rules have a case to fire on.</summary>
    public TradeTestLeague WithInjury(string teamKey, int playerIndex, string description = "Sprained ankle")
    {
        var playerId = new PlayerId($"PLAYER-{teamKey}-{playerIndex}");
        var existing = _players.Single(player => player.Id == playerId);
        var replacement = Player.Create(
            existing.Id,
            existing.FullName,
            existing.Position,
            existing.Rating,
            existing.BirthDate,
            existing.SeasonsOfService,
            new Injury(description)).Value;

        _players[_players.IndexOf(existing)] = replacement;
        return this;
    }

    public TradeTestLeague WithTradeRules(TradeRules tradeRules)
    {
        TradeRules = tradeRules;
        return this;
    }

    public Team TeamOf(string key) => _teams[key];

    public FranchiseId FranchiseOf(string key) => _franchises[key].Id;

    public PlayerId PlayerOf(string teamKey, int index) => new($"PLAYER-{teamKey}-{index}");

    public DraftPickId PickOf(string teamKey, int year, int round) => new($"PICK-{teamKey}-{year}-R{round}");

    public IReadOnlyCollection<Player> Players => _players;

    public TradeContext Context() => new(
        CurrentSeason,
        _teams.Values.ToList(),
        _players,
        _contracts,
        DraftAssets,
        Ledger,
        RosterLimits,
        CapThresholds,
        TradeRules,
        DraftRules);

    /// <summary>Builds a proposal against the league as it stands, so it starts out fresh rather than stale.</summary>
    public TradeProposal Proposal(params TradeAssetMovement[] movements)
    {
        var participants = movements
            .SelectMany(movement => new[] { movement.FromTeamId, movement.ToTeamId })
            .Distinct()
            .ToList();

        var result = TradeProposal.Create(
            new TradeId("TRADE-TEST"),
            CurrentSeason,
            participants,
            movements,
            LeagueStateToken.From(Ledger));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    public TradeAssetMovement SendPlayer(string fromKey, int playerIndex, string toKey) =>
        TradeAssetMovement.Player(PlayerOf(fromKey, playerIndex), TeamOf(fromKey).Id, TeamOf(toKey).Id);

    public TradeAssetMovement SendPick(string fromKey, int year, int round, string toKey) =>
        TradeAssetMovement.DraftPick(PickOf(fromKey, year, round), TeamOf(fromKey).Id, TeamOf(toKey).Id);

    /// <summary>A snapshot of everything a trade could touch, for asserting that nothing moved.</summary>
    public string StateFingerprint()
    {
        var rosters = _teams.Values
            .OrderBy(team => team.Id.Value, StringComparer.Ordinal)
            .Select(team => $"{team.Id.Value}:{string.Join(",", team.PlayerIds.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal))}");

        var contracts = _contracts
            .OrderBy(contract => contract.Id.Value, StringComparer.Ordinal)
            .Select(contract => $"{contract.Id.Value}@{contract.TeamId.Value}");

        var picks = DraftAssets.Picks
            .OrderBy(pick => pick.Id.Value, StringComparer.Ordinal)
            .Select(pick => $"{pick.Id.Value}@{DraftAssets.Ownership(pick.Id)!.CurrentOwnerFranchiseId.Value}");

        return string.Join("|", rosters.Concat(contracts).Concat(picks)) + $"|ledger:{Ledger.Count}";
    }
}

/// <summary>
/// A clock that advances by a fixed step on every read. Ledger entries then carry distinct, entirely
/// predictable timestamps — a test league that reads the wall clock is a test league whose ledger
/// assertions fail on a slow machine.
/// </summary>
internal sealed class SteppingTestClock(DateTimeOffset start, TimeSpan step) : IClock
{
    private long _reads;

    public DateTimeOffset UtcNow => start + (step * _reads++);
}
