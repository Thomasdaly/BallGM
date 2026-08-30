using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Domain.Common;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// The market path end to end: a ruleset file on disk, several teams bidding for one free agent
/// through the Application port, the market resolving at a point, and the read models showing the
/// outcome without anything being reloaded.
/// <para>
/// The fixture's three kinds of team are the point here as much as they are for signings — one with
/// real room, one with only its allowance, and one past the apron — because a market where everyone
/// can afford everyone has no rules in it. The uncapped conformance league appears too, for the
/// opposite reason: it is the league where none of those rules exist.
/// </para>
/// </summary>
public sealed class FixtureFreeAgencyMarketTests
{
    /// <summary>Payroll 120.7m against a 141m soft cap, and roster spots still to fill.</summary>
    private const string TeamWithRoom = "Old Foundry Bellringers";

    /// <summary>Payroll 168m: over the soft cap, under the first apron, so the allowance is live.</summary>
    private const string TeamWithOnlyItsAllowance = "Saltpan City Prospectors";

    /// <summary>Payroll 198m: above the first apron, so this league withdraws the allowance.</summary>
    private const string TeamPastTheApron = "Harbourline Tidewatch";

    /// <summary>
    /// A journeyman rather than the best player available, deliberately. The star's asking price is
    /// the league maximum and nobody in this fixture has the room for it — which is a true thing
    /// about this league, and not the thing most of these tests are about.
    /// </summary>
    private const string AffordableFreeAgent = "Casimir Vandeleur";

    /// <summary>
    /// The uncapped conformance league carries a roster maximum of fourteen and nothing else, so the
    /// only rule left for a market there to run into is roster space. These two have some.
    /// </summary>
    private const string TeamWithSpace = "Northreach Aurora";

    [Fact]
    public void TheBoardColumnsTheMarketByPositionAgainstTheTeamsOwnDepth()
    {
        var session = NewSession(out var overview);
        var team = TeamNamed(overview, TeamWithRoom);

        var board = session.FreeAgencyBoard(team.TeamId, day: 0);
        Assert.True(board.IsSuccess, Messages(board.Errors));

        // One column per position, always — a position with nobody available is information too.
        Assert.Equal(["PG", "SG", "SF", "PF", "C"], board.Value.Columns.Select(column => column.Position));

        foreach (var column in board.Value.Columns)
        {
            Assert.Equal(column.OwnDepth, column.OwnPlayers.Count);
            Assert.Equal(team.Roster.Count(spot => spot.Position == column.Position), column.OwnDepth);

            // Own players run best first, so the column reads as a depth chart rather than a set.
            Assert.Equal(
                column.OwnPlayers.Select(player => player.Overall).OrderByDescending(overall => overall),
                column.OwnPlayers.Select(player => player.Overall));
        }

        var available = board.Value.Columns.SelectMany(column => column.BestAvailable).ToList();
        Assert.Equal(overview.FreeAgents.Players.Count, available.Count);
        Assert.All(available, candidate => Assert.Equal("None", candidate.NegotiationState));
    }

    [Fact]
    public void TheBoardQuotesAnAskingPriceInsideWhatTheLeaguePermitsForThatPlayer()
    {
        var session = NewSession(out var overview);
        var board = session.FreeAgencyBoard(TeamNamed(overview, TeamWithRoom).TeamId, day: 0).Value;

        var candidates = board.Columns.SelectMany(column => column.BestAvailable).ToList();

        Assert.All(candidates, candidate =>
        {
            Assert.NotNull(candidate.AskingPrice);

            // The ask sits inside what this league permits for that player's service. Outside it, the
            // board would be quoting a figure no legal offer could ever meet.
            Assert.InRange(candidate.AskingPrice!.Value, candidate.MinimumSalary!.Value, candidate.MaximumSalary!.Value);
        });
    }

    [Fact]
    public void CompetingOffersAreResolvedTogetherAndEachLoserIsToldWhichKindOfLoserItIs()
    {
        var session = NewSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);

        var withRoom = TeamNamed(overview, TeamWithRoom);
        var pastTheApron = TeamNamed(overview, TeamPastTheApron);
        var withAllowance = TeamNamed(overview, TeamWithOnlyItsAllowance);

        // Three offers, three different fates: one signs, one is not a legal signing at all, and one
        // is perfectly legal and simply not enough money.
        session.PlaceOffer(Offer(pastTheApron, target, 17_000_000, 3), day: 0);
        session.PlaceOffer(Offer(withAllowance, target, 12_000_000, 3), day: 0);
        session.PlaceOffer(Offer(withRoom, target, 16_000_000, 3), day: 0);

