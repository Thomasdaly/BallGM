using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Trades;

namespace BallGM.Rules.AI;

/// <summary>One veteran sale: the trade, the two sides, the veteran, and why both made it.</summary>
public sealed record VeteranSale(
    TradeProposal Proposal,
    TeamId SellerTeamId,
    TeamId BuyerTeamId,
    PlayerId VeteranId,
    IReadOnlyList<PlayerId> SalaryBack,
    IReadOnlyList<DraftPickId> Picks,
    bool IsStar,
    RuleFinding Rationale);

/// <summary>
/// Veterans sold for picks (<c>docs/franchise-realism.md</c> → F6): a losing team that is not contending
/// sends a good veteran to a contender for draft picks and the salary it needs to take back — the trade
/// that makes the bad teams worse and the good ones better, which is most of what spreads a real league's
/// table. Without it every non-contender kept its veterans and the worst teams still won 25–30 games.
/// <para>
/// Who sells: a team that won under <see cref="SellerWinPercent"/> per cent last season and is not
/// contending, its veterans of <see cref="MinimumAge"/> or older. Who buys: a contender for whom the
/// veteran would be one of its best <see cref="BuyerRotationSlot"/> players. The price is set by where the
/// veteran ranks in the league (<see cref="PriceFor"/>), paid in the buyer's picks — latest first, spaced
/// for the retained-round rule — and the buyer sends back players outside its top <see cref="BuyerProtectedPlayers"/>,
/// biggest contracts first, until the salaries match. Every sale is
/// re-validated by <see cref="TradeValidator"/>; one a team a summer.
/// </para>
/// </summary>
public static class VeteranSaleModel
{
    private const string SaleCode = "ai_trade.veteran_sale";

    /// <summary>A team that won fewer than this per cent of its games last season sells.</summary>
    public const int SellerWinPercent = 45;

    /// <summary>The youngest veteran a seller moves: younger players are its future.</summary>
    public const int MinimumAge = 28;

    /// <summary>
    /// The league's best players by count, and each seller's own best player: franchise players a losing
    /// team builds around rather than sells — unless he is <see cref="FranchisePlayerSaleAge"/> or older
    /// and the rebuild will outlast him. Stars changing teams every summer is exactly the carousel this
    /// game must not have. Wider than the thirty a stability check counts, because a summer's ageing
    /// reshuffles the ranks.
    /// </summary>
    public const int FranchisePlayers = 45;

    /// <inheritdoc cref="FranchisePlayers"/>
    public const int FranchisePlayerSaleAge = 32;

    /// <summary>
    /// At most one sale a summer of a player among the league's <see cref="FranchisePlayers"/> best: an
    /// ageing star moving to a contender is a summer's blockbuster, not a summer's pattern.
    /// </summary>
    public const int StarSalesPerSummer = 1;

    /// <summary>A contender buys only a player who would be among its best this many.</summary>
    public const int BuyerRotationSlot = 6;

    /// <summary>A buyer's best players are never sent back as salary: the veteran joins them.</summary>
    public const int BuyerProtectedPlayers = 5;

    /// <summary>The most players a buyer sends back to match salary.</summary>
    public const int MaximumSalaryBack = 3;

    public static IReadOnlyList<VeteranSale> Find(
        TradeContext context,
        IReadOnlyDictionary<TeamId, CompetitiveDirection> directions,
        IReadOnlyDictionary<TeamId, TeamRecord> lastSeason,
        DateOnly asOf,
        IReadOnlySet<TeamId>? excludedTeams = null,
        bool excludedStarSold = false,
        bool inSeason = false,
        InjuryReport? injuries = null)
    {
        injuries ??= InjuryReport.None;
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directions);
        ArgumentNullException.ThrowIfNull(lastSeason);

        var season = context.CurrentSeason;
        var playersById = context.Players.ToDictionary(player => player.Id);
        var rostered = context.Teams.SelectMany(team => team.PlayerIds).Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
        var leagueOveralls = rostered.Select(player => player.Rating.Overall).OrderDescending().ToArray();
        var used = new HashSet<TeamId>(excludedTeams ?? new HashSet<TeamId>());
        var validator = new TradeValidator();
        var sales = new List<VeteranSale>();

        // Signed this summer (a re-signing, an extension starting now, a free agent): not traded the
        // summer he signed — a team does not keep a player only to move him, and real leagues bar it.
        // In season (the trade deadline) a summer signing has been with his team for months and moves
        // like anyone else, as in a real league.
        bool SignedThisSummer(PlayerId playerId, TeamId teamId) => !inSeason && context.Contracts
            .Any(contract => contract.PlayerId == playerId && contract.TeamId == teamId && !contract.IsTerminated && contract.FirstSeason.Year == season.Year);
        long SalaryOf(PlayerId playerId, TeamId teamId) => context.Contracts
            .FirstOrDefault(contract => contract.PlayerId == playerId && contract.TeamId == teamId && !contract.IsTerminated && contract.TermFor(season) is not null)?
            .TermFor(season)!.Compensation.SmallestUnits ?? 0;
        List<Player> RosterOf(Team team) => team.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();

