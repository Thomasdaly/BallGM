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
/// The season boundary through the real stack: a fixture league plays a full season to a champion,
/// concludes it, and starts a second season off the same session — the "played for multiple seasons"
/// half of the vertical-slice target in <c>docs/product-scope.md</c>. Nothing here saves or loads
/// anything; that is <c>SaveGameDeterminismTests</c>' job.
/// </summary>
public sealed class TwoSeasonIntegrationTests
{
    [Fact]
    public void ASecondSeasonStartsCoherentlyFromTheFirstOnesConclusion()
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed: 2031).IsSuccess);
        Assert.True(session.AdvanceDays(session.Season().Value.Calendar.LengthInDays).IsSuccess);

        var overviewBefore = session.Overview().Value;
        var rosteredBefore = overviewBefore.Teams.Sum(team => team.RosterCount);
        var freeAgentsBefore = overviewBefore.FreeAgents.Players.Count;

        var conclusion = session.ConcludeSeason();
        Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));
        Assert.False(session.HasSeason, "Concluding a season should leave none in progress.");

        var released = conclusion.Value.PlayersReleasedToFreeAgency;
        var drafted = conclusion.Value.PlayersDrafted;
        var unrostered = conclusion.Value.PlayersDraftedButUnrostered;
        var autoSigned = conclusion.Value.PlayersAutoSigned;
        // AI-executed trades move an already-rostered player between two teams, so they leave the
        // league-wide rostered and free-agent totals below untouched; only a signing changes either,
        // the same way PlayersAutoSigned already does.
        var aiSigned = conclusion.Value.AiSigningsExecuted;

        var overviewAfter = session.Overview().Value;
        Assert.Equal(conclusion.Value.NextSeasonYear, overviewAfter.SeasonYear);
        Assert.Equal(rosteredBefore - released + drafted + autoSigned + aiSigned, overviewAfter.Teams.Sum(team => team.RosterCount));
        Assert.Equal(freeAgentsBefore + released + unrostered - autoSigned - aiSigned, overviewAfter.FreeAgents.Players.Count);

        var started = session.StartSeason(seed: 2032);
        Assert.True(started.IsSuccess, string.Join("; ", started.Errors.Select(error => error.Message)));
        Assert.Equal(conclusion.Value.NextSeasonYear, started.Value.Calendar.SeasonYear);

        var advanced = session.AdvanceDays(started.Value.Calendar.LengthInDays);
        Assert.True(advanced.IsSuccess, string.Join("; ", advanced.Errors.Select(error => error.Message)));
        Assert.True(session.Season().Value.Calendar.IsComplete);
        Assert.True(session.Season().Value.Calendar.PlayedGames > 0, "The second season should have played games.");
    }

    [Fact]
    public void RefusesToConcludeASeasonThatHasNotBeenPlayedOut()
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed: 2031).IsSuccess);

        var conclusion = session.ConcludeSeason();

        Assert.True(conclusion.IsFailure);
        Assert.Equal("league_session.season_not_complete", Assert.Single(conclusion.Errors).Code);
        Assert.True(session.HasSeason, "A refused conclusion must not touch the season in progress.");
    }

    /// <summary>
    /// The session clears its season on a successful conclusion, so "concluded twice" surfaces here
    /// as "no season is in progress to conclude" rather than <c>League.RecordSeason</c>'s own
    /// duplicate-year refusal — that refusal is exercised directly against the rule in
    /// <c>SeasonConclusionTests.RefusesToConcludeASeasonAlreadyConcludedAndMutatesNothingFurther</c>.
    /// </summary>
    [Fact]
    public void RefusesToConcludeWhenNoSeasonIsInProgress()
    {
        var session = NewSession();
        Assert.True(session.StartSeason(seed: 2031).IsSuccess);
        Assert.True(session.AdvanceDays(session.Season().Value.Calendar.LengthInDays).IsSuccess);
        Assert.True(session.ConcludeSeason().IsSuccess);

        var conclusion = session.ConcludeSeason();

        Assert.True(conclusion.IsFailure);
        Assert.Equal("league_session.no_season_in_progress", Assert.Single(conclusion.Errors).Code);
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
