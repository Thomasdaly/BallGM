using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;
using BallGM.Rules.Signings;

namespace BallGM.Rules.AI;

/// <summary>An AI team's decision to keep one of its own expiring players, with the terms and the reason.</summary>
public sealed record ReSigningDecision(TeamId TeamId, PlayerId PlayerId, int Seasons, Money FirstSeasonSalary, RuleFinding Rationale);

/// <summary>One team's offer to one free agent in a market round: what it is worth to the team, and why.</summary>
public sealed record MarketOffer(Offer Offer, int Value, RuleFinding Rationale);

/// <summary>
/// How an AI front office runs its summer (<c>docs/franchise-realism.md</c> → F3): which of its own
/// expiring players it keeps, and which free agents it bids for and at what price. Pure and
/// deterministic — no randomness, every tie broken by identifier — and every legality question is
/// answered by the real <see cref="SigningValidator"/>, never a second opinion.
/// <para>
/// <b>Direction decides the budget.</b> A contender spends to the first apron (to the second to keep
/// one of its three best players); a retooling team to the tax line; a rebuilding team to the cap, and
/// it does not commit to players past thirty. This is what keeps a league's stars where they are: a
/// good team keeps its core, and a player only moves when his team cannot or will not pay him — not
/// because every summer is a lottery.
/// </para>
/// <para>
/// <b>Prices fall as the summer goes on.</b> Round 0 offers a free agent his asking price; each later
/// round offers <see cref="PriceDropPercentPerRound"/> per cent less, never below the league minimum —
/// the market clearing the way a real one does, stars first at full price and the rest finding their level.
/// </para>
/// </summary>
public static class OffseasonModel
{
    private const string KeepCode = "ai_offseason.keep";
    private const string BidCode = "ai_offseason.bid";
    private const string ChoiceCode = "ai_offseason.player_choice";

    /// <summary>Win percentage, as a whole number, at or above which a team reads as a contender (the classifier's own line).</summary>
    public const int ContendingWinPercent = 55;

    /// <summary>
    /// How far below its eighth man a team under the payroll floor will still bid for a player: it is
    /// spending money it owes anyway, so a near-rotation player is worth a one-season overpay.
    /// </summary>
    public const int FloorRotationReach = 4;

    /// <summary>
    /// The least a team under the floor overpays a free agent, as a share of his price; it pays more when
    /// the gap spread over its open roster spots is larger.
    /// </summary>
    public const int FloorOverpayPercent = 200;

    /// <summary>
    /// Roster spots a team keeps open below the maximum when it signs depth: a full complement less one,
    /// the flexibility real teams keep for a trade or an in-season signing.
    /// </summary>
    public const int DepthSpotsKeptOpen = 1;

    /// <summary>
    /// How far above a free agent's asking price a contending team bids, in per cent (a retooling team,
    /// <see cref="RetoolingPremiumPercent"/>): a team trying to win now pays to win the player, which is
    /// how good teams end up paying the tax and how talent concentrates on contenders.
    /// </summary>
    public const int ContenderPremiumPercent = 20;

    /// <inheritdoc cref="ContenderPremiumPercent"/>
    public const int RetoolingPremiumPercent = 5;

    /// <summary>The age from which a free agent chases a title: winning weighs far more in his choice.</summary>
    public const int RingChasingAge = 30;

    /// <summary>A season this good makes a team a contender on its own, whatever the season before.</summary>
    public const int BreakoutWinPercent = 62;

    /// <summary>How many of a team's best players its rotation is: a free agent must beat the last of them to be worth a real offer.</summary>
    public const int RotationSize = 8;

    /// <summary>Per cent a free agent's price drops each round he goes unsigned.</summary>
    public const int PriceDropPercentPerRound = 12;

    /// <summary>The age from which a rebuilding team will not commit to a player.</summary>
    public const int RebuildingAgeLimit = 30;

    /// <summary>
    /// A team's direction from last season's record and the age of its roster: a winning team contends;
    /// a losing one with a young core retools; an old losing one rebuilds.
    /// </summary>
    public static CompetitiveDirection DirectionFor(TeamRecord? lastSeason, IReadOnlyList<Player> roster, DateOnly asOf, int peakAgeStart) =>
        DirectionFor(lastSeason, seasonBefore: null, roster, asOf, peakAgeStart);