        bool IsSeller(Team team) =>
            directions.GetValueOrDefault(team.Id) != CompetitiveDirection.Contending &&
            lastSeason.TryGetValue(team.Id, out var record) && record.Games > 0 && record.Wins * 100 < SellerWinPercent * record.Games;

        var veterans = context.Teams
            .Where(team => !used.Contains(team.Id) && IsSeller(team))
            .SelectMany(team => RosterOf(team).Select(player => (Team: team, Player: player)))
            .Where(entry => entry.Player.AgeOn(asOf) >= MinimumAge && SalaryOf(entry.Player.Id, entry.Team.Id) > 0 && !SignedThisSummer(entry.Player.Id, entry.Team.Id) && PriceFor(Rank(entry.Player.Rating.Overall)) is not null)
            // Out past the season's end, nobody buys him for this one (his contract is a dump's business).
            .Where(entry => injuries.DaysOutFor(entry.Player.Id) == 0 || injuries.ReturnsThisSeason(entry.Player.Id))
            .Where(entry => entry.Player.AgeOn(asOf) >= FranchisePlayerSaleAge || IsLastChance(entry.Player) ||
                (Above(entry.Player.Rating.Overall) >= FranchisePlayers && RosterOf(entry.Team).Max(player => player.Rating.Overall) > entry.Player.Rating.Overall))
            .OrderByDescending(entry => entry.Player.Rating.Overall)
            .ThenBy(entry => entry.Player.Id.Value, StringComparer.Ordinal)
            .ToList();

        int Above(int overall)
        {
            var above = Array.FindIndex(leagueOveralls, value => value <= overall);
            return above < 0 ? leagueOveralls.Length : above;
        }

        int Rank(int overall) => Above(overall) * 1_000 / Math.Max(1, leagueOveralls.Length);

        // A former All-Star, past thirty and hurt, but back before the postseason: a contender's
        // last-chance gamble, bought cheap.
        bool IsLastChance(Player player) =>
            injuries.IsPricedIn(player.Id) && injuries.FormerStars.Contains(player.Id) && player.AgeOn(asOf) >= InjuryReport.VeteranAge;

