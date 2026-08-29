using BallGM.Domain.Negotiations;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

/// <summary>
/// This league's playoff eligibility cutoff, applied to the day a signing is made on.
/// <para>
/// The cutoff is the one rule in the signing path that reads a calendar, and the three ways it can
/// fail to apply — no postseason, no stated cutoff, no season under way — each say so. A check that
/// never ran is otherwise indistinguishable from a check that ran and approved.
/// </para>
/// </summary>
public sealed class PlayoffEligibilityCutoffTests
{
    private static readonly SigningValidator Validator = new();

    [Fact]
    public void WarnsThatAPlayerSignedAfterTheCutoffCannotPlayInThePostseason()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, new SeasonDay(61), Postseason(cutoffDay: 60));

        // Permitted, deliberately: a league with a cutoff decides who may appear in the postseason,
        // not who may be signed.
        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));

        var warning = Assert.Single(
            assessment.Warnings,
            finding => finding.RuleCode == SigningValidator.PostseasonIneligibleCode);

        Assert.Contains("day 60", warning.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysNothingAboutEligibilityForASigningMadeOnTheCutoffItself()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        // The cutoff is the last day a player may be added and still be eligible, so the day itself
        // is inside it.
        var assessment = Assess(league, new SeasonDay(60), Postseason(cutoffDay: 60));

        Assert.DoesNotContain(
            assessment.Warnings,
            finding => finding.RuleCode == SigningValidator.PostseasonIneligibleCode);

        Assert.DoesNotContain(
            assessment.Notes,
            finding => finding.RuleCode == SigningValidator.EligibilityUncheckableCode);
    }

    [Fact]
    public void ReportsThatALeagueStatingNoCutoffHasNoSuchRule()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, new SeasonDay(200), Postseason(cutoffDay: null));

        Assert.Contains(
            assessment.Notes,
            finding => finding.RuleCode == SigningValidator.NoEligibilityCutoffCode);

        Assert.DoesNotContain(
            assessment.Warnings,
            finding => finding.RuleCode == SigningValidator.PostseasonIneligibleCode);
    }

    [Fact]
    public void ReportsThatALeagueWithNoPostseasonHasNoSuchRule()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, new SeasonDay(200), PostseasonRules.None);

        Assert.Contains(
            assessment.Notes,
            finding => finding.RuleCode == SigningValidator.NoEligibilityCutoffCode);
    }

    [Fact]
    public void ReportsThatTheCutoffCouldNotBeCheckedWithNoSeasonUnderWay()
    {
        var league = SigningTestLeague.Build([40_000_000, 20_000_000, 10_000_000]);

        var assessment = Assess(league, day: null, Postseason(cutoffDay: 60));

        // Not silently eligible: a signing made outside a season has no day to measure, and the
        // assessment says so rather than approving by omission.
        Assert.Contains(
            assessment.Notes,
            finding => finding.RuleCode == SigningValidator.EligibilityUncheckableCode);

        Assert.DoesNotContain(
            assessment.Warnings,
            finding => finding.RuleCode == SigningValidator.PostseasonIneligibleCode);
    }

    private static PostseasonRules Postseason(int? cutoffDay)
    {
        var created = PostseasonRules.Create(
            postseasonDays: 30,
            qualifyingTeamsPerConference: 4,
            seriesLengths: [7, 7],
            homeCourtSequence: "2-2-1-1-1",
            playoffEligibilityCutoffDay: cutoffDay,
            regularSeasonEndDay: 100,
            includesFinal: false);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static SigningAssessment Assess(SigningTestLeague league, SeasonDay? day, PostseasonRules postseason)
    {
        var result = Validator.Validate(league.Offer(20_000_000), league.ContextOn(day, postseason));
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
