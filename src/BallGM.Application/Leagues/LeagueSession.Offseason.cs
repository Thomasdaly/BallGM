using BallGM.Application.AI;
using BallGM.Application.Players;
using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Application.Leagues;

/// <summary>
/// The offseason an unattended league runs at every season boundary (<c>docs/franchise-realism.md</c>
/// → F3): teams keep the players they want and can afford before their contracts run out, then the
/// rest go to a free-agency market where teams bid and each player chooses. Every decision is the AI
/// front office's (<see cref="AI.IFrontOfficeAdvisor"/>) and every signing goes through the same
/// engines a human's does, so nothing here is a second rulebook.
/// </summary>
public sealed partial class LeagueSession
{
    /// <summary>The most rounds a market runs. Prices fall each round, so a market rarely needs them all.</summary>
    public const int MarketRounds = 8;

    /// <summary>
    /// Every team's re-signings, decided in the final season of the expiring contracts (so before the
    /// season is concluded) against the season's own table. Each is offered as an extension at the
    /// player's asking price, which he accepts from his own team; a team's payroll budget, by its
    /// direction, decides how many it can keep.
    /// </summary>
    /// <summary>
    /// The franchise the person playing runs, if one is set: the summer's AI acts for every other team
    /// and never for this one — no re-signing, qualifying offer, buyout, free-agent bid, dump, sale or
    /// trade is made on its behalf or with it. What the league itself requires still happens to it
    /// (options decided, draft picks signed, a roster below the minimum filled), because an empty roster
    /// is not a choice. Session state, like an in-flight negotiation: not saved with the league.
    /// </summary>
    public TeamId? ManagedTeamId { get; private set; }

    /// <summary>Sets (or clears, with null) the franchise the person playing runs.</summary>
    public DomainOperationResult SetManagedTeam(string? teamId)
    {
        if (teamId is null)
        {
            ManagedTeamId = null;
            return DomainOperationResult.Success;
        }

        if (_snapshot?.Teams.FirstOrDefault(team => team.Id.Value == teamId) is not { } team)
        {
            return DomainOperationResult.Failure(new DomainError(AIUnknownTeamCode, $"Team '{teamId}' is not a team in this league."));
        }

        ManagedTeamId = team.Id;
        return DomainOperationResult.Success;
    }

    private const string SummerOfferNoTeamCode = "summer_offer.no_managed_team";
    private const string SummerOfferUnknownPlayerCode = "summer_offer.unknown_player";
    private const string SummerOfferNotAvailableCode = "summer_offer.not_a_free_agent_this_summer";
    private const string SummerOfferTermsCode = "summer_offer.invalid_terms";

    /// <summary>The most seasons a standing summer offer may run; the signing rules still decide what is legal.</summary>
    public const int MaximumSummerOfferSeasons = 5;

    /// <summary>
    /// The managed franchise's standing offers for the coming summer, by player
    /// (<c>docs/franchise-realism.md</c> → §19). Session state, like an in-flight negotiation's, and not
    /// saved with the league; cleared when the market they were made for has run.
    /// </summary>
    private readonly Dictionary<PlayerId, SummerOffer> _summerOffers = [];

