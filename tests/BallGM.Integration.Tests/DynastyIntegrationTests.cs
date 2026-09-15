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
/// A single <see cref="LeagueSession"/> chained across many seasons with no human re-signing a
/// departing free agent and no human running the draft — the exact shape the sim audit's dynasty run
/// reproduced failing at season 4 of 50 ("Team ... has nobody available, so it cannot be put on the
/// floor for this game."), because nothing auto-resigned anyone once a roster fell below the
/// configured minimum and nothing drafted a rookie once a class was generated. Milestone 8's own
/// development/retirement rules still have no call site here (a separate, larger piece of the same
/// gap), so this only proves the free-agency and draft halves survive unattended.
/// </summary>
public sealed class DynastyIntegrationTests
{
    [Fact]
    public void SurvivesManyChainedSeasonsWithoutAHumanReSigningAnyFreeAgent()
    {
        const int seasonCount = 15;
        var session = NewSession();
        var totalAutoSigned = 0;
        var totalDrafted = 0;

        for (var index = 0; index < seasonCount; index++)
        {
            var seed = 3000 + index;

            var started = session.StartSeason(seed: seed);
            Assert.True(started.IsSuccess, $"Season {index}: {Describe(started.Errors)}");

            var advanced = session.AdvanceDays(session.Season().Value.Calendar.LengthInDays);
            Assert.True(advanced.IsSuccess, $"Season {index}: {Describe(advanced.Errors)}");
            Assert.True(
                session.Season().Value.Calendar.IsComplete,
                $"Season {index} did not reach the end of its calendar — a team likely ran out of players to field.");

            var overviewBeforeConclusion = session.Overview().Value;
            foreach (var team in overviewBeforeConclusion.Teams)
            {
                Assert.True(
                    team.RosterCount >= 5,
                    $"Season {index}: team '{team.TeamName}' finished with only {team.RosterCount} players, below what a game needs on the floor.");
            }

            var conclusion = session.ConcludeSeason();
            Assert.True(conclusion.IsSuccess, $"Season {index}: {Describe(conclusion.Errors)}");
            totalAutoSigned += conclusion.Value.PlayersAutoSigned;
            totalDrafted += conclusion.Value.PlayersDrafted;
        }

        Assert.True(
            totalAutoSigned > 0,
            "Across 15 chained seasons of attrition with no human intervention, the roster-floor auto-resign never fired — either it is broken or this fixture league never falls below its minimum, which would make this test meaningless.");

        Assert.True(
            totalDrafted > 0,
            "Across 15 chained seasons, no draft selection was ever signed — either the draft never fired or every selection failed to sign, which would make this test meaningless for the wiring it is supposed to prove.");
    }

    private static string Describe(IReadOnlyList<BallGM.Domain.Common.DomainError> errors) =>
        string.Join("; ", errors.Select(error => error.Message));

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
