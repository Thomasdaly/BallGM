using BallGM.Domain.Cap;
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

namespace BallGM.Rules.Tests;

/// <summary>
/// A league with several teams bidding for one free agent, assembled through the real aggregate
/// factories. The multi-team counterpart to <see cref="SigningTestLeague"/>: a market needs rivals,
/// and one team bidding against nobody is the case Milestone 6a already covers.
/// </summary>
internal sealed class MarketTestLeague
{
    internal static readonly Season CurrentSeason = new(2031);

    private static readonly DateTimeOffset LedgerStart = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly List<Contract> _contracts = [];
    private readonly List<Player> _players = [];
    private readonly List<Team> _teams = [];

    private MarketTestLeague(
        RosterSizeLimits rosterLimits,
        CapThresholds thresholds,
        NegotiationRules negotiationRules,
        Player freeAgent)
    {
        RosterLimits = rosterLimits;
        CapThresholds = thresholds;
        NegotiationRules = negotiationRules;
        FreeAgent = freeAgent;
        Ledger = new TransactionLedger(new SteppingTestClock(LedgerStart, TimeSpan.FromMinutes(1)));
        _players.Add(freeAgent);
    }

    public RosterSizeLimits RosterLimits { get; }

    public CapThresholds CapThresholds { get; }

    public NegotiationRules NegotiationRules { get; }

    public Player FreeAgent { get; }

    public TransactionLedger Ledger { get; }

    public IReadOnlyList<Team> Teams => _teams;

    public IReadOnlyCollection<Contract> Contracts => _contracts;

    /// <summary>Team identifiers are fixed and ordinal-ascending, because the ordering key reads them.</summary>
    public Team Team(int index) => _teams[index];

    /// <summary>
    /// Builds a league. Each entry in <paramref name="teamSalaries"/> is one team, and the salaries
    /// inside it are one rostered contract each — a test states a payroll by stating who is on it.
    /// </summary>
    public static MarketTestLeague Build(
        long[][] teamSalaries,
        int minimumRoster = 3,
        int maximumRoster = 8,
        int freeAgentSeasonsOfService = 4,
        int freeAgentOverall = 80,
        CapThresholds? thresholds = null,
        NegotiationRules? negotiationRules = null)
    {
        var limits = new RosterSizeLimits(minimumRoster, maximumRoster);
        var capThresholds = thresholds ?? SigningTestLeague.StandardThresholds;
        var rules = negotiationRules ?? SigningTestLeague.StandardRules(capThresholds);

        var league = new MarketTestLeague(limits, capThresholds, rules, BuildFreeAgent(freeAgentSeasonsOfService, freeAgentOverall));

        for (var teamIndex = 0; teamIndex < teamSalaries.Length; teamIndex++)
        {
            var franchise = Franchise.Create(new FranchiseId($"FRANCHISE-{teamIndex}"), $"Testfield {teamIndex}").Value;
            var rosterIds = new List<PlayerId>();

            for (var playerIndex = 0; playerIndex < teamSalaries[teamIndex].Length; playerIndex++)
            {
                var playerId = new PlayerId($"PLAYER-{teamIndex}-{playerIndex}");
                rosterIds.Add(playerId);

                league._players.Add(Player.Create(
                    playerId,
                    $"Rostered {teamIndex}-{playerIndex}",
                    // Rosters are built at the free agent's own position deliberately: team fit is a
                    // question about depth, and a league where nobody plays their position cannot ask it.
                    Position.SmallForward,
                    new PlayerRating(60),
                    new DateOnly(2003, 1, 1),
                    5).Value);
            }

            // Ordinal ascending on TeamId is the market's stated ordering key, so the identifiers are
            // zero-padded: "TEAM-10" must not sort before "TEAM-2" in a test that is about ordering.
            var team = Domain.Teams.Team.Create(
                new TeamId($"TEAM-{teamIndex:D2}"),
                franchise.Id,
                $"Testfield Team {teamIndex}",
                limits,
                rosterIds).Value;

            league._teams.Add(team);

            for (var playerIndex = 0; playerIndex < teamSalaries[teamIndex].Length; playerIndex++)
            {
                var salary = teamSalaries[teamIndex][playerIndex];
                league._contracts.Add(Contract.Create(
                    new ContractId($"CONTRACT-{teamIndex}-{playerIndex}"),
                    team.Id,
                    rosterIds[playerIndex],
                    [new ContractSeasonTerm(CurrentSeason, new Money(salary), new Money(salary))]).Value);
            }
        }

        return league;
    }

    public MarketContext Context(int day = 0, IRandomSource? random = null) => new(
        CurrentSeason,
        new SeasonDay(day),
        FreeAgent,
        _teams,
        _players,
        _contracts,
        Ledger,
        RosterLimits,
        CapThresholds,
        NegotiationRules,
        random ?? new SeededRandomSource(1));

    /// <summary>An offer from one team, flat across the seasons.</summary>
    public Offer Offer(int teamIndex, long firstSeasonCompensation, int seasons = 3, string? offerId = null) =>
        Domain.Negotiations.Offer.Create(
            new OfferId(offerId ?? $"OFFER-{teamIndex:D2}"),
            _teams[teamIndex].Id,
            FreeAgent.Id,
            Enumerable.Range(0, seasons).Select(index =>
            {
                var compensation = new Money(firstSeasonCompensation);
                return new ContractSeasonTerm(new Season(CurrentSeason.Year + index), compensation, compensation);
            })).Value;

    /// <summary>
    /// Puts the free agent on a roster without giving them a contract. Not a state a league reaches
    /// by playing, but it is the state that separates the two signing checks: the validator asks
    /// whether anyone holds their contract, and the executor asks whether this roster already has
    /// them. A test of the rollback path needs the first to pass and the second to fail.
    /// </summary>
    public void PlaceFreeAgentOnRosterOf(int teamIndex) => _teams[teamIndex].AddPlayer(FreeAgent.Id);

    /// <summary>An open negotiation over the free agent, with nothing on the table yet.</summary>
    public Negotiation OpenNegotiation(int day = 0) =>
        Negotiation.Open(new NegotiationId("NEGOTIATION-1"), FreeAgent.Id, new SeasonDay(day)).Value;

    private static Player BuildFreeAgent(int seasonsOfService, int overall) => Player.Create(
        new PlayerId("PLAYER-FREE"),
        "Available Player",
        Position.SmallForward,
        new PlayerRating(overall),
        new DateOnly(2004, 3, 1),
        seasonsOfService).Value;
}
