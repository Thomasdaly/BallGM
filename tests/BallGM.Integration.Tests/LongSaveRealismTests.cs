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
/// The long save, audited (<c>docs/franchise-realism.md</c> → F7): twelve chained seasons of the
/// thirty-team calibration league, unattended, checked every season for the failures a long save in the
/// competitor's franchise mode is known for — flat scoring, a starter winning the bench award, stars
/// changing teams every summer, buyouts moving good players, and a league that ages or inflates into
/// something else. Seeded, so a regression fails here rather than twelve seasons into somebody's save.
/// </summary>
public sealed class LongSaveRealismTests
{
    private const int Seasons = 12;

    [Fact]
    public void TwelveSeasonsLaterTheLeagueStillLooksLikeALeague()
    {
        var session = NewSession();
        var leaders = new List<double>();
        var startMedian = Median(session);

        for (var season = 1; season <= Seasons; season++)
        {
            Assert.True(session.StartSeason(100 + season).IsSuccess);
            Assert.True(session.AdvanceToEndOfSeason().IsSuccess, $"Season {season} could not be played out.");

            var stats = session.LeagueStats(SeasonPhase.RegularSeason).Players.Where(line => line.Stats.Games >= 40).ToDictionary(line => line.PlayerId);
            leaders.Add(stats.Values.Max(line => (double)line.Stats.Points / line.Stats.Games));
            var stars = session.Overview().Value.Teams
                .SelectMany(team => team.Roster.Select(player => (player.PlayerId, player.Overall, team.TeamId)))
                .OrderByDescending(entry => entry.Overall)
                .Take(30)
                .ToList();

            var conclusion = session.ConcludeSeason();
            Assert.True(conclusion.IsSuccess, $"Season {season}: {string.Join("; ", conclusion.Errors.Select(error => error.Message))}");
            var summary = conclusion.Value;

            if (summary.Awards.FirstOrDefault(award => award.AwardCode == "sixth-man") is { } bench)
            {
                var line = stats[bench.PlayerId].Stats;
                Assert.True(line.Starts * 2 < line.Games, $"Season {season}: {bench.PlayerName} won the bench award after {line.Starts} starts in {line.Games} games.");
            }

            var teams = session.Overview().Value.Teams;
            var teamOf = teams.SelectMany(team => team.Roster.Select(player => (player.PlayerId, team.TeamId))).ToDictionary(entry => entry.PlayerId, entry => entry.TeamId);
            var moved = stars.Count(star => teamOf.TryGetValue(star.PlayerId, out var now) && now != star.TeamId);
            Assert.True(moved <= 3, $"Summer after season {season}: {moved} of the league's thirty best players changed teams.");
            Assert.All(teams, team => Assert.InRange(team.RosterCount, 12, 15));
            Assert.InRange(summary.SalaryDumps, 0, 6);
        }

        // The tradable window of future picks rolls forward with the seasons rather than running out.
        var board = session.Overview().Value.PickBoard;
        var lastDraft = board.DraftSeasons.Max();
        Assert.All(board.Franchises, row => Assert.NotEmpty(row.Drafts.Single(cell => cell.DraftSeason == lastDraft).Assets));

        Assert.True(leaders.Average() >= 30, $"Scoring leaders averaged {leaders.Average():F1} a game over {Seasons} seasons.");
        Assert.True(leaders.Min() >= 27, $"A season's scoring leader averaged only {leaders.Min():F1}.");

        var endMedian = Median(session);
        Assert.InRange(endMedian - startMedian, -6, 6);
        var ages = session.Overview().Value.Teams.SelectMany(team => team.Roster).Select(player => player.Age).ToList();
        Assert.InRange(ages.Average(), 24.0, 28.0);
    }

    private static int Median(LeagueSession session)
    {
        var overalls = session.Overview().Value.Teams.SelectMany(team => team.Roster).Select(player => player.Overall).Order().ToList();
        return overalls[overalls.Count / 2];
    }

    private static LeagueSession NewSession()
    {
        var session = new LeagueSession(
            new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 3),
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
