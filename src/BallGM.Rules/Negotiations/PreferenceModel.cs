using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Rules.Negotiations;

/// <summary>
/// What one player would take, and why — decomposed into four factors that never become one number.
/// <para>
/// Every factor returns its own 0–100 reading, its own materiality band, and its own sentence. The
/// sentence is not decoration: an AI front office that outbids a rival and still loses the player
/// has to be able to say which factor beat it, and a weighted total cannot answer that question
/// however it is presented.
/// </para>
/// <para>
/// Nothing here is stochastic. The seeded draw belongs to the resolver and only fires where
/// <see cref="PreferenceRanking"/> reports that no factor separates two offers at all.
/// </para>
/// </summary>
public sealed class PreferenceModel
{
    public const string MoneyAgainstRangeCode = "preference.money_against_league_range";
    public const string MoneyAgainstFieldCode = "preference.money_against_the_field";
    public const string TermCode = "preference.term_against_desired_length";
    public const string TeamFitCode = "preference.team_fit";
    public const string MarketDemandCode = "preference.market_demand";
    public const string ReservationMetCode = "preference.offer_clears_asking_price";
    public const string ReservationUnmetCode = "preference.offer_below_asking_price";
    public const string NoAskingPriceCode = "preference.no_asking_price_in_an_open_market";

    /// <summary>
    /// How far below the asking price a player will still sign. They settle a little; they do not
    /// settle indefinitely, and the gap between those two is what makes a market resolve on nobody
    /// a reachable outcome rather than a theoretical one.
    /// </summary>
    private const int ReservationPercentOfAsk = 85;

    // Materiality bands, per factor: how much better one offer has to read before this player would
    // actually notice. Money's is the tightest because money is the factor a GM bids with; market
    // demand's is the loosest because it is the noisiest signal and the least about the deal itself.
    private const int MoneyBand = 5;
    private const int TermBand = 10;
    private const int TeamFitBand = 10;
    private const int MarketDemandBand = 15;

    /// <summary>
    /// Reads one offer for one player, against the whole field of live offers — the field is an
    /// input rather than a lookup because market demand is not a property of an offer in isolation,
    /// and money in a league with no configured range can only be judged relatively.
    /// </summary>
    public OfferPreference Evaluate(Offer offer, IReadOnlyList<Offer> liveOffers, MarketContext context)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(liveOffers);
        ArgumentNullException.ThrowIfNull(context);

        var player = context.Player;
        var ask = AskingPrice(context);

        var contributions = new List<PreferenceContribution>
        {
            Money(offer, liveOffers, ask),
            Term(offer, context),
            TeamFit(offer, context),
            MarketDemand(offer, liveOffers),
        };

        var (meets, code, explanation) = Reservation(offer, ask, player);

