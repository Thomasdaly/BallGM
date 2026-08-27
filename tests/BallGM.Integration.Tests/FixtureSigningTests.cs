using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// The signing path end to end: a ruleset file on disk, free agents loaded like anyone else, the
/// rules layer judging an offer through the Application port, and the read model showing the result
/// without anything being reloaded. The fixture's three kinds of team are the point — one with real
/// room, one with only its allowance, and one past the apron with nothing to offer but a pitch.
/// </summary>
public sealed class FixtureSigningTests
{
    /// <summary>Payroll 120.7m against a 141m soft cap, and two roster spots still to fill.</summary>
    private const string TeamWithRoom = "Old Foundry Bellringers";

    /// <summary>Payroll 168m: over the soft cap, under the first apron, so the allowance is live.</summary>
    private const string TeamWithOnlyItsAllowance = "Saltpan City Prospectors";

    /// <summary>Payroll 198m: above the first apron, so this league withdraws the allowance.</summary>
    private const string TeamPastTheApron = "Harbourline Tidewatch";

    [Fact]
    public void TheMarketOffersUnsignedPlayersWithTheRangeTheLeaguePermitsForEachOfThem()
    {
        var overview = NewSession(out _).Overview().Value;
        var market = overview.FreeAgents;

        Assert.NotEmpty(market.Players);
        Assert.True(market.LeagueHasCompensationFloor);
        Assert.True(market.LeagueHasCompensationCeiling);
        Assert.Equal(5, market.MaximumContractSeasons);

        // Nobody in the market is on a roster or under contract — that is what makes them available.
        var rostered = overview.Teams.SelectMany(team => team.Roster.Select(spot => spot.PlayerId)).ToHashSet();
        Assert.All(market.Players, player => Assert.DoesNotContain(player.PlayerId, rostered));

        // The permitted range keys off service, so a rookie and a long-serving veteran differ on both
        // ends of it rather than being quoted one league-wide figure.
        var rookie = market.Players.Single(player => player.SeasonsOfService == 0);
        var veteran = market.Players.OrderByDescending(player => player.SeasonsOfService).First();

        Assert.Equal(1_150_000, rookie.MinimumSalary);
        Assert.Equal(141_000_000L * 25 / 100, rookie.MaximumSalary);
        Assert.Equal(3_300_000, veteran.MinimumSalary);
        Assert.Equal(141_000_000L * 35 / 100, veteran.MaximumSalary);
    }

    [Fact]
    public void TheTeamWithRoomSignsTheStarAndEveryScreenShowsIt()
    {
        var session = NewSession(out var overview);
        var team = TeamNamed(overview, TeamWithRoom);
        var star = BestAvailable(overview);

        var request = Offer(team, star, firstSeason: 18_000_000, seasons: 3);

        var assessment = session.AssessOffer(request);
        Assert.True(assessment.IsSuccess, string.Join("; ", assessment.Errors.Select(error => error.Message)));
        Assert.True(assessment.Value.IsLegal, string.Join("; ", assessment.Value.Violations.Select(v => v.Explanation)));
        Assert.Equal("Cap room", assessment.Value.PermittingRouteName);

        var submission = session.SubmitOffer(request);
        Assert.True(submission.IsSuccess, string.Join("; ", submission.Errors.Select(error => error.Message)));

        var after = submission.Value.Overview;
        var signedTeam = TeamNamed(after, TeamWithRoom);

        Assert.Contains(signedTeam.Roster, spot => spot.PlayerId == star.PlayerId);
        Assert.DoesNotContain(after.FreeAgents.Players, player => player.PlayerId == star.PlayerId);
        Assert.Equal(team.RosterCount + 1, signedTeam.RosterCount);

        // One roster spot filled, so one hold released: the payroll rises by the salary less the hold.
        Assert.Equal(1_150_000, signedTeam.CapSheet.RosterHolds);
        Assert.Equal(team.CapSheet.TotalPayroll + 18_000_000 - 1_150_000, signedTeam.CapSheet.TotalPayroll);

        Assert.Contains(signedTeam.CapSheet.Transactions, line => line.Kind == "Contract signed" && line.Amount == 18_000_000);
    }

    /// <summary>
    /// The team over the cap can still bid, but only up to a fixed sum — and the screen says which
    /// sum, so a GM can plan against it rather than discovering it by being refused.
    /// </summary>
    [Fact]
    public void TheTeamOverTheCapIsHeldToItsAllowanceAndToldWhatIsLeftOfIt()
    {
        var session = NewSession(out var overview);
        var team = TeamNamed(overview, TeamWithOnlyItsAllowance);
        var star = BestAvailable(overview);

        var tooMuch = session.AssessOffer(Offer(team, star, firstSeason: 18_000_000, seasons: 3)).Value;

        Assert.False(tooMuch.IsLegal);
        Assert.Null(tooMuch.PermittingRouteName);
        Assert.Equal(0, tooMuch.CapRoomBefore);

        var allowance = tooMuch.Routes.Single(route => route.RouteName == "Standard over-cap allowance");
        Assert.True(allowance.Applicable);
        Assert.False(allowance.Permits);
        Assert.Equal(12_800_000, allowance.MaximumFirstSeasonCompensation);

        var withinIt = session.AssessOffer(Offer(team, star, firstSeason: 12_000_000, seasons: 3)).Value;
        Assert.True(withinIt.IsLegal, string.Join("; ", withinIt.Violations.Select(v => v.Explanation)));
        Assert.Equal("Standard over-cap allowance", withinIt.PermittingRouteName);
    }