        var assessment = session.AssessMarket(target.PlayerId, day: 0);
        Assert.True(assessment.IsSuccess, Messages(assessment.Errors));

        Assert.True(assessment.Value.WouldSign);
        Assert.Equal(withRoom.TeamId, assessment.Value.WinningTeamId);
        Assert.Equal("ResolutionPoint", assessment.Value.ResolutionMode);
        Assert.Equal(3, assessment.Value.Standings.Count);

        var illegal = assessment.Value.Standings.Single(standing => standing.TeamName == TeamPastTheApron);
        Assert.False(illegal.IsSignable);
        Assert.Equal(0, illegal.Rank);
        Assert.Contains(illegal.Exclusions, finding => finding.RuleCode == "market.offer_no_longer_signable");

        var tooSmall = assessment.Value.Standings.Single(standing => standing.TeamName == TeamWithOnlyItsAllowance);
        Assert.True(tooSmall.IsSignable);
        Assert.False(tooSmall.MeetsAskingPrice);
        Assert.Contains(tooSmall.Exclusions, finding => finding.RuleCode == "market.offer_below_asking_price");

        // Every offer carries the whole factor breakdown, including the ones that lost: a GM who was
        // outbid has to be able to read which factor beat them rather than being handed a number.
        Assert.All(assessment.Value.Standings, standing =>
            Assert.Equal(
                ["Money", "Term", "TeamFit", "MarketDemand"],
                standing.Factors.Select(factor => factor.Factor)));
    }

    [Fact]
    public void ResolvingTheMarketSignsTheWinnerAndEveryScreenShowsIt()
    {
        var session = NewSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);
        var team = TeamNamed(overview, TeamWithRoom);

        session.PlaceOffer(Offer(team, target, 16_000_000, 3), day: 0);

        var resolution = session.ResolveMarket(target.PlayerId, day: 0);
        Assert.True(resolution.IsSuccess, Messages(resolution.Errors));
        Assert.True(resolution.Value.Signed);
        Assert.Equal("Cap room", resolution.Value.RouteName);
        Assert.Equal("Signed", resolution.Value.Negotiation.State);
        Assert.Equal(1, resolution.Value.LedgerEntryCount);

        var after = resolution.Value.Overview;
        Assert.Contains(TeamNamed(after, TeamWithRoom).Roster, spot => spot.PlayerId == target.PlayerId);
        Assert.DoesNotContain(after.FreeAgents.Players, player => player.PlayerId == target.PlayerId);

        // A resolved market is over: the same call again finds nothing left to resolve.
        var again = session.ResolveMarket(target.PlayerId, day: 0);
        Assert.True(again.IsFailure);
        Assert.Contains(again.Errors, error => error.Code == "market.negotiation_not_open");
    }

    [Fact]
    public void AnOfferThatStoodTooLongIsOffTheTableAndTheMarketSaysSo()
    {
        var session = NewSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);
        var team = TeamNamed(overview, TeamWithRoom);

        // This league expires offers after three days, so a market resolved on day three finds an
        // offer that would have signed him on day two gone.
        session.PlaceOffer(Offer(team, target, 16_000_000, 3), day: 0);

        var onDayTwo = session.AssessMarket(target.PlayerId, day: 2).Value;
        Assert.True(onDayTwo.WouldSign);
        Assert.Equal(team.TeamId, onDayTwo.WinningTeamId);

        var onDayThree = session.AssessMarket(target.PlayerId, day: 3).Value;

        Assert.False(onDayThree.WouldSign);
        Assert.Empty(onDayThree.Standings);
        Assert.Contains(onDayThree.Warnings, warning => warning.RuleCode == "market.offer_expired");

        var resolution = session.ResolveMarket(target.PlayerId, day: 3);
        Assert.True(resolution.IsSuccess, Messages(resolution.Errors));
        Assert.False(resolution.Value.Signed);
        Assert.Equal("Closed", resolution.Value.Negotiation.State);

        // The expiry is recorded rather than merely observed, so the history says why he went unsigned.
        Assert.Contains(resolution.Value.Negotiation.History, entry => entry.Kind == "OfferExpired");
    }

    [Fact]
    public void ACounterofferIsRecordedWithoutAcceptingAnythingAndShowsOnTheBoard()
    {
        var session = NewSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);
        var team = TeamNamed(overview, TeamWithRoom);

        var placed = session.PlaceOffer(Offer(team, target, 12_000_000, 3), day: 0);
        Assert.True(placed.IsSuccess, Messages(placed.Errors));

        var board = session.FreeAgencyBoard(team.TeamId, day: 0).Value;
        var candidate = board.Columns.SelectMany(column => column.BestAvailable).Single(line => line.PlayerId == target.PlayerId);
        Assert.True(candidate.HasOurOffer);
        Assert.NotNull(candidate.OurOfferId);

        var countered = session.Counteroffer(
            new CounterofferRequest(target.PlayerId, team.TeamId, candidate.OurOfferId!, Seasons(17_000_000, 4)),
            day: 1);

        Assert.True(countered.IsSuccess, Messages(countered.Errors));

        // A counter is a new offer in the history authored by the player, not a state transition: the
        // market is still open, nothing is accepted, and the team answers it by offering again.
        Assert.Equal("Open", countered.Value.State);
        Assert.Equal(1, countered.Value.CounterofferCount);
        Assert.Equal(1, countered.Value.LiveOfferCount);

        var afterCounter = session.FreeAgencyBoard(team.TeamId, day: 1).Value;
        var counteredLine = afterCounter.Columns.SelectMany(column => column.BestAvailable).Single(line => line.PlayerId == target.PlayerId);

        Assert.Equal(17_000_000, counteredLine.CounterofferFirstSeasonCompensation);
        Assert.Equal(4, counteredLine.CounterofferSeasonCount);
    }

    [Fact]
    public void AnUncappedLeagueRefusesNobodyForBeingTooSmallAndWeighsOffersAgainstEachOther()
    {
        var session = UncappedSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);

        var first = TeamNamed(overview, TeamWithRoom);
        var second = TeamNamed(overview, TeamWithSpace);

        // Two payrolls that would be over lines in the other league, and figures that would be
        // laughed at there. Neither means anything here, which is the point of this league: the only
        // rule it configures is a roster maximum, so that is the only rule a signing can break.
        session.PlaceOffer(Offer(first, target, 4_000_000, 3), day: 0);
        session.PlaceOffer(Offer(second, target, 9_000_000, 3), day: 0);

        var assessment = session.AssessMarket(target.PlayerId, day: 0);
        Assert.True(assessment.IsSuccess, Messages(assessment.Errors));

        Assert.True(assessment.Value.WouldSign);
        Assert.Equal(second.TeamId, assessment.Value.WinningTeamId);

        // Both are signable and both clear an asking price that does not exist, and the two rules the
        // league does not have are stated rather than silently skipped.
        Assert.All(assessment.Value.Standings, standing =>
        {
            Assert.True(standing.IsSignable);
            Assert.True(standing.MeetsAskingPrice);
        });

        Assert.Contains(assessment.Value.Notes, note => note.RuleCode == "market.no_compensation_range_configured");
        Assert.Contains(assessment.Value.Notes, note => note.RuleCode == "market.no_offer_expiry_configured");
    }

    [Fact]
    public void TheSameLeagueAndTheSameSeedResolveTheSameMarketTheSameWay()
    {
        // Determinism is the second product pillar, and a market that draws where it is indifferent
        // is exactly where it would be lost. Same league, same offers, same seed — same answer, every
        // run, whether or not a draw was needed to get there.
        var winners = Enumerable.Range(0, 8).Select(_ => ResolveTwinOffers(seed: 4242)).Distinct().ToList();

        Assert.Single(winners);
    }

    [Fact]
    public void AnInFlightMarketSurvivesBeingSavedAndReloadedIntoAFreshSession()
    {
        var session = NewSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);

        session.PlaceOffer(Offer(TeamNamed(overview, TeamWithOnlyItsAllowance), target, 12_000_000, 3), day: 0);
        session.PlaceOffer(Offer(TeamNamed(overview, TeamWithRoom), target, 16_000_000, 3), day: 0);

        var before = session.AssessMarket(target.PlayerId, day: 1).Value;

        var serializer = new NegotiationSerializer();
        var saved = serializer.Serialize(session.NegotiationFor(target.PlayerId)!);

        var restored = serializer.Deserialize(saved);
        Assert.True(restored.IsSuccess, Messages(restored.Errors));
        Assert.True(session.AdoptNegotiation(restored.Value).IsSuccess);

        var after = session.AssessMarket(target.PlayerId, day: 1).Value;

        // The whole market comes back, not merely the fact that one was open: same offers, same
        // standings, same winner — and the reloaded negotiation is the one the session now resolves.
        Assert.Equal(before.WinningTeamId, after.WinningTeamId);
        Assert.Equal(
            before.Standings.Select(standing => (standing.TeamId, standing.Rank, standing.FirstSeasonCompensation)),
            after.Standings.Select(standing => (standing.TeamId, standing.Rank, standing.FirstSeasonCompensation)));

        var resolved = session.ResolveMarket(target.PlayerId, day: 1);
        Assert.True(resolved.IsSuccess, Messages(resolved.Errors));
        Assert.True(resolved.Value.Signed);
    }

    [Fact]
    public void ASavedMarketIsRefusedByALeagueThatHasNeverHeardOfItsPlayer()
    {
        // This fixture mints identifiers on every load, so a negotiation saved from one run names
        // people the next run has never heard of. That is a property of a fixture rather than of the
        // format — but it is exactly the shape of a save opened against the wrong league, and it has
        // to be a message rather than a market quietly resolving over nobody.
        var session = NewSession(out var overview);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);
        session.PlaceOffer(Offer(TeamNamed(overview, TeamWithRoom), target, 16_000_000, 3), day: 0);

        var serializer = new NegotiationSerializer();
        var saved = serializer.Deserialize(serializer.Serialize(session.NegotiationFor(target.PlayerId)!)).Value;

        var elsewhere = NewSession(out _);
        var result = elsewhere.AdoptNegotiation(saved);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation_request.negotiation_player_not_in_league");
    }

    [Fact]
    public void ANegotiationCannotBeOpenedOverSomebodyAlreadyUnderContract()
    {
        var session = NewSession(out var overview);
        var rostered = overview.Teams.SelectMany(team => team.Roster).First();

        var result = session.OpenNegotiation(rostered.PlayerId, day: 0);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation_request.player_is_not_a_free_agent");
    }

    [Fact]
    public void AMarketCannotBeAssessedForAPlayerNobodyHasOpenedOneOver()
    {
        var session = NewSession(out var overview);

        var result = session.AssessMarket(FreeAgentNamed(overview, AffordableFreeAgent).PlayerId, day: 0);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "negotiation_request.no_negotiation_for_player");
    }

    /// <summary>
    /// Two teams making the same offer to the same player in a league with no rules to separate them.
    /// Whatever decides it — a factor or the seeded draw — has to decide it the same way every run.
    /// </summary>
    private static string ResolveTwinOffers(int seed)
    {
        var session = UncappedSession(out var overview, seed);
        var target = FreeAgentNamed(overview, AffordableFreeAgent);

        session.PlaceOffer(Offer(TeamNamed(overview, TeamWithRoom), target, 9_000_000, 3), day: 0);
        session.PlaceOffer(Offer(TeamNamed(overview, TeamWithSpace), target, 9_000_000, 3), day: 0);

        var assessment = session.AssessMarket(target.PlayerId, day: 0);
        Assert.True(assessment.IsSuccess, Messages(assessment.Errors));

        // The name rather than the identifier: this fixture mints identifiers on every load, so two
        // runs of the same league produce two sets of them. What has to be reproducible is which
        // team won, not what the fixture happened to call it this time.
        return assessment.Value.WinningTeamName ?? "nobody";
    }

    private static string Messages(IReadOnlyList<DomainError> errors) =>
        string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}"));

    private static OfferRequest Offer(TeamSummary team, FreeAgentLine player, long firstSeason, int seasons) =>
        new(team.TeamId, player.PlayerId, Seasons(firstSeason, seasons));

    private static IReadOnlyList<OfferSeasonRequest> Seasons(long firstSeason, int seasons) =>
        Enumerable.Range(0, seasons)
            .Select(index => new OfferSeasonRequest(2031 + index, firstSeason, firstSeason))
            .ToList();

    private static FreeAgentLine FreeAgentNamed(LeagueOverview overview, string fullName) =>
        overview.FreeAgents.Players.Single(player => player.FullName == fullName);

    private static TeamSummary TeamNamed(LeagueOverview overview, string teamName) =>
        overview.Teams.Single(team => team.TeamName == teamName);

    private static LeagueSession NewSession(out LeagueOverview overview, int seed = LeagueSession.DefaultMarketSeed) =>
        Session(new FixtureLeagueDataSource(), out overview, seed);

    private static LeagueSession UncappedSession(out LeagueOverview overview, int seed = LeagueSession.DefaultMarketSeed)
    {
        var rulesetPath = Path.Combine(
            AppContext.BaseDirectory, "data", "rulesets", "conformance", "uncapped-open-league.json");

        return Session(new FixtureLeagueDataSource(rulesetPath), out overview, seed);
    }

    private static LeagueSession Session(FixtureLeagueDataSource dataSource, out LeagueOverview overview, int seed)
    {
        var session = new LeagueSession(
            dataSource,
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            seed);

        var result = session.Load();
        Assert.True(result.IsSuccess, Messages(result.Errors));

        overview = result.Value;
        return session;
    }
}
