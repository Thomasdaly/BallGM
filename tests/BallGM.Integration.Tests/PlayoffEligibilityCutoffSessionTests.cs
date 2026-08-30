using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;
using BallGM.Rules.Signings;

namespace BallGM.Integration.Tests;

/// <summary>
/// The playoff eligibility cutoff end to end: a day on the session's season reaching the signing
/// rules through the Application port, and changing what an offer screen is told.
/// <para>
/// The fixture ruleset states a cutoff on day 170, the last day of its regular season. Before that
/// day a signing is an ordinary signing; after it, the same offer for the same money is still
/// permitted and carries a warning that the player cannot appear in the postseason.
/// </para>
/// </summary>
public sealed class PlayoffEligibilityCutoffSessionTests
{
    private const int CutoffDay = 170;

    [Fact]
    public void AnOfferMadeBeforeTheCutoffCarriesNoEligibilityWarning()
    {
        var session = NewSession(out var overview);
        Assert.True(session.StartSeason(seed: 11).IsSuccess);

        var assessment = Assess(session, overview);

        Assert.True(assessment.IsLegal);
        Assert.DoesNotContain(
            assessment.Warnings,
            warning => warning.RuleCode == SigningValidator.PostseasonIneligibleCode);
    }

    [Fact]
    public void AnOfferMadeAfterTheCutoffIsPermittedAndSaysThePlayerCannotPlayInThePostseason()
    {
        var session = NewSession(out var overview);
        Assert.True(session.StartSeason(seed: 11).IsSuccess);

        var advanced = session.AdvanceDays(CutoffDay + 1);
        Assert.True(advanced.IsSuccess, string.Join("; ", advanced.Errors.Select(error => error.Message)));

        var assessment = Assess(session, overview);

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
        Assert.Contains(
            assessment.Warnings,
            warning => warning.RuleCode == SigningValidator.PostseasonIneligibleCode);
    }

    [Fact]
    public void AnOfferMadeWithNoSeasonUnderWayReportsThatTheCutoffCouldNotBeChecked()
    {
        var session = NewSession(out var overview);

        // No StartSeason: the session holds a league but not a calendar, so there is no day to
        // measure the signing against and the assessment says which of the three cases it is.
        var assessment = Assess(session, overview);

        Assert.Contains(
            assessment.Notes,
            note => note.RuleCode == SigningValidator.EligibilityUncheckableCode);
    }

    private static SigningAssessmentSummary Assess(LeagueSession session, LeagueOverview overview)
    {
        var team = overview.Teams.Single(candidate => candidate.TeamName == "Old Foundry Bellringers");
        var player = overview.FreeAgents.Players[0];

        var request = new OfferRequest(
            team.TeamId,
            player.PlayerId,
            [new OfferSeasonRequest(2031, 12_000_000, 12_000_000)]);

        var result = session.AssessOffer(request);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
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
            new SaveGameSerializer());

        var result = session.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        overview = result.Value;
        return session;
    }
}
