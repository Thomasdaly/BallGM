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
/// The Milestone 9 diagnostics query against the shipped fixture, through the real session, the real
/// four AI models, and the real ruleset file — the same path the client's diagnostics screen takes.
/// Every assertion here is about the read staying legal and explainable; it does not pin exact
/// candidates, because the shipped fixture's roster shape is not this test's concern.
/// </summary>
public sealed class FixtureFrontOfficeAdvisoryTests
{
    private const string SomeTeam = "Old Foundry Bellringers";

    [Fact]
    public void UnknownTeam_FailsExplainably()
    {
        var session = NewSession(out _);

        var result = session.FrontOfficeAdvisory("no-such-team");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ai_advisory.unknown_team");
    }

    [Fact]
    public void RealTeam_ReadsAllFourSectionsWithoutChangingTheLeague()
    {
        var session = NewSession(out var overview);
        var team = overview.Teams.Single(candidate => candidate.TeamName == SomeTeam);

        var result = session.FrontOfficeAdvisory(team.TeamId);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var advisory = result.Value;
        Assert.Equal(team.TeamId, advisory.TeamId);
        Assert.Equal(SomeTeam, advisory.TeamName);

        // Direction is always one of the three named postures, with at least one finding behind it.
        Assert.Contains(advisory.Direction.Direction, new[] { "Rebuilding", "Retooling", "Contending" });
        Assert.NotEmpty(advisory.Direction.Factors);
        Assert.All(advisory.Direction.Factors, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Explanation)));

        // Every trade and free-agent target this model surfaces already cleared the real validator.
        Assert.All(advisory.TradeTargets, target => Assert.True(target.Assessment.IsLegal));
        Assert.All(advisory.FreeAgentTargets, target => Assert.True(target.Assessment.IsLegal));

        // The shipped ruleset generates its own draft classes, so the preview names a real prospect
        // rather than falling back to the "no draft" note.
        Assert.Null(advisory.DraftPreviewNote);
        Assert.NotNull(advisory.DraftPreview);
        Assert.False(string.IsNullOrWhiteSpace(advisory.DraftPreview!.ProspectName));
        Assert.NotEmpty(advisory.DraftPreview.Rationale);

        // Read-only, and reproducible from the fixed preview seed: the generated content (never the
        // prospect's identifier, which mints fresh with SortableId.NewId() on every generation, the
        // same way FixtureLeagueDataSource's own identifiers do) comes back identical on a second ask.
        var again = session.FrontOfficeAdvisory(team.TeamId);
        Assert.True(again.IsSuccess);
        Assert.Equal(advisory.DraftPreview.ProspectName, again.Value.DraftPreview!.ProspectName);
        Assert.Equal(advisory.DraftPreview.Position, again.Value.DraftPreview.Position);
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