    /// <summary>
    /// Above the apron the allowance is gone and only the league minimum is left. That is the whole
    /// texture of the market: a team that has to sell something other than money.
    /// </summary>
    [Fact]
    public void TheTeamPastTheApronHasNothingButTheLeagueMinimum()
    {
        var session = NewSession(out var overview);
        var team = TeamNamed(overview, TeamPastTheApron);
        var star = BestAvailable(overview);
        var rookie = overview.FreeAgents.Players.Single(player => player.SeasonsOfService == 0);

        var forTheStar = session.AssessOffer(Offer(team, star, firstSeason: 12_000_000, seasons: 3)).Value;

        Assert.False(forTheStar.IsLegal);
        var allowance = forTheStar.Routes.Single(route => route.RouteName == "Standard over-cap allowance");
        Assert.Equal("signing.allowance_unavailable_above_threshold", allowance.RuleCode);
        Assert.Contains("first apron", allowance.Explanation, StringComparison.Ordinal);

        // The roster is full at this team, so even the minimum route is closed — but it is closed for
        // a reason about the roster rather than about the money.
        Assert.Contains(forTheStar.Violations, finding => finding.RuleCode == "signing.roster_full");

        var forTheRookie = session.AssessOffer(Offer(team, rookie, firstSeason: 1_150_000, seasons: 1)).Value;
        var minimumRoute = forTheRookie.Routes.Single(route => route.RouteName == "Minimum salary");
        Assert.True(minimumRoute.Permits);
    }

    /// <summary>
    /// A refused offer changes nothing, and the reason names the rule. Assessing is safe to do as
    /// often as a GM likes, which is what an offer screen does on every keystroke.
    /// </summary>
    [Fact]
    public void AnOfferLongerThanTheLeaguePermitsIsRefusedAndTheLeagueIsUnchanged()
    {
        var session = NewSession(out var overview);
        var team = TeamNamed(overview, TeamWithRoom);
        var star = BestAvailable(overview);

        var result = session.SubmitOffer(Offer(team, star, firstSeason: 10_000_000, seasons: 8));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "offer.term_exceeds_limit");

        var after = session.Overview().Value;
        Assert.Contains(after.FreeAgents.Players, player => player.PlayerId == star.PlayerId);
        Assert.Equal(
            TeamNamed(overview, TeamWithRoom).CapSheet.TotalPayroll,
            TeamNamed(after, TeamWithRoom).CapSheet.TotalPayroll);
    }

    /// <summary>
    /// The allowance is a finite pot, and how much is left is derived from the ledger. Two signings
    /// out of one allowance is the case that proves it.
    /// </summary>
    [Fact]
    public void ASecondAllowanceSigningSeesWhatTheFirstOneSpent()
    {
        var session = NewSession(out var overview);
        var team = TeamNamed(overview, TeamWithOnlyItsAllowance);
        var players = overview.FreeAgents.Players.Where(player => player.SeasonsOfService > 0).Take(2).ToList();

        var first = session.SubmitOffer(Offer(team, players[0], firstSeason: 8_000_000, seasons: 2));
        Assert.True(first.IsSuccess, string.Join("; ", first.Errors.Select(error => error.Message)));
        Assert.Equal("Standard over-cap allowance", first.Value.RouteName);

        var second = session.AssessOffer(Offer(team, players[1], firstSeason: 8_000_000, seasons: 2)).Value;
        var allowance = second.Routes.Single(route => route.RouteName == "Standard over-cap allowance");

        Assert.Equal(12_800_000 - 8_000_000, allowance.MaximumFirstSeasonCompensation);
        Assert.False(allowance.Permits);
    }

    private static OfferRequest Offer(TeamSummary team, FreeAgentLine player, long firstSeason, int seasons) =>
        new(
            team.TeamId,
            player.PlayerId,
            Enumerable.Range(0, seasons)
                .Select(index => new OfferSeasonRequest(2031 + index, firstSeason, firstSeason))
                .ToList());

    private static FreeAgentLine BestAvailable(LeagueOverview overview) => overview.FreeAgents.Players[0];

    private static TeamSummary TeamNamed(LeagueOverview overview, string teamName) =>
        overview.Teams.Single(team => team.TeamName == teamName);

    private static LeagueSession NewSession(out LeagueOverview overview)
    {
        var session = new LeagueSession(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket());

        var result = session.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        overview = result.Value;
        return session;
    }
}