    /// <summary>
    /// A team's direction with memory: a contender is a team that won <see cref="ContendingWinPercent"/>
    /// per cent last season, or over the last two seasons together — so one bad year (an injury-hit
    /// season) does not turn a contender into a seller, and one lucky year does not make a builder spend
    /// like a contender unless it was a clear one (<see cref="BreakoutWinPercent"/>+).
    /// </summary>
    public static CompetitiveDirection DirectionFor(TeamRecord? lastSeason, TeamRecord? seasonBefore, IReadOnlyList<Player> roster, DateOnly asOf, int peakAgeStart)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var last = lastSeason ?? TeamRecord.None;
        var before = seasonBefore ?? TeamRecord.None;
        var wins = last.Wins + before.Wins;
        var games = last.Games + before.Games;
        var contendedLast = last.Games > 0 && last.Wins * 100 >= ContendingWinPercent * last.Games;
        var contendedOverTwo = before.Games > 0 && wins * 100 >= ContendingWinPercent * games;
        var breakout = last.Games > 0 && last.Wins * 100 >= BreakoutWinPercent * last.Games;
        if (seasonBefore is null ? contendedLast : contendedOverTwo || breakout)
        {
            return CompetitiveDirection.Contending;
        }

        var young = roster.Count(player => player.AgeOn(asOf) < peakAgeStart);
        return young * 2 >= roster.Count ? CompetitiveDirection.Retooling : CompetitiveDirection.Rebuilding;
    }

    /// <summary>The payroll a team of this direction will commit to, or null in a league with no lines to read.</summary>
    public static Money? BudgetFor(CompetitiveDirection direction, CapThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        return direction switch
        {
            CompetitiveDirection.Contending => thresholds.FirstApron ?? thresholds.LuxuryTax ?? thresholds.SoftCap,
            CompetitiveDirection.Retooling => thresholds.LuxuryTax ?? thresholds.SoftCap,
            _ => thresholds.SoftCap,
        };
    }

    /// <summary>What a team owes in a season on every live contract, options not yet decided excluded.</summary>
    public static Money PayrollFor(TeamId teamId, IReadOnlyCollection<Contract> contracts, Season season)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var total = 0L;
        foreach (var contract in contracts)
        {
            if (contract.TeamId != teamId || contract.IsTerminated)
            {
                continue;
            }

            if (contract.TermFor(season) is { } term && term.OptionStatus is not (ContractOptionStatus.Pending or ContractOptionStatus.Declined))
            {
                total += term.Compensation.SmallestUnits;
            }
        }

        return new Money(total);
    }

    /// <summary>
    /// How long a contract an AI team offers a player of this age: long for the young, short for the
    /// old, never past the league's limit.
    /// </summary>
    public static int TermFor(int age, bool star, int? maximumSeasons)
    {
        var term = age switch
        {
            <= 27 => star ? 5 : 4,
            <= 29 => 3,
            <= 31 => 2,
            _ => 1,
        };

        return maximumSeasons is int maximum ? Math.Min(term, maximum) : term;
    }

    /// <summary>
    /// Which of a team's players in the final season of their contracts it re-signs, best first, at
    /// his asking price (what he accepts from his own team), while the budget allows — or, for one of
    /// its three best players, up to the second apron on a contending team and the tax line on any other. A player is worth keeping
    /// if he is in the rotation, or young with a ceiling above the team's fifth-best player.
    /// </summary>
    public static IReadOnlyList<ReSigningDecision> ReSignings(
        Team team,
        IReadOnlyList<Player> roster,
        IReadOnlyCollection<Contract> contracts,
        Season currentSeason,
        NegotiationRules rules,
        CapThresholds thresholds,
        CompetitiveDirection direction,
        DateOnly asOf,
        SalaryScale? scale = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(thresholds);

        var next = new Season(currentSeason.Year + 1);
        var ranked = roster.OrderByDescending(player => player.Rating.Overall).ThenBy(player => player.Id.Value, StringComparer.Ordinal).ToList();
        var rotationFloor = ranked.Count >= RotationSize ? ranked[RotationSize - 1].Rating.Overall : 0;
        var fifthBest = ranked.Count >= 5 ? ranked[4].Rating.Overall : 0;
        var topThree = ranked.Take(3).Select(player => player.Id).ToHashSet();

        var expiring = ranked.Where(player =>
        {
            var live = contracts.Where(contract => contract.PlayerId == player.Id && contract.TeamId == team.Id && !contract.IsTerminated).ToList();
            return live.Any(contract => contract.LastSeason == currentSeason) && !live.Any(contract => contract.FirstSeason.Year > currentSeason.Year);
        }).ToList();

        var payroll = PayrollFor(team.Id, contracts, next).SmallestUnits;
        var committedPlayers = contracts
            .Where(contract => contract.TeamId == team.Id && !contract.IsTerminated && contract.TermFor(next) is not null)
            .Select(contract => contract.PlayerId)
            .Distinct()
            .Count();
        var budget = BudgetFor(direction, thresholds)?.SmallestUnits;
        var stretch = thresholds.SecondApron?.SmallestUnits ?? budget;
        var maximumRoster = team.RosterLimits.MaximumPlayers;

        var decisions = new List<ReSigningDecision>();
        foreach (var player in expiring)
        {
            var age = player.AgeOn(asOf);
            var inRotation = player.Rating.Overall >= rotationFloor;
            var prospect = age <= 24 && player.Rating.PotentialAbility > fifthBest;
            if (!inRotation && !prospect)
            {
                continue;
            }

            if (direction == CompetitiveDirection.Rebuilding && age >= RebuildingAgeLimit && !topThree.Contains(player.Id))
            {
                continue;
            }

            if (committedPlayers >= maximumRoster)
            {
                break;
            }

            var ask = PreferenceModel.AskingPriceFor(player, rules, thresholds, scale);
            if (ask is null)
            {
                continue;
            }

            var salary = RoundToTenThousand(ask.SmallestUnits);
            // One of its three best players is kept on his incumbent rights past the direction's budget:
            // a contender up to the second apron, anyone else up to the tax line. A rebuilding team whose
            // budget is the cap does not let its best young player walk to stay under it.
            var keepLine = thresholds.LuxuryTax?.SmallestUnits is long tax && budget is long within ? Math.Max(tax, within) : budget;
            var limit = topThree.Contains(player.Id)
                ? direction == CompetitiveDirection.Contending ? stretch : keepLine
                : budget;
            if (limit is long line && payroll + salary > line)
            {
                continue;
            }

            var seasons = TermFor(age, topThree.Contains(player.Id), rules.MaximumSeasonsFor(isIncumbentTeam: true));
            payroll += salary;
            committedPlayers++;
            decisions.Add(new ReSigningDecision(
                team.Id,
                player.Id,
                seasons,
                new Money(salary),
                new RuleFinding(KeepCode,
                    $"{team.Name} ({direction}) keeps {player.FullName} ({player.Rating.Overall}, age {age}) for {ExplanationText.Count(seasons, "season")} at his asking price of {ExplanationText.Money(salary)}; next season's payroll stays within {(limit is long shown ? ExplanationText.Money(shown) : "no line")}.",
                    team.Id)));
        }

        return decisions;
    }

    /// <summary>
    /// A team's best offers in one market round, most valuable first: for every free agent who would
    /// improve its rotation (or fill a roster below the minimum), an offer at his price for the round,
    /// re-run through the real signing validator — at the price, or else at what its over-cap allowance
    /// allows, or else at the minimum once his price has fallen near it — and inside the team's budget.
    /// </summary>
    public static IReadOnlyList<MarketOffer> MarketOffers(
        Team team,
        IReadOnlyCollection<Player> freeAgents,
        MarketContext context,
        CompetitiveDirection direction,
        DateOnly asOf,
        int round,
        int maximumOffers)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(freeAgents);
        ArgumentNullException.ThrowIfNull(context);

        if (team.RosterCount >= team.RosterLimits.MaximumPlayers)
        {
            return [];
        }

        var roster = context.RosterOf(team);
        var ranked = roster.Select(player => player.Rating.Overall).OrderDescending().ToList();
        var replacement = ranked.Count >= RotationSize ? ranked[RotationSize - 1] : 0;
        var shortOfMinimum = team.RosterCount < team.RosterLimits.MinimumPlayers;
        var shortOfDepth = team.RosterCount < team.RosterLimits.MaximumPlayers - DepthSpotsKeptOpen;
        var weakest = ranked.Count > 0 ? ranked[^1] : 0;
        var byPosition = roster.GroupBy(player => player.Position).ToDictionary(group => group.Key, group => group.Count());
        var payroll = PayrollFor(team.Id, context.Contracts, context.CurrentSeason).SmallestUnits;
        var budget = BudgetFor(direction, context.CapThresholds)?.SmallestUnits;
        var minimumSalary = context.NegotiationRules.CompensationFloor.FloorFor(0)?.SmallestUnits ?? 0;
        var validator = new SigningValidator();

        // Below the payroll floor a team pays the shortfall anyway, so it spends it: it bids for anyone
        // near its rotation, and overpays on one-season deals up to the gap — the real league's
        // floor-team behaviour, rather than a payroll left at a quarter of the cap.
        var floorGap = context.CapThresholds.PayrollFloor is { } floor ? Math.Max(0, floor.SmallestUnits - payroll) : 0;

        var scored = new List<(Player Player, int Value)>();
        foreach (var player in freeAgents)
        {
            var age = player.AgeOn(asOf);
            var value = player.Rating.Overall - replacement;
            value += age <= 24 ? Math.Max(0, player.Rating.PotentialAbility - player.Rating.Overall) / (direction == CompetitiveDirection.Contending ? 6 : 3) : 0;
            value -= direction == CompetitiveDirection.Rebuilding && age >= RebuildingAgeLimit ? 8 : 0;
            value += byPosition.GetValueOrDefault(player.Position) < 2 ? 3 : 0;
            if (shortOfMinimum)
            {
                value = Math.Max(value, player.Rating.Overall - 40);
            }
            else if (floorGap > 0 && (player.Rating.Overall >= replacement - FloorRotationReach || (shortOfDepth && player.Rating.Overall > weakest)))
            {
                value = Math.Max(value, 1 + player.Rating.Overall - replacement + FloorRotationReach);
            }
            else if (shortOfDepth && player.Rating.Overall > weakest)
            {
                // Depth: a roster short of its full complement takes anyone better than its weakest
                // player — a contender preferring the veteran who has done it before, a rebuilding team
                // the young player it can develop. This is where the league's veterans find work.
                var depth = 1 + ((player.Rating.Overall - weakest) / 3) + direction switch
                {
                    CompetitiveDirection.Contending => age >= 28 ? 2 : 0,
                    CompetitiveDirection.Rebuilding => age <= 24 ? 2 : 0,
                    _ => 0,
                };
                value = Math.Max(value, depth);
            }

            if (value > 0)
            {
                scored.Add((player, value));
            }
        }

        var offers = new List<MarketOffer>();
        foreach (var (player, value) in scored.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Player.Id.Value, StringComparer.Ordinal))
        {
            if (offers.Count >= maximumOffers)
            {
                break;
            }

            var ask = PreferenceModel.AskingPriceFor(player, context.NegotiationRules, context.CapThresholds, context.SalaryScale)?.SmallestUnits;
            if (ask is null)
            {
                continue;
            }

            var premium = direction switch
            {
                CompetitiveDirection.Contending => ContenderPremiumPercent,
                CompetitiveDirection.Retooling => RetoolingPremiumPercent,
                _ => 0,
            };
            var price = RoundToTenThousand(Math.Max(minimumSalary, ask.Value * Math.Max(0, 100 + premium - (PriceDropPercentPerRound * round)) / 100));
            var seasons = TermFor(player.AgeOn(asOf), star: false, context.NegotiationRules.MaximumSeasonsFor(isIncumbentTeam: false));
            var floorOverpay = false;
            if (floorGap > 0)
            {
                // The gap spread across the spots it has left to fill, never less than twice his price.
                var openSpots = Math.Max(1, team.RosterLimits.MaximumPlayers - DepthSpotsKeptOpen - team.RosterCount - offers.Count);
                var ceiling = context.NegotiationRules.CompensationCeiling.CeilingFor(player.SeasonsOfService, context.CapThresholds.SoftCap)?.SmallestUnits ?? long.MaxValue;
                var overpaid = RoundToTenThousand(Math.Min(ceiling, Math.Min(floorGap, Math.Max(floorGap / openSpots, Math.Max(price, ask.Value) * FloorOverpayPercent / 100))));
                if (overpaid > price)
                {
                    price = overpaid;
                    seasons = 1;
                    floorOverpay = true;
                }
            }
            var allowance = context.NegotiationRules.StandardOverCapAllowance?.SmallestUnits;
            var candidates = new List<long> { price };
            if (allowance is long pot && pot < price && pot * 100 >= price * PreferenceModel.ReservationPercentOfAsk)
            {
                candidates.Add(RoundToTenThousand(pot));
            }

            if (price <= minimumSalary * 3 / 2)
            {
                candidates.Add(minimumSalary);
            }

            foreach (var salary in candidates.Distinct())
            {
                var isMinimum = salary <= minimumSalary;
                if (!isMinimum && budget is long line && payroll + salary > line)
                {
                    continue;
                }

                var offer = BuildOffer(team.Id, player.Id, context.CurrentSeason, isMinimum ? 1 : seasons, new Money(salary), round);
                if (offer is null)
                {
                    continue;
                }

                var assessment = validator.Validate(offer, (context with { Player = player }).SigningContextFor(team));
                if (assessment.IsFailure || !assessment.Value.IsLegal)
                {
                    continue;
                }

                offers.Add(new MarketOffer(offer, value, new RuleFinding(BidCode,
                    $"{team.Name} ({direction}) bids {ExplanationText.Money(salary)} a season for {ExplanationText.Count(offer.Terms.Count, "season")} on {player.FullName} ({player.Rating.Overall}), who would {(shortOfMinimum ? "fill a roster short of the minimum" : player.Rating.Overall > replacement ? $"improve on its eighth man ({replacement})" : $"add depth over its weakest player ({weakest})")}{(floorOverpay ? $" — overpaying for one season, {ExplanationText.Money(floorGap)} short of the payroll floor" : string.Empty)}.",
                    team.Id)));
                if (floorOverpay)
                {
                    floorGap = Math.Max(0, floorGap - salary);
                }

                break;
            }
        }

        return offers;
    }

    /// <summary>
    /// Which offer a free agent takes: the most money a season, weighted a little toward a winning
    /// team (a .700 team's offer counts eight per cent more than a .300 team's), then the longer
    /// deal, then the team identifier — so the same offers always produce the same choice.
    /// </summary>
    public static (Offer Offer, RuleFinding Finding) Choose(IReadOnlyList<Offer> offers, IReadOnlyDictionary<TeamId, TeamRecord> lastSeason, string playerName) =>
        Choose(offers, lastSeason, playerName, ringChaser: false);

    /// <summary>
    /// The same choice for a veteran chasing a title (<paramref name="ringChaser"/>): winning weighs three
    /// times as much — a .700 team's offer counts about a quarter more than a .300 team's — which is how a
    /// contender fills its bench with good veterans for less than a losing team would pay them.
    /// </summary>
    public static (Offer Offer, RuleFinding Finding) Choose(IReadOnlyList<Offer> offers, IReadOnlyDictionary<TeamId, TeamRecord> lastSeason, string playerName, bool ringChaser)
    {
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(lastSeason);
        if (offers.Count == 0)
        {
            throw new ArgumentException("A player chooses among at least one offer.", nameof(offers));
        }

        long Utility(Offer offer)
        {
            var record = lastSeason.GetValueOrDefault(offer.TeamId);
            var winPercent = record.Games == 0 ? 50 : record.Wins * 100 / record.Games;
            return ringChaser
                ? offer.FirstSeasonCompensation.SmallestUnits * (88 + (winPercent * 3 / 5)) / 100
                : offer.FirstSeasonCompensation.SmallestUnits * (96 + (winPercent / 5)) / 100;
        }

        var chosen = offers
            .OrderByDescending(Utility)
            .ThenByDescending(offer => offer.Terms.Count)
            .ThenBy(offer => offer.TeamId.Value, StringComparer.Ordinal)
            .First();

        return (chosen, new RuleFinding(ChoiceCode,
            offers.Count == 1
                ? $"{playerName} accepts the only offer, {ExplanationText.Money(chosen.FirstSeasonCompensation)} a season."
                : $"{playerName} chooses {ExplanationText.Money(chosen.FirstSeasonCompensation)} a season for {ExplanationText.Count(chosen.Terms.Count, "season")} over {ExplanationText.Count(offers.Count - 1, "other offer")}, weighing the money and how the team won last season.",
            chosen.TeamId));
    }

    /// <summary>
    /// Settles one market round: every free agent with offers, best player first, takes the one he
    /// chooses (<see cref="Choose"/>) from the teams that have not already signed someone this round —
    /// so a team adds at most one player a round and has to re-read its needs before the next.
    /// </summary>
    public static IReadOnlyList<(MarketOffer Bid, RuleFinding Choice)> ResolveRound(
        IReadOnlyList<MarketOffer> offers,
        IReadOnlyDictionary<PlayerId, Player> players,
        IReadOnlyDictionary<TeamId, TeamRecord> lastSeason,
        DateOnly asOf = default)
    {
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(lastSeason);

        var signedThisRound = new HashSet<TeamId>();
        var accepted = new List<(MarketOffer, RuleFinding)>();
        var byPlayer = offers
            .GroupBy(offer => offer.Offer.PlayerId)
            .Select(group => (Player: players[group.Key], Offers: group.ToList()))
            .OrderByDescending(entry => entry.Player.Rating.Overall)
            .ThenBy(entry => entry.Player.Id.Value, StringComparer.Ordinal);

        foreach (var (player, playerOffers) in byPlayer)
        {
            var open = playerOffers.Where(offer => !signedThisRound.Contains(offer.Offer.TeamId)).ToList();
            if (open.Count == 0)
            {
                continue;
            }

            var ringChaser = player.AgeOn(asOf) >= RingChasingAge;
            var (chosen, finding) = Choose(open.Select(offer => offer.Offer).ToList(), lastSeason, player.FullName, ringChaser);
            signedThisRound.Add(chosen.TeamId);
            accepted.Add((open.First(offer => offer.Offer == chosen), finding));
        }

        return accepted;
    }

    /// <summary>
    /// Every option on next season's term of a team's contracts, decided now: a team takes up its option
    /// on a player in its rotation, or a young one whose ceiling beats its fifth-best, and declines the
    /// rest; a player takes up his own option when it pays at least his asking price and walks away from
    /// it to free agency when the market would pay him more.
    /// </summary>
    public static IReadOnlyList<(ContractId ContractId, Season Season, bool Exercise, RuleFinding Rationale)> OptionDecisions(
        Team team,
        IReadOnlyList<Player> roster,
        IReadOnlyCollection<Contract> contracts,
        Season currentSeason,
        NegotiationRules rules,
        CapThresholds thresholds,
        DateOnly asOf,
        SalaryScale? scale = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(contracts);

        var next = new Season(currentSeason.Year + 1);
        var ranked = roster.OrderByDescending(player => player.Rating.Overall).ThenBy(player => player.Id.Value, StringComparer.Ordinal).ToList();
        var rotationFloor = ranked.Count >= RotationSize ? ranked[RotationSize - 1].Rating.Overall : 0;
        var fifthBest = ranked.Count >= 5 ? ranked[4].Rating.Overall : 0;
        var decisions = new List<(ContractId, Season, bool, RuleFinding)>();
        foreach (var player in ranked)
        {
            var contract = contracts.FirstOrDefault(candidate => candidate.PlayerId == player.Id && candidate.TeamId == team.Id && !candidate.IsTerminated);
            if (contract?.TermFor(next) is not { IsPendingOption: true } term)
            {
                continue;
            }

            if (term.Option == ContractOptionKind.Team)
            {
                var keep = player.Rating.Overall >= rotationFloor || (player.AgeOn(asOf) <= 24 && player.Rating.PotentialAbility > fifthBest);
                decisions.Add((contract.Id, next, keep, new RuleFinding(keep ? "ai_option.team_exercises" : "ai_option.team_declines",
                    keep
                        ? $"{team.Name} takes up its {next.Year} option on {player.FullName} ({player.Rating.Overall}, potential {player.Rating.PotentialAbility}) at {ExplanationText.Money(term.Compensation.SmallestUnits)}."
                        : $"{team.Name} declines its {next.Year} option on {player.FullName} ({player.Rating.Overall}): he has not earned a place in its rotation, and becomes a free agent when this season ends.",
                    team.Id)));
            }
            else
            {
                var ask = PreferenceModel.AskingPriceFor(player, rules, thresholds, scale)?.SmallestUnits ?? 0;
                var stay = term.Compensation.SmallestUnits >= ask;
                decisions.Add((contract.Id, next, stay, new RuleFinding(stay ? "ai_option.player_exercises" : "ai_option.player_declines",
                    stay
                        ? $"{player.FullName} takes up his {next.Year} option at {ExplanationText.Money(term.Compensation.SmallestUnits)}, at least what the market would pay him ({ExplanationText.Money(ask)})."
                        : $"{player.FullName} declines his {next.Year} option at {ExplanationText.Money(term.Compensation.SmallestUnits)}: the market would pay him {ExplanationText.Money(ask)}.",
                    team.Id)));
            }
        }

        return decisions;
    }

    /// <summary>
    /// The qualifying offers a team tenders (restricted free agency, <see cref="IncumbentRightsRules"/>): to
    /// each player in the final season of a contract that started within the league's restricted window
    /// (a rookie deal) whom it has not re-signed but still wants — a rotation player, or a young one whose
    /// ceiling beats its fifth-best — a one-season offer at the stated share of his last salary. Tendered,
    /// he is restricted: the team may match any offer he takes elsewhere.
    /// </summary>
    public static IReadOnlyList<(PlayerId PlayerId, Money Salary, RuleFinding Rationale)> QualifyingOffers(
        Team team,
        IReadOnlyList<Player> roster,
        IReadOnlyCollection<Contract> contracts,
        Season currentSeason,
        IncumbentRightsRules rights,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(rights);
        if (!rights.HasRestrictedFreeAgency)
        {
            return [];
        }

        var ranked = roster.OrderByDescending(player => player.Rating.Overall).ThenBy(player => player.Id.Value, StringComparer.Ordinal).ToList();
        var rotationFloor = ranked.Count >= RotationSize ? ranked[RotationSize - 1].Rating.Overall : 0;
        var fifthBest = ranked.Count >= 5 ? ranked[4].Rating.Overall : 0;
        var tenders = new List<(PlayerId, Money, RuleFinding)>();
        foreach (var player in ranked)
        {
            var live = contracts.Where(contract => contract.PlayerId == player.Id && contract.TeamId == team.Id && !contract.IsTerminated).ToList();
            var expiring = live.FirstOrDefault(contract => contract.LastSeason == currentSeason);
            if (expiring is null || live.Any(contract => contract.FirstSeason.Year > currentSeason.Year))
            {
                continue;
            }

            var serviceAtStart = player.SeasonsOfService - (currentSeason.Year - expiring.FirstSeason.Year);
            var wanted = player.Rating.Overall >= rotationFloor || (player.AgeOn(asOf) <= 24 && player.Rating.PotentialAbility > fifthBest);
            if (serviceAtStart >= rights.RestrictedServiceSeasons || !wanted)
            {
                continue;
            }

            var last = expiring.TermFor(currentSeason)!.Compensation.SmallestUnits;
            var salary = new Money(RoundToTenThousand(last * rights.QualifyingOfferPercent / 100));
            tenders.Add((player.Id, salary, new RuleFinding("ai_offseason.qualifying_offer",
                $"{team.Name} tenders {player.FullName} a qualifying offer of {ExplanationText.Money(salary.SmallestUnits)}: he is a restricted free agent, and {team.Name} may match any offer he takes.",
                team.Id)));
        }

        return tenders;
    }

    /// <summary>
    /// Whether a team matches the offer its restricted free agent accepted elsewhere: when he is still a
    /// player it wants (in its rotation, or young with a ceiling above its fifth-best) and the offer keeps
    /// its payroll within the second apron — a team will stretch to keep a young player it drafted.
    /// </summary>
    public static bool MatchesOfferSheet(Team incumbent, IReadOnlyList<Player> roster, Player player, Offer offer, IReadOnlyCollection<Contract> contracts, Season season, CapThresholds thresholds, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(incumbent);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (incumbent.RosterCount >= incumbent.RosterLimits.MaximumPlayers)
        {
            return false;
        }

        var ranked = roster.Select(member => member.Rating.Overall).OrderDescending().ToList();
        var rotationFloor = ranked.Count >= RotationSize ? ranked[RotationSize - 1] : 0;
        var fifthBest = ranked.Count >= 5 ? ranked[4] : 0;
        var wanted = player.Rating.Overall >= rotationFloor || (player.AgeOn(asOf) <= 24 && player.Rating.PotentialAbility > fifthBest);
        var line = thresholds.SecondApron ?? thresholds.FirstApron ?? thresholds.LuxuryTax;
        return wanted && (line is null || PayrollFor(incumbent.Id, contracts, season).SmallestUnits + offer.FirstSeasonCompensation.SmallestUnits <= line.SmallestUnits);
    }

    /// <summary>
    /// Whether a team signs one of its draft picks: a first-round pick whenever it has a roster spot (his
    /// contract is guaranteed by the scale); a second-round pick only if a spot would still be left for
    /// the market afterwards and he is at least as good as the worst player it carries, or his ceiling
    /// beats its eighth man — otherwise he goes to free agency, as most second-round picks do.
    /// </summary>
    public static bool SignsDraftPick(int round, PlayerRating rating, IReadOnlyList<Player> roster, int rosterCount, int maximumRoster)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(roster);
        if (round <= 1)
        {
            return rosterCount < maximumRoster;
        }

        if (rosterCount >= maximumRoster - 1)
        {
            return false;
        }

        var ranked = roster.Select(player => player.Rating.Overall).OrderDescending().ToList();
        var worst = ranked.Count == 0 ? 0 : ranked[^1];
        var eighth = ranked.Count >= RotationSize ? ranked[RotationSize - 1] : 0;
        return rating.Overall >= worst || rating.PotentialAbility > eighth;
    }

    /// <summary>
    /// A standing offer the person playing made before the summer (<c>docs/franchise-realism.md</c> → §19),
    /// as a bid in one market round — or null, with the reason, when it cannot be one this round. It
    /// bids when his team has a roster spot, the salary reaches what the player will take this round
    /// (his asking price falling round by round as the AI's prices do, down to the share of it he would
    /// still accept) and the signing is legal. The player then chooses among it and every AI bid by the
    /// same rule (<see cref="ResolveRound"/>): the person gets no better treatment than a front office.
    /// </summary>
    public static (MarketOffer? Bid, RuleFinding Reason) StandingOffer(Team team, Player player, Money salary, int seasons, MarketContext context, int round)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(context);

        if (team.RosterCount >= team.RosterLimits.MaximumPlayers)
        {
            return (null, new RuleFinding(StandingRosterFullCode, $"{team.Name} has no roster spot for {player.FullName}.", team.Id));
        }

        var minimumSalary = context.NegotiationRules.CompensationFloor.FloorFor(player.SeasonsOfService)?.SmallestUnits ?? 0;
        var ask = PreferenceModel.AskingPriceFor(player, context.NegotiationRules, context.CapThresholds, context.SalaryScale)?.SmallestUnits ?? minimumSalary;
        var roundPrice = Math.Max(minimumSalary, ask * Math.Max(0, 100 - (PriceDropPercentPerRound * round)) / 100);
        var acceptable = RoundToTenThousand(Math.Max(minimumSalary, roundPrice * PreferenceModel.ReservationPercentOfAsk / 100));
        if (salary.SmallestUnits < acceptable)
        {
            return (null, new RuleFinding(StandingBelowAskCode,
                $"{player.FullName} will not take {ExplanationText.Money(salary)} a season this round: he wants at least {ExplanationText.Money(acceptable)}.", team.Id));
        }

        var offer = BuildOffer(team.Id, player.Id, context.CurrentSeason, seasons, salary, round);
        if (offer is null)
        {
            return (null, new RuleFinding(StandingIllegalCode, $"The offer to {player.FullName} does not state a valid contract.", team.Id));
        }

        var assessment = new SigningValidator().Validate(offer, (context with { Player = player }).SigningContextFor(team));
        if (assessment.IsFailure || !assessment.Value.IsLegal)
        {
            var reasons = assessment.IsFailure
                ? assessment.Errors.Select(error => error.Message)
                : assessment.Value.Violations.Select(violation => violation.Explanation);
            return (null, new RuleFinding(StandingIllegalCode, $"{team.Name} cannot sign {player.FullName} on these terms: {string.Join(" ", reasons)}", team.Id));
        }

        var bid = new RuleFinding(BidCode,
            $"{team.Name} offers {ExplanationText.Money(salary)} a season for {ExplanationText.Count(offer.Terms.Count, "season")} to {player.FullName} ({player.Rating.Overall}).",
            team.Id);
        return (new MarketOffer(offer, 0, bid), bid);
    }

    private const string StandingRosterFullCode = "summer_offer.roster_full";
    private const string StandingBelowAskCode = "summer_offer.below_asking_price";
    private const string StandingIllegalCode = "summer_offer.not_legal";

    private static Offer? BuildOffer(TeamId teamId, PlayerId playerId, Season start, int seasons, Money salary, int round)
    {
        var terms = Enumerable.Range(0, Math.Max(1, seasons))
            .Select(offset => new ContractSeasonTerm(new Season(start.Year + offset), salary, salary))
            .ToList();
        var offer = Offer.Create(new OfferId($"MARKET-{round}-{teamId.Value}-{playerId.Value}"), teamId, playerId, terms);
        return offer.IsSuccess ? offer.Value : null;
    }

    private static long RoundToTenThousand(long amount) => Math.Max(0, (amount + 5_000) / 10_000 * 10_000);
}
