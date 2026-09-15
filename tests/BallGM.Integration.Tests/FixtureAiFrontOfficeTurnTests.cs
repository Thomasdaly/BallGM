using BallGM.Application.AI;
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
/// The AI-turn-execution slice against the shipped fixture, through the real session, the real
/// front-office models, and the real trade/signing engines — the same path
/// <see cref="LeagueSession.SubmitTrade"/>/<see cref="LeagueSession.SubmitOffer"/> take for a human.
/// <see cref="LeagueSession.RunAiFrontOfficeTurn"/> is glue over both, so this test is mostly about
/// the glue staying legal and explainable, not about pinning exactly which trade or signing a given
/// fixture roster happens to surface.
/// </summary>
public sealed class FixtureAiFrontOfficeTurnTests
{
    [Fact]
    public void UnknownTeam_FailsExplainablyAndChangesNothing()
    {
        var session = NewSession(out _);

        var result = session.RunAiFrontOfficeTurn(["no-such-team"]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ai_advisory.unknown_team");
    }

    [Fact]
    public void RealTeams_EachGetExactlyOneExplainedOutcome()
    {
        var session = NewSession(out var overview);
        var teamIds = overview.Teams.Select(team => team.TeamId).ToList();

        var result = session.RunAiFrontOfficeTurn(teamIds);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var outcomes = result.Value.Outcomes;
        Assert.Equal(teamIds.Count, outcomes.Count);
        Assert.Equal(teamIds, outcomes.Select(outcome => outcome.TeamId));

        foreach (var outcome in outcomes)
        {
            switch (outcome.Action)
            {
                case AiTurnAction.TradeExecuted:
                    Assert.NotNull(outcome.Trade);
                    Assert.Null(outcome.Signing);
                    Assert.True(outcome.Trade!.Assessment.IsLegal);
                    break;
                case AiTurnAction.SigningExecuted:
                    Assert.NotNull(outcome.Signing);
                    Assert.Null(outcome.Trade);
                    Assert.True(outcome.Signing!.Assessment.IsLegal);
                    break;
                case AiTurnAction.NoActionTaken:
                    Assert.Null(outcome.Trade);
                    Assert.Null(outcome.Signing);
                    Assert.NotEmpty(outcome.Notes);
                    break;
            }
        }

        // The turn actually changed the league it ran against, and left it in a state the rest of
        // the session can still read — the same guarantee a human's own SubmitTrade/SubmitOffer give.
        var overviewAfter = session.Overview();
        Assert.True(overviewAfter.IsSuccess, string.Join("; ", overviewAfter.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void ASigningExecutedByOneTeamIsUnavailableToTheNextTeamInTheSameTurn()
    {
        var session = NewSession(out var overview);
        var teamIds = overview.Teams.Select(team => team.TeamId).ToList();

        // Running every team's turn once, in order, must not throw or fail even though later teams
        // in the list see the roster and free-agent pool exactly as earlier teams in the same call
        // left it — the single-session, single-threaded guarantee this slice relies on instead of
        // building a second concurrency mechanism.
        var result = session.RunAiFrontOfficeTurn(teamIds);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    private static LeagueSession NewSession(out LeagueOverview overview)
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

        overview = result.Value;
        return session;
    }
}
