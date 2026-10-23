using BallGM.Application.Leagues;
using BallGM.Domain.Seasons;
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
/// The season boundary a long save depends on, through the real stack: awards decided by their stated
/// rules and archived (and kept by a save), every player a season older, and careers ending only where
/// the rules let them. See <c>docs/franchise-realism.md</c> → slice F2.
/// </summary>
public sealed class SeasonBoundaryRealismTests
{
    [Fact]
    public void ConcludedSeasonArchivesItsAwards_AndASaveKeepsThem()
    {
        var session = NewSession();
        PlaySeason(session, seed: 2031);

        var conclusion = session.ConcludeSeason();
        Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));
        var awards = conclusion.Value.Awards;
        Assert.Contains(awards, award => award.AwardCode == "most-valuable");
        Assert.Equal(15, awards.Count(award => award.AwardCode == "all-league"));
        Assert.All(awards, award => Assert.False(string.IsNullOrWhiteSpace(award.Explanation)));

        var saved = session.Save();
        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(error => error.Message)));
        var reloaded = NewSession();
        Assert.True(reloaded.LoadSave(saved.Value).IsSuccess);

        var history = Assert.Single(reloaded.SeasonHistory().Value);
        Assert.Equal(awards.Select(award => (award.AwardCode, award.Selection, award.PlayerId)), history.Awards.Select(award => (award.AwardCode, award.Selection, award.PlayerId)));
    }

    [Fact]
    public void TheBenchAwardNeverGoesToAPlayerWhoStartedMostOfHisGames()
    {
        // The competitor's failure this guards against: a team's best player, who starts every night,
        // named the best player off the bench. Eligibility is a rule, so he never is — however well he plays.
        for (var seed = 2031; seed < 2034; seed++)
        {
            var session = NewSession();
            PlaySeason(session, seed);
            var stats = session.LeagueStats(SeasonPhase.RegularSeason).Players.ToDictionary(line => line.PlayerId);
            var bestScorer = stats.Values.Where(line => line.Stats.Games >= 40).MaxBy(line => (double)line.Stats.Points / line.Stats.Games)!;

            var conclusion = session.ConcludeSeason().Value;
            var bench = conclusion.Awards.SingleOrDefault(award => award.AwardCode == "sixth-man");
            if (bench is null)
            {
                continue;
            }

            var line = stats[bench.PlayerId].Stats;
            Assert.True(line.Games - line.Starts > line.Starts, $"{bench.PlayerName} started {line.Starts} of {line.Games} games and still won the bench award.");
            Assert.NotEqual(bestScorer.PlayerId, bench.PlayerId);
        }
    }

    [Fact]
    public void EveryoneAgesAtTheBoundary_AndNobodyUnderContractRetires()
    {
        var session = NewSession();
        PlaySeason(session, seed: 2031);
        var before = session.Overview().Value.Teams
            .SelectMany(team => team.Roster)
            .ToDictionary(player => player.PlayerId, player => player.Overall);

        var conclusion = session.ConcludeSeason().Value;

        var after = session.Overview().Value.Teams
            .SelectMany(team => team.Roster)
            .ToDictionary(player => player.PlayerId, player => player.Overall);
        var moved = before.Keys.Count(id => after.TryGetValue(id, out var now) && now != before[id]);
        Assert.True(moved * 2 > before.Count, $"Only {moved} of {before.Count} rostered players' ratings moved over a season boundary.");
        Assert.Equal(before.Count(entry => after.ContainsKey(entry.Key)), before.Keys.Count(after.ContainsKey));
        Assert.All(conclusion.PlayersRetired, name => Assert.DoesNotContain(
            session.Overview().Value.Teams.SelectMany(team => team.Roster),
            player => player.FullName == name));
    }

    private static void PlaySeason(LeagueSession session, int seed)
    {
        Assert.True(session.StartSeason(seed).IsSuccess);
        Assert.True(session.AdvanceToEndOfSeason().IsSuccess);
    }

    private static LeagueSession NewSession()
    {
        var session = new LeagueSession(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));

        var result = session.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return session;
    }
}
