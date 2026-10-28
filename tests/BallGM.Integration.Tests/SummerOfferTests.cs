using BallGM.Application.Leagues;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// The person playing takes part in the summer market: offers made before the season ends stand in
/// every round, and each player weighs them against the AI's bids by the same rule.
/// </summary>
public sealed class SummerOfferTests
{
    [Fact]
    public void AStandingOfferAtAFairPrice_SignsTheFreeAgent_AndTheSummerReportsIt()
    {
        var (session, managed) = SeasonPlayed();
        var target = ExpiringElsewhere(session, managed).OrderBy(line => line.Overall).First();
        var ask = session.ExtensionTerms(target.PlayerId).Value.AskingPrice!.Value;

        Assert.True(session.PlaceSummerOffer(target.PlayerId, ask * 2, 1).IsSuccess);
        Assert.Single(session.SummerOffers());

        var summary = session.ConcludeSeason().Value;

        var roster = session.Overview().Value.Teams.Single(team => team.TeamId == managed).Roster;
        Assert.Contains(roster, spot => spot.PlayerId == target.PlayerId);
        Assert.Contains(summary.SummerOfferOutcomes, line => line.Contains(target.FullName, StringComparison.Ordinal) && line.Contains("accepted", StringComparison.Ordinal));
        Assert.Empty(session.SummerOffers());
    }

    [Fact]
    public void AnOfferBelowWhatThePlayerWillTake_DoesNotSignHim_AndTheReasonIsReported()
    {
        var (session, managed) = SeasonPlayed();
        var target = ExpiringElsewhere(session, managed).OrderByDescending(line => line.Overall).First();
        var ask = session.ExtensionTerms(target.PlayerId).Value.AskingPrice!.Value;

        Assert.True(session.PlaceSummerOffer(target.PlayerId, ask / 4, 3).IsSuccess);
        var summary = session.ConcludeSeason().Value;

        var roster = session.Overview().Value.Teams.Single(team => team.TeamId == managed).Roster;
        Assert.DoesNotContain(roster, spot => spot.PlayerId == target.PlayerId);
        var outcome = Assert.Single(summary.SummerOfferOutcomes);
        Assert.Contains(target.FullName, outcome, StringComparison.Ordinal);
        Assert.DoesNotContain("accepted", outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void ASummerOfferNeedsAManagedTeam_AFreeAgentThisSummer_AndSensibleTerms()
    {
        var session = NewSession();
        var overview = session.Overview().Value;
        var anyone = overview.Teams[1].Roster[0].PlayerId;
        Assert.Equal("summer_offer.no_managed_team", session.PlaceSummerOffer(anyone, 1_000_000, 1).Errors[0].Code);

        var managed = overview.Teams[0].TeamId;
        Assert.True(session.SetManagedTeam(managed).IsSuccess);
        var underContract = overview.Teams.Skip(1).SelectMany(team => team.Roster).First(spot => spot.ContractSeasonsRemaining >= 2).PlayerId;
        Assert.Equal("summer_offer.not_a_free_agent_this_summer", session.PlaceSummerOffer(underContract, 1_000_000, 1).Errors[0].Code);

        var expiring = ExpiringElsewhere(session, managed).First().PlayerId;
        Assert.Equal("summer_offer.invalid_terms", session.PlaceSummerOffer(expiring, 1_000_000, 0).Errors[0].Code);
        Assert.Equal("summer_offer.invalid_terms", session.PlaceSummerOffer(expiring, 0, 2).Errors[0].Code);
        Assert.True(session.PlaceSummerOffer(expiring, 1_000_000, 2).IsSuccess);
        Assert.True(session.WithdrawSummerOffer(expiring).IsSuccess);
        Assert.Empty(session.SummerOffers());
    }

    private static IEnumerable<BallGM.Application.Players.UpcomingFreeAgentLine> ExpiringElsewhere(LeagueSession session, string managed)
    {
        var nextSummer = session.UpcomingFreeAgents().Min(freeAgentClass => freeAgentClass.Year);
        return session.UpcomingFreeAgents()
            .Where(freeAgentClass => freeAgentClass.Year == nextSummer)
            .SelectMany(freeAgentClass => freeAgentClass.Players)
            .Where(line => line.TeamId != managed && line.FinalSeasonOption is null);
    }

    private static (LeagueSession Session, string Managed) SeasonPlayed()
    {
        var session = NewSession();
        var managed = session.Overview().Value.Teams[0].TeamId;
        Assert.True(session.SetManagedTeam(managed).IsSuccess);
        Assert.True(session.StartSeason(700).IsSuccess);
        Assert.True(session.AdvanceToEndOfSeason().IsSuccess);
        return (session, managed);
    }

    private static LeagueSession NewSession()
    {
        var session = new LeagueSession(
            new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 5),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));
        Assert.True(session.Load().IsSuccess);
        return session;
    }
}