        return new OfferPreference(offer.Id, offer.TeamId, contributions, meets, code, explanation);
    }

    /// <summary>
    /// What this player is asking per season: where their quality places them inside the range this
    /// league permits for their service. <c>null</c> in a league that configures no floor and no
    /// ceiling — an open market has no range to be placed inside, so the player has no asking price
    /// and any offer that pays something is one they will consider.
    /// </summary>
    public Money? AskingPrice(MarketContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var service = context.Player.SeasonsOfService;
        var floor = context.NegotiationRules.CompensationFloor.FloorFor(service);
        var ceiling = context.NegotiationRules.CompensationCeiling.CeilingFor(service, context.CapThresholds.SoftCap);

        return (floor, ceiling) switch
        {
            (null, null) => null,

            // A floor with no ceiling gives a starting point and no scale to climb: the honest ask is
            // the only figure the league states.
            (not null, null) => floor,

            (null, not null) => new Money(ceiling.SmallestUnits * QualityShare(context.Player) / 100),

            _ => new Money(
                floor.SmallestUnits +
                ((ceiling.SmallestUnits - floor.SmallestUnits) * QualityShare(context.Player) / 100)),
        };
    }

    /// <summary>
    /// Where a player sits between "asks the league minimum" and "asks the maximum", from their
    /// rating alone. A deliberately blunt line rather than a curve: it is a placeholder for the
    /// multi-attribute rating that arrives with the match engine, and a curve fitted to a single
    /// number would only look more authoritative than it is.
    /// </summary>
    private static int QualityShare(Player player) =>
        Math.Clamp((player.Rating.Overall - 40) * 2, 0, 100);

    private static PreferenceContribution Money(Offer offer, IReadOnlyList<Offer> liveOffers, Money? ask)
    {
        var offered = offer.FirstSeasonCompensation.SmallestUnits;

        if (ask is null || ask.SmallestUnits == 0)
        {
            // No configured range, so the only scale available is the rest of the market. In a league
            // with one offer on the table, that offer is the whole market and leads it.
            var best = liveOffers.Count == 0
                ? offered
                : liveOffers.Max(candidate => candidate.FirstSeasonCompensation.SmallestUnits);

            var relative = best <= 0 ? 0 : PreferenceContribution.Clamp((int)(offered * 100 / best));

            return new PreferenceContribution(
                PreferenceFactorKind.Money,
                relative,
                MoneyBand,
                MoneyAgainstFieldCode,
                best == offered
                    ? $"At {offered} in the first season this is the best money on the table, and this league sets no salary range to measure it against."
                    : $"At {offered} in the first season this trails the {best} another team is offering, and this league sets no salary range to measure either against.");
        }

        var asking = ask.SmallestUnits;
        var score = PreferenceContribution.Clamp((int)(50 + ((offered - asking) * 50 / asking)));

        return new PreferenceContribution(
            PreferenceFactorKind.Money,
            score,
            MoneyBand,
            MoneyAgainstRangeCode,
            offered >= asking
                ? $"{offered} in the first season meets the {asking} this player is asking for."
                : $"{offered} in the first season is {asking - offered} short of the {asking} this player is asking for.");
    }

    private static PreferenceContribution Term(Offer offer, MarketContext context)
    {
        var desired = DesiredSeasons(context);
        var offered = offer.SeasonCount;
        var distance = Math.Abs(offered - desired);
        var score = PreferenceContribution.Clamp(100 - (25 * distance));

        var sentence = distance == 0
            ? $"{offered} season(s) is exactly the security this player wants."
            : offered < desired
                ? $"{offered} season(s) is {distance} short of the {desired} this player wants at this stage of a career."
                : $"{offered} season(s) is {distance} more than the {desired} this player wants to commit to.";

        return new PreferenceContribution(PreferenceFactorKind.Term, score, TermBand, TermCode, sentence);
    }

    /// <summary>
    /// How long this player wants, from age, capped by what the league would even permit — a player
    /// cannot want six seasons in a league with a five-season limit, and a model that lets them want
    /// it manufactures a disappointment no team can fix.
    /// </summary>
    private static int DesiredSeasons(MarketContext context)
    {
        var age = context.Player.AgeOn(AgeReferenceDate(context));

        var wanted = age switch
        {
            <= 25 => 5,
            <= 29 => 4,
            <= 32 => 3,
            _ => 2,
        };

        var permitted = context.NegotiationRules.MaximumSeasonsFor(isIncumbentTeam: false);
        return permitted is null ? wanted : Math.Min(wanted, permitted.Value);
    }

    /// <summary>
    /// The day a season's ages are read on. The same convention the league overview uses: a season
    /// labelled by its opening year is aged on the first of October in that year.
    /// </summary>
    private static DateOnly AgeReferenceDate(MarketContext context) =>
        new(context.CurrentSeason.Year, 10, 1);

    private static PreferenceContribution TeamFit(Offer offer, MarketContext context)
    {
        var team = context.TeamFor(offer.TeamId);
        if (team is null)
        {
            return new PreferenceContribution(
                PreferenceFactorKind.TeamFit,
                50,
                TeamFitBand,
                TeamFitCode,
                "This offer's team is not in the league, so nothing can be said about the fit.");
        }

        var roster = context.RosterOf(team);
        var atPosition = roster.Count(player => player.Position == context.Player.Position);

        // Depth first: a player wants minutes, and every body already at their position is somebody
        // in front of them. Three deep and the fit is close to worthless whatever the squad is like.
        var depthScore = PreferenceContribution.Clamp(100 - (30 * atPosition));

        var quality = roster.Count == 0
            ? 50
            : PreferenceContribution.Clamp((int)roster.Average(player => player.Rating.Overall));

        // Two parts of one factor rather than two factors: both answer "is this a good place for me
        // to play", and splitting them would let a deep bench outvote the whole of a contract's term.
        var score = PreferenceContribution.Clamp(((depthScore * 2) + quality) / 3);

        var depthSentence = atPosition == 0
            ? $"{team.Name} rosters nobody at {context.Player.Position} — the spot is open."
            : $"{team.Name} already rosters {atPosition} player(s) at {context.Player.Position}.";

        return new PreferenceContribution(
            PreferenceFactorKind.TeamFit,
            score,
            TeamFitBand,
            TeamFitCode,
            roster.Count == 0
                ? $"{depthSentence} The squad is empty, so there is no standard to judge it by."
                : $"{depthSentence} The squad averages {quality} overall.");
    }

    private static PreferenceContribution MarketDemand(Offer offer, IReadOnlyList<Offer> liveOffers)
    {
        var offered = offer.FirstSeasonCompensation.SmallestUnits;
        var rivals = liveOffers.Count(candidate => candidate.Id != offer.Id);
        var better = liveOffers.Count(candidate =>
            candidate.Id != offer.Id && candidate.FirstSeasonCompensation.SmallestUnits > offered);

        var score = liveOffers.Count == 0
            ? 100
            : PreferenceContribution.Clamp(100 - (100 * better / liveOffers.Count));

        var sentence = rivals == 0
            ? "No other team has an offer on the table for this player."
            : better == 0
                ? $"{rivals} other team(s) are chasing this player and none of them is paying more."
                : $"{rivals} other team(s) are chasing this player, {better} of them paying more.";

        return new PreferenceContribution(
            PreferenceFactorKind.MarketDemand,
            score,
            MarketDemandBand,
            MarketDemandCode,
            sentence);
    }

    private static (bool Meets, string Code, string Explanation) Reservation(Offer offer, Money? ask, Player player)
    {
        if (ask is null)
        {
            return (
                true,
                NoAskingPriceCode,
                $"This league configures no salary range, so {player.FullName} has no asking price and will consider any offer that pays something.");
        }

        var reservation = ask.SmallestUnits * ReservationPercentOfAsk / 100;
        var offered = offer.FirstSeasonCompensation.SmallestUnits;

        return offered >= reservation
            ? (
                true,
                ReservationMetCode,
                $"At {offered} this clears the {reservation} {player.FullName} will go down to, against an asking price of {ask.SmallestUnits}.")
            : (
                false,
                ReservationUnmetCode,
                $"At {offered} this is below the {reservation} {player.FullName} will go down to, against an asking price of {ask.SmallestUnits}. They would rather wait.");
    }
}