    /// <summary>The managed franchise's standing offers for the coming summer, best player first.</summary>
    public IReadOnlyList<SummerOfferLine> SummerOffers()
    {
        if (_snapshot is null || ManagedTeamId is not { } managed)
        {
            return [];
        }

        var teamNames = TeamNames(_snapshot);
        var playersById = _snapshot.Players.ToDictionary(player => player.Id);
        return _summerOffers.Values
            .Where(offer => offer.TeamId == managed && playersById.ContainsKey(offer.PlayerId))
            .Select(offer =>
            {
                var player = playersById[offer.PlayerId];
                var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.PlayerIds.Contains(player.Id));
                return new SummerOfferLine(
                    player.Id.Value,
                    player.FullName,
                    team is null ? "Free agent" : teamNames.GetValueOrDefault(team.Id, team.Name),
                    player.Rating.Overall,
                    offer.Salary.SmallestUnits,
                    offer.Seasons);
            })
            .OrderByDescending(line => line.Overall)
            .ThenBy(line => line.PlayerId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Makes (or replaces) a standing offer for the coming summer to a player who will be a free agent
    /// then — a flat salary a season for a number of seasons from the season the market opens. It joins
    /// every round of the market alongside the AI's bids, and the player weighs it against them by the
    /// same rule; it bids only in a round where it meets what he will take, there is a roster spot, and
    /// the signing is legal.
    /// </summary>
    public DomainOperationResult PlaceSummerOffer(string playerId, long salary, int seasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (_snapshot is null)
        {
            return DomainOperationResult.Failure(new DomainError(NotLoadedCode, "No league is loaded."));
        }

        if (ManagedTeamId is not { } managed)
        {
            return DomainOperationResult.Failure(new DomainError(SummerOfferNoTeamCode, "Choose the franchise you run before making summer offers."));
        }

        if (_snapshot.Players.FirstOrDefault(candidate => candidate.Id.Value == playerId) is not { } player)
        {
            return DomainOperationResult.Failure(new DomainError(SummerOfferUnknownPlayerCode, $"Player '{playerId}' is not in this league."));
        }

        if (!FreeAgentThisSummer(_snapshot, player.Id))
        {
            return DomainOperationResult.Failure(new DomainError(SummerOfferNotAvailableCode, $"{player.FullName} is under contract beyond this season, so he will not be a free agent this summer."));
        }

        if (salary <= 0 || seasons < 1 || seasons > MaximumSummerOfferSeasons)
        {
            return DomainOperationResult.Failure(new DomainError(SummerOfferTermsCode, $"A summer offer is a salary above zero for 1 to {MaximumSummerOfferSeasons} seasons."));
        }

        _summerOffers[player.Id] = new SummerOffer(managed, player.Id, new Money(salary), seasons);
        return DomainOperationResult.Success;
    }

    /// <summary>Withdraws a standing summer offer; withdrawing one that was never made is not an error.</summary>
    public DomainOperationResult WithdrawSummerOffer(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        _summerOffers.Remove(new PlayerId(playerId));
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Whether a player reaches free agency at the end of this season: no live contract running past
    /// it (an option on next season may still keep him, which the market will find out).
    /// </summary>
    private static bool FreeAgentThisSummer(LeagueSnapshot snapshot, PlayerId playerId) =>
        !snapshot.Contracts.Any(contract => contract.PlayerId == playerId && !contract.IsTerminated && contract.LastSeason.Year > snapshot.CurrentSeason.Year);

    /// <summary>What became of each standing offer once the market has run, then the offers are spent.</summary>
    private IReadOnlyList<string> SettleSummerOffers()
    {
        if (_snapshot is null || _summerOffers.Count == 0)
        {
            _summerOffers.Clear();
            return [];
        }

        var teamNames = TeamNames(_snapshot);
        var outcomes = new List<string>();
        foreach (var offer in _summerOffers.Values.OrderBy(offer => offer.PlayerId.Value, StringComparer.Ordinal))
        {
            if (_snapshot.Players.FirstOrDefault(player => player.Id == offer.PlayerId) is not { } player)
            {
                continue;
            }

            var signed = _snapshot.Contracts.FirstOrDefault(contract =>
                contract.PlayerId == offer.PlayerId && !contract.IsTerminated && contract.TermFor(_snapshot.CurrentSeason) is not null);
            if (signed is not null && signed.TeamId == offer.TeamId)
            {
                outcomes.Add($"{player.FullName} accepted your offer of {ExplanationText.Money(signed.TermFor(_snapshot.CurrentSeason)!.Compensation)} a season.");
            }
            else if (signed is not null)
            {
                outcomes.Add($"{player.FullName} chose {teamNames.GetValueOrDefault(signed.TeamId, signed.TeamId.Value)} ({ExplanationText.Money(signed.TermFor(_snapshot.CurrentSeason)!.Compensation)} a season) over your offer.");
            }
            else
            {
                var reason = _frontOfficeAdvisor.ExplainStandingOffer(_snapshot, offer, MarketRounds - 1);
                outcomes.Add(reason.IsSuccess
                    ? $"{player.FullName} is still unsigned. {reason.Value.Explanation}"
                    : $"{player.FullName} is still unsigned.");
            }
        }

        _summerOffers.Clear();
        return outcomes;
    }

    /// <summary>The managed franchise as a set the planners exclude, empty when none is set.</summary>
    private HashSet<TeamId> ManagedTeams() => ManagedTeamId is { } managed ? [managed] : [];

    private (int ReSigned, IReadOnlyList<RuleFinding> Notes) RunReSigningWindow()
    {
        if (_snapshot is null || _seasonRun is null)
        {
            return (0, []);
        }

        var records = _seasonEngine.Standings(_seasonRun, _snapshot).Rows.ToDictionary(row => row.TeamId, row => row.Overall);
        var plans = _frontOfficeAdvisor.PlanReSignings(_snapshot, records);
        if (plans.IsFailure)
        {
            return (0, plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)).ToList());
        }

        var reSigned = 0;
        var notes = new List<RuleFinding>();
        foreach (var plan in plans.Value.Where(plan => plan.TeamId != ManagedTeamId))
        {
            var outcome = OfferExtension(plan.PlayerId.Value, plan.Seasons, plan.FirstSeasonSalary, annualRaisePercent: 0);
            if (outcome.IsSuccess && outcome.Value.Accepted)
            {
                reSigned++;
                notes.Add(plan.Rationale);
            }
        }

        return (reSigned, notes);
    }