        var starSold = excludedStarSold;
        foreach (var (seller, veteran) in veterans)
        {
            var isStar = Above(veteran.Rating.Overall) < FranchisePlayers;
            if (used.Contains(seller.Id) || (isStar && starSold))
            {
                continue;
            }

            var salary = SalaryOf(veteran.Id, seller.Id);
            var (firsts, seconds) = PriceFor(Rank(veteran.Rating.Overall))!.Value;
            var hurt = injuries.IsPricedIn(veteran.Id);
            if (hurt)
            {
                // Hurt, he goes a tier cheaper: the buyer is paying for a chance, not a sure thing.
                (firsts, seconds) = Cheaper(firsts, seconds);
            }

            foreach (var buyer in context.Teams
                .Where(team => team.Id != seller.Id && !used.Contains(team.Id) && directions.GetValueOrDefault(team.Id) == CompetitiveDirection.Contending)
                .OrderByDescending(team => lastSeason.TryGetValue(team.Id, out var record) && record.Games > 0 ? record.Wins * 1_000 / record.Games : 0)
                .ThenBy(team => team.Id.Value, StringComparer.Ordinal))
            {
                var buyerRoster = RosterOf(buyer).OrderByDescending(player => player.Rating.Overall).ThenBy(player => player.Id.Value, StringComparer.Ordinal).ToList();
                if (buyerRoster.Count >= BuyerRotationSlot && veteran.Rating.Overall <= buyerRoster[BuyerRotationSlot - 1].Rating.Overall)
                {
                    continue;
                }

                var owned = context.DraftAssets.PicksControlledBy(buyer.FranchiseId)
                    .Where(pick => pick.DraftSeason.Year > season.Year && pick.DraftSeason.Year <= season.Year + context.DraftRules.TradableFutureDraftHorizon)
                    .OrderByDescending(pick => pick.DraftSeason.Year)
                    .ThenBy(pick => pick.OriginalFranchiseId.Value, StringComparer.Ordinal)
                    .ToList();
                var picks = SalaryDumpModel.SpacedPicks(owned.Where(pick => pick.Round == 1), firsts, context.DraftRules.RetainedRoundNumber == 1 ? context.DraftRules.RetainedRoundInterval : 1)
                    .Concat(SalaryDumpModel.SpacedPicks(owned.Where(pick => pick.Round == 2), seconds, context.DraftRules.RetainedRoundNumber == 2 ? context.DraftRules.RetainedRoundInterval : 1))
                    .ToList();
                if (picks.Count(pick => pick.Round == 1) < firsts || picks.Count(pick => pick.Round == 2) < seconds)
                {
                    continue;
                }

                // Salary back: the buyer's spare players, biggest contracts first (the fewest bodies that match),
                // the younger first among equals; each added only while the trade is still refused.
                var spare = buyerRoster.Skip(BuyerProtectedPlayers)
                    .Where(player => SalaryOf(player.Id, buyer.Id) > 0 && !SignedThisSummer(player.Id, buyer.Id))
                    .OrderByDescending(player => SalaryOf(player.Id, buyer.Id))
                    .ThenBy(player => player.AgeOn(asOf))
                    .ThenBy(player => player.Id.Value, StringComparer.Ordinal)
                    .ToList();
                var back = new List<Player>();
                VeteranSale? sale = null;
                while (true)
                {
                    var movements = new List<TradeAssetMovement> { TradeAssetMovement.Player(veteran.Id, seller.Id, buyer.Id) };
                    movements.AddRange(back.Select(player => TradeAssetMovement.Player(player.Id, buyer.Id, seller.Id)));
                    movements.AddRange(picks.Select(pick => TradeAssetMovement.DraftPick(pick.Id, buyer.Id, seller.Id)));
                    var proposal = TradeProposal.Create(
                        new TradeId($"AI-SALE-{season.Year}-{seller.Id.Value}-{veteran.Id.Value}-TO-{buyer.Id.Value}-{back.Count}"),
                        season,
                        [seller.Id, buyer.Id],
                        movements,
                        LeagueStateToken.From(context.Ledger));
                    if (proposal.IsFailure)
                    {
                        break;
                    }

                    var assessment = validator.Validate(proposal.Value, context);
                    if (assessment.IsSuccess && assessment.Value.IsLegal)
                    {
                        var price = string.Join(", ", picks.Select(pick => $"{pick.DraftSeason.Year} {(pick.Round == 1 ? "first" : "second")}-round pick"));
                        var salaryBack = back.Count == 0 ? string.Empty : $" and {string.Join(", ", back.Select(player => player.FullName))} to match salaries";
                        sale = new VeteranSale(proposal.Value, seller.Id, buyer.Id, veteran.Id, back.Select(player => player.Id).ToList(), picks.Select(pick => pick.Id).ToList(), isStar,
                            new RuleFinding(SaleCode,
                                $"{seller.Name}, out of contention, sell {veteran.FullName} ({veteran.Rating.Overall}, age {veteran.AgeOn(asOf)}, {ExplanationText.Money(salary)}) to contending {buyer.Name} for {(price.Length == 0 ? "salary relief" : price)}{salaryBack}{(IsLastChance(veteran) ? $" — a last-chance gamble on a former All-Star, out {injuries.DaysOutFor(veteran.Id)} more days but back before the postseason" : hurt ? $" — cheaper for the {injuries.DaysOutFor(veteran.Id)} days he is still out" : string.Empty)}.",
                                seller.Id));
                        break;
                    }

                    if (back.Count >= MaximumSalaryBack || back.Count >= spare.Count)
                    {
                        break;
                    }

                    back.Add(spare[back.Count]);
                }

                if (sale is not null)
                {
                    starSold |= isStar;
                    sales.Add(sale);
                    used.Add(seller.Id);
                    used.Add(buyer.Id);
                    break;
                }
            }
        }

        return sales;
    }

    /// <summary>
    /// What a veteran fetches, by how far from the top of the league he ranks (per mille): the top 5%
    /// two firsts, the top 12% a first and a second, the top 25% a first, the top 40% two seconds; a
    /// veteran below that is not worth a contender's picks, and is not sold.
    /// </summary>
    /// <summary>One price tier down: two firsts to one, a first to two seconds, two seconds to one.</summary>
    public static (int Firsts, int Seconds) Cheaper(int firsts, int seconds) => (firsts, seconds) switch
    {
        ( >= 2, _) => (firsts - 1, 0),
        (1, > 0) => (1, 0),
        (1, 0) => (0, 2),
        (0, > 1) => (0, seconds - 1),
        _ => (0, 0),
    };

    public static (int Firsts, int Seconds)? PriceFor(int topPerMille) => topPerMille switch
    {
        < 50 => (2, 0),
        < 120 => (1, 1),
        < 250 => (1, 0),
        < 400 => (0, 2),
        _ => null,
    };
}
