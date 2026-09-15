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
using Xunit;
using SteppingTestClock = BallGM.Rules.Tests.SteppingTestClock;

namespace BallGM.Rules.Tests.AI;

/// <summary>
/// A small league for trade-targeting tests, assembled through the real aggregate factories. Unlike
/// <c>TradeTestLeague</c>, every player carries a stated position and Overall, because targeting is
/// about which position a roster is thin at — a fixture that puts everyone at point guard would never
/// exercise it.
/// </summary>
internal sealed class TradeTargetingTestLeague
{
    internal static readonly Season CurrentSeason = new(2031);

    private static readonly DateTimeOffset LedgerStart = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, Team> _teams = [];
    private readonly List<Player> _players = [];
    private readonly List<Contract> _contracts = [];

    private TradeTargetingTestLeague(RosterSizeLimits rosterLimits, CapThresholds thresholds, TradeRules tradeRules)
    {
        RosterLimits = rosterLimits;
        CapThresholds = thresholds;
        TradeRules = tradeRules;
        DraftAssets = new DraftAssetBook(new LeagueId("LEAGUE-TARGET-TEST"));
        Ledger = new TransactionLedger(new SteppingTestClock(LedgerStart, TimeSpan.FromMinutes(1)));
    }

    public DraftAssetBook DraftAssets { get; }

    public TransactionLedger Ledger { get; }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public TradeRules TradeRules { get; }

    public static TradeTargetingTestLeague Build(int minimumRoster = 2, int maximumRoster = 12, CapThresholds? capThresholds = null)
    {
        var thresholds = capThresholds ?? CapThresholds.Uncapped;
        var tradeRules = Configuration.TradeRules.Create(
            salaryMatchPercent: null,
            salaryMatchAllowance: null,
            InjuredPlayerTradeEligibility.Allowed,
            secondApronBlocksSalaryIncrease: false).Value;

        return new TradeTargetingTestLeague(new RosterSizeLimits(minimumRoster, maximumRoster), thresholds, tradeRules);
    }

    /// <summary>Adds a team whose roster is exactly the stated (position, overall, salary) triples, one player each.</summary>
    public TradeTargetingTestLeague WithTeam(string key, params (Position Position, int Overall, long Salary)[] roster)
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

            _players.Add(player);
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

    /// <summary>
    /// Gives an existing player a second, already-terminated contract whose stated terms still reach
    /// into <see cref="CurrentSeason"/> — the shape a released player with guaranteed money keeps
    /// (<c>Contract.TermFor</c> answers "is this season in the stated terms," not "is this contract
    /// still live"), reproducing the two-contracts-at-once state a player who was released and later
    /// re-signed elsewhere legitimately reaches.
    /// </summary>
    public TradeTargetingTestLeague WithTerminatedContractStillCoveringTheCurrentSeason(string key, int index)
    {
        var playerId = PlayerId(key, index);
        var priorSeason = new Season(CurrentSeason.Year - 1);

        var contract = Contract.Create(
            new ContractId($"CONTRACT-{key}-{index}-RELEASED"),
            new TeamId($"TEAM-{key}"),
            playerId,
            [
                new ContractSeasonTerm(priorSeason, new Money(1), new Money(1)),
                new ContractSeasonTerm(CurrentSeason, new Money(1), new Money(1)),
            ]).Value;

        Assert.True(contract.Terminate(CurrentSeason).IsSuccess);
        _contracts.Add(contract);

        return this;
    }

    public TeamId TeamId(string key) => new($"TEAM-{key}");

    public PlayerId PlayerId(string key, int index) => new($"PLAYER-{key}-{index}");

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
        DraftRules.NoDraft);
}