    /// <summary>
    /// The free-agency market: round by round, every team bids for the free agents who would improve
    /// it, inside its budget, and each player takes the offer he prefers; prices fall each round until
    /// the market clears or <see cref="MarketRounds"/> have run. Every accepted offer is executed
    /// through the signing engine, which re-validates it against the league as it then stands.
    /// </summary>
    /// <summary>
    /// The qualifying offers tendered after the re-signing window: each restricted player (his own team
    /// did not re-sign him but wants him) and the one-season salary his team will sign him to if nobody
    /// else bids.
    /// </summary>
    private (IReadOnlyList<AI.QualifyingOfferPlan> Tendered, IReadOnlyList<RuleFinding> Notes) RunQualifyingOffers()
    {
        if (_snapshot is null)
        {
            return ([], []);
        }

        var plans = _frontOfficeAdvisor.PlanQualifyingOffers(_snapshot);
        if (plans.IsFailure)
        {
            return ([], plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)).ToList());
        }

        var tendered = plans.Value.Where(plan => plan.TeamId != ManagedTeamId).ToList();
        return (tendered, tendered.Select(plan => plan.Rationale).ToList());
    }

    /// <summary>
    /// The summer's veteran sales, after free agency: losing teams send good veterans to contenders for
    /// picks and matching salary. Executed through the trade engine like any trade.
    /// </summary>
    /// <param name="alreadyTraded">Teams already in a deal this window (a salary dump): a team that just took a contract on does not turn round and sell it.</param>
    private (int Executed, IReadOnlyList<RuleFinding> Notes) RunVeteranSales(IReadOnlyDictionary<TeamId, Domain.Seasons.TeamRecord>? records = null, AI.LiveInjuries? injuries = null, IEnumerable<TeamId>? alreadyTraded = null)
    {
        var soldTeams = ManagedTeams();
        soldTeams.UnionWith(alreadyTraded ?? []);
        var starSold = false;
        return ExecuteTradesOneAtATime(
            () =>
            {
                var plans = _frontOfficeAdvisor.PlanVeteranSales(_snapshot!, soldTeams, starSold, records, injuries);
                // The first plan is the one executed next; once a star's sale is planned first, the cap
                // is spent whether or not it goes through, so no second star is ever planned behind it.
                starSold |= plans.IsSuccess && plans.Value.Count > 0 && plans.Value[0].IsStar;
                return Trades(plans, plan => (plan.Proposal, plan.SellerTeamId, plan.BuyerTeamId, plan.Rationale));
            },
            soldTeams,
            "ai_trade.veteran_sale_failed",
            "A planned veteran sale did not go through");
    }

    private static DomainOperationResult<List<(Domain.Trades.TradeProposal Proposal, TeamId First, TeamId Second, RuleFinding Rationale)>> Trades<TPlan>(
        DomainOperationResult<IReadOnlyList<TPlan>> plans,
        Func<TPlan, (Domain.Trades.TradeProposal, TeamId, TeamId, RuleFinding)> select) =>
        plans.IsFailure
            ? DomainOperationResult<List<(Domain.Trades.TradeProposal Proposal, TeamId First, TeamId Second, RuleFinding Rationale)>>.Failure(plans.Errors.ToArray())
            : DomainOperationResult<List<(Domain.Trades.TradeProposal Proposal, TeamId First, TeamId Second, RuleFinding Rationale)>>.Success(plans.Value.Select(select).ToList());

    /// <summary>
    /// Executes the first planned trade, then plans again — every plan carries the league state it was
    /// drawn against, so a second plan from the same batch would be refused as stale once the first has
    /// changed the ledger. Stops when nothing is planned, or the first plan fails (noted).
    /// </summary>
    private (int Executed, IReadOnlyList<RuleFinding> Notes) ExecuteTradesOneAtATime(
        Func<DomainOperationResult<List<(Domain.Trades.TradeProposal Proposal, TeamId First, TeamId Second, RuleFinding Rationale)>>> plan,
        HashSet<TeamId> involved,
        string failureCode,
        string failureText)
    {
        if (_snapshot is null)
        {
            return (0, []);
        }

        var executed = 0;
        var notes = new List<RuleFinding>();
        while (true)
        {
            var plans = plan();
            if (plans.IsFailure)
            {
                notes.AddRange(plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)));
                break;
            }

            if (plans.Value.Count == 0)
            {
                break;
            }

            var (proposal, first, second, rationale) = plans.Value[0];
            var result = _tradeEngine.Execute(proposal, _snapshot, _seasonRun?.CurrentDay);
            involved.Add(first);
            involved.Add(second);
            if (result.IsFailure)
            {
                notes.Add(new RuleFinding(failureCode, $"{failureText}: {string.Join("; ", result.Errors.Select(error => error.Message))}", first));
                continue;
            }

            executed++;
            notes.Add(rationale);
        }

        return (executed, notes);
    }

    /// <summary>
    /// The summer's salary dumps, before free agency: a taxpayer clears an overpaid contract into a
    /// rebuilding team's room, paying in picks. Each is executed through the trade engine (which
    /// re-validates it); one that no longer passes is skipped and noted. Run twice a summer — before the
    /// market, while rebuilding teams still have room, and after it, once rosters are filled enough
    /// that sending a player out does not leave a team short — with <paramref name="dumpedTeams"/>
    /// keeping it to one dump a team.
    /// </summary>
    private (int Executed, IReadOnlyList<RuleFinding> Notes) RunSalaryDumps(HashSet<TeamId> dumpedTeams, bool inSeason = false, AI.LiveInjuries? injuries = null) =>
        ExecuteTradesOneAtATime(
            () => Trades(_frontOfficeAdvisor.PlanSalaryDumps(_snapshot!, dumpedTeams, inSeason, injuries), plan => (plan.Proposal, plan.FromTeamId, plan.ToTeamId, plan.Rationale)),
            dumpedTeams,
            "ai_trade.salary_dump_failed",
            "A planned salary dump did not go through");

    /// <summary>
    /// Restricted players nobody signed in the market sign their qualifying offers with their own teams —
    /// one season, through the incumbent-rights route — where the team still has a roster spot.
    /// </summary>
    private (int Signed, IReadOnlyList<RuleFinding> Notes) SignQualifyingOffers(IReadOnlyList<AI.QualifyingOfferPlan> tendered)
    {
        if (_snapshot is null || tendered.Count == 0)
        {
            return (0, []);
        }

        var pool = FreeAgentPool(_snapshot).Select(player => player.Id).ToHashSet();
        var signed = 0;
        var notes = new List<RuleFinding>();
        foreach (var plan in tendered.Where(plan => pool.Contains(plan.PlayerId)))
        {
            var offer = Offer.Create(
                new OfferId(SortableId.NewId()),
                plan.TeamId,
                plan.PlayerId,
                [new ContractSeasonTerm(_snapshot.CurrentSeason, new Money(plan.Salary), new Money(plan.Salary))]);
            if (offer.IsFailure)
            {
                continue;
            }

            var execution = _signingEngine.Execute(offer.Value, _snapshot, plan.TeamId, plan.PlayerId);
            if (execution.IsSuccess)
            {
                _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, execution.Value.Contract] };
                signed++;
                notes.Add(new RuleFinding("ai_offseason.qualifying_offer_signed", $"Unclaimed in free agency, he signs his qualifying offer of {Domain.Common.ExplanationText.Money(plan.Salary)} for one season.", plan.TeamId));
            }
        }

        return (signed, notes);
    }

    private (int Signed, IReadOnlyList<RuleFinding> Notes) RunFreeAgencyMarket(IReadOnlyDictionary<PlayerId, TeamId>? restricted = null)
    {
        if (_snapshot is null)
        {
            return (0, []);
        }

        var signed = 0;
        var notes = new List<RuleFinding>();
        for (var round = 0; round < MarketRounds; round++)
        {
            var pool = FreeAgentPool(_snapshot);
            if (pool.Count == 0)
            {
                break;
            }

            var standing = _summerOffers.Values.Where(offer => offer.TeamId == ManagedTeamId).ToList();
            var plans = _frontOfficeAdvisor.PlanMarketRound(_snapshot, pool, round, restricted, ManagedTeams(), standing);
            if (plans.IsFailure)
            {
                notes.AddRange(plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)));
                break;
            }

            foreach (var plan in plans.Value)
            {
                var execution = _signingEngine.Execute(plan.Offer, _snapshot, plan.Offer.TeamId, plan.Offer.PlayerId);
                if (execution.IsFailure)
                {
                    continue;
                }

                _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, execution.Value.Contract] };
                signed++;
                notes.Add(plan.Choice);
            }
        }

        return (signed, notes);
    }

    /// <summary>
    /// Every option on next season, decided before the re-signing window so a declined option's player is
    /// treated as expiring there (his own team may still re-sign him at his price) and a player who walks
    /// away from his option reaches the market.
    /// </summary>
    private (int Decided, IReadOnlyList<RuleFinding> Notes) RunOptionDecisions()
    {
        if (_snapshot is null)
        {
            return (0, []);
        }

        var plans = _frontOfficeAdvisor.PlanOptionDecisions(_snapshot);
        if (plans.IsFailure)
        {
            return (0, plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)).ToList());
        }

        var decided = 0;
        var notes = new List<RuleFinding>();
        foreach (var plan in plans.Value)
        {
            if (_signingEngine.DecideOption(_snapshot, plan.ContractId, plan.Season, plan.Exercise).IsSuccess)
            {
                decided++;
                notes.Add(plan.Rationale);
            }
        }

        return (decided, notes);
    }

    /// <summary>
    /// The summer's buyouts, before the market opens so a bought-out veteran can sign elsewhere: each
    /// agreed by the front office's own rule and carried out by the signing engine, which refuses one
    /// the league's buyout rules do not allow.
    /// </summary>
    private (IReadOnlyList<BuyoutLine> BoughtOut, IReadOnlyList<RuleFinding> Notes) RunBuyouts()
    {
        if (_snapshot is null)
        {
            return ([], []);
        }

        var planned = _frontOfficeAdvisor.PlanBuyouts(_snapshot);
        if (planned.IsFailure)
        {
            return ([], planned.Errors.Select(error => new RuleFinding(error.Code, error.Message)).ToList());
        }

        var plans = DomainOperationResult<List<AI.BuyoutPlan>>.Success(planned.Value.Where(plan => plan.TeamId != ManagedTeamId).ToList());

        var boughtOut = new List<BuyoutLine>();
        var notes = new List<RuleFinding>();
        foreach (var plan in plans.Value)
        {
            if (_signingEngine.ExecuteBuyout(_snapshot, plan.TeamId, plan.PlayerId, plan.ContractId, plan.RetainedPercent).IsSuccess)
            {
                boughtOut.Add(new BuyoutLine(plan.PlayerId.Value, plan.TeamId.Value, plan.RetainedPercent, plan.Rationale.Explanation));
                notes.Add(plan.Rationale);
            }
        }

        return (boughtOut, notes);
    }

    /// <summary>Every unsigned, unretired player, worked out once rather than player by player against every contract.</summary>
    private static List<Player> FreeAgentPool(LeagueSnapshot snapshot)
    {
        var rostered = snapshot.Teams.SelectMany(team => team.PlayerIds).ToHashSet();
        var contracted = snapshot.Contracts
            .Where(contract => !contract.IsTerminated && contract.TermFor(snapshot.CurrentSeason) is not null)
            .Select(contract => contract.PlayerId)
            .ToHashSet();

        return snapshot.Players
            .Where(player => !player.IsRetired && !rostered.Contains(player.Id) && !contracted.Contains(player.Id))
            .OrderBy(player => player.Id.Value, StringComparer.Ordinal)
            .ToList();
    }
}
