using BallGM.Domain.AI;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Negotiations;

namespace BallGM.Rules.Tests.AI;

/// <summary>
/// Injured players are tradeable and priced by their injury: a veteran out long on a big deal is almost a
/// salary dump; a former All-Star back before the postseason is a contender's cheap last-chance gamble; a
/// free agent still hurt asks less.
/// </summary>
public sealed class InjuryValuationTests
{
    private static readonly DateOnly Summer = new(2031, 7, 1);

    [Fact]
    public void AVeteranOutLongOnABigDealIsDumped_EvenThoughHeWasOneOfTheTeamsBest()
    {
        // 130M against a 120M tax; every player rates the same, so the first five are its "best" and
        // protected. The 30M first player is 31 and out 200 days.
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 8)
            .WithTeam("TAX", 30_000_000, 30_000_000, 20_000_000, 15_000_000, 10_000_000, 25_000_000)
            .WithContractsSignedEarlier("TAX", 1)
            .WithTeam("REB", 5_000_000, 5_000_000);
        var directions = new Dictionary<TeamId, CompetitiveDirection>
        {
            [league.TeamOf("TAX").Id] = CompetitiveDirection.Contending,
            [league.TeamOf("REB").Id] = CompetitiveDirection.Rebuilding,
        };
        var rules = SigningTestLeague.StandardRules(league.CapThresholds);
        var hurt = new InjuryReport(new Dictionary<PlayerId, int> { [league.PlayerOf("TAX", 0)] = 200 }, 150, new HashSet<PlayerId>());

        var healthy = Assert.Single(SalaryDumpModel.Find(league.Context(), directions, rules, scale: null));
        var injured = Assert.Single(SalaryDumpModel.Find(league.Context(), directions, rules, scale: null, injuries: hurt));

        Assert.Equal(league.PlayerOf("TAX", 5), healthy.PlayerId);
        Assert.Equal(league.PlayerOf("TAX", 0), injured.PlayerId);
        Assert.Contains("dead weight", injured.Rationale.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInjuredFormerStarBackBeforeThePostseasonIsAContendersCheapGamble_OneOutAllSeasonIsNotBought()
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 10)
            .WithTeam("SELL", 20_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000)
            .WithTeam("BUY", 30_000_000, 25_000_000, 20_000_000, 15_000_000, 10_000_000, 8_000_000, 6_000_000, 4_000_000)
            .WithPlayer("SELL", 0, 85, new DateOnly(2000, 1, 1))
            .WithContractsSignedEarlier("SELL", 2)
            .WithContractsSignedEarlier("BUY", 2);
        for (var index = 1; index < 6; index++)
        {
            league.WithPlayer("SELL", index, 60, new DateOnly(2008, 1, 1));
        }

        for (var index = 0; index < 8; index++)
        {
            league.WithPlayer("BUY", index, index < 5 ? 80 : 60, new DateOnly(2003, 1, 1));
        }

        var directions = new Dictionary<TeamId, CompetitiveDirection>
        {
            [league.TeamOf("SELL").Id] = CompetitiveDirection.Rebuilding,
            [league.TeamOf("BUY").Id] = CompetitiveDirection.Contending,
        };
        var records = new Dictionary<TeamId, TeamRecord>
        {
            [league.TeamOf("SELL").Id] = new(20, 62),
            [league.TeamOf("BUY").Id] = new(60, 22),
        };
        var star = league.PlayerOf("SELL", 0);
        var stars = new HashSet<PlayerId> { star };

        // 31, the league's best, healthy: a franchise player under 32 is kept.
        Assert.Empty(VeteranSaleModel.Find(league.Context(), directions, records, Summer));

        // Hurt for 60 of the 150 days left: a last-chance gamble, a tier cheaper than his rank's two firsts.
        var gamble = Assert.Single(VeteranSaleModel.Find(league.Context(), directions, records, Summer,
            injuries: new InjuryReport(new Dictionary<PlayerId, int> { [star] = 60 }, 150, stars)));
        Assert.Equal(star, gamble.VeteranId);
        Assert.Single(gamble.Picks);
        Assert.Contains("last-chance gamble", gamble.Rationale.Explanation, StringComparison.Ordinal);

        // Out past the season's end: nobody buys him for this one.
        Assert.Empty(VeteranSaleModel.Find(league.Context(), directions, records, Summer,
            injuries: new InjuryReport(new Dictionary<PlayerId, int> { [star] = 200 }, 150, stars)));
    }

    [Fact]
    public void AnInjuredFreeAgentAsksLess_TheLongerHeIsOut()
    {
        var rules = SigningTestLeague.StandardRules();
        Player Agent(int? daysOut) => Player.Create(new PlayerId("FA"), "Free Agent", Position.SmallForward, new PlayerRating(80), new DateOnly(2000, 1, 1), 6,
            daysOut is int days ? new Injury("torn knee ligament", days) : null).Value;

        var healthy = PreferenceModel.AskingPriceFor(Agent(null), rules, SigningTestLeague.StandardThresholds)!.SmallestUnits;
        var month = PreferenceModel.AskingPriceFor(Agent(30), rules, SigningTestLeague.StandardThresholds)!.SmallestUnits;
        var season = PreferenceModel.AskingPriceFor(Agent(300), rules, SigningTestLeague.StandardThresholds)!.SmallestUnits;

        Assert.True(healthy > month && month > season);
        Assert.InRange(season, (healthy / 2) - 10_000, (healthy / 2) + 10_000);
    }
}
