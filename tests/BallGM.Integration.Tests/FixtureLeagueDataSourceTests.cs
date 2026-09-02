using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;
using BallGM.Rules.Configuration;

namespace BallGM.Integration.Tests;

/// <summary>
/// Exercises the moddable-rules path end to end: a ruleset file on disk, through
/// <c>LeagueRulesetSerializer</c>, into aggregates, into the read model the client renders.
/// </summary>
public sealed class FixtureLeagueDataSourceTests
{
    [Fact]
    public void ShippedRulesetFile_ProducesALeagueTheClientCanRender()
    {
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var overview = result.Value;
        Assert.NotEmpty(overview.Teams);
        Assert.All(overview.Teams, team =>
        {
            Assert.False(string.IsNullOrWhiteSpace(team.TeamName));
            Assert.False(string.IsNullOrWhiteSpace(team.FranchiseName));
            // Not every team is at or above the roster minimum, deliberately: a league where every
            // squad is full is a league where free agency cannot be played, and a team short of the
            // minimum is what puts a roster-slot hold on a cap sheet.
            Assert.InRange(team.RosterCount, 1, overview.MaximumRosterPlayers);
            Assert.Equal(team.RosterCount, team.Roster.Count);
        });
    }

    [Fact]
    public void EveryPlayerInTheLeagueHasADistinctName()
    {
        var overview = LoadShippedLeague();

        var names = overview.Teams.SelectMany(team => team.Roster).Select(spot => spot.FullName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TeamsDoNotRepeatTheSameGivenNamesInTheSameOrder()
    {
        var overview = LoadShippedLeague();

        var givenNameSequences = overview.Teams
            .Select(team => string.Join(",", team.Roster.Select(spot => spot.FullName.Split(' ')[0])))
            .ToList();

        Assert.Equal(givenNameSequences.Count, givenNameSequences.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryTeamFieldsAFullSpreadOfPositions()
    {
        var overview = LoadShippedLeague();

        Assert.All(overview.Teams, team =>
            Assert.Equal(5, team.Roster.Select(spot => spot.Position).Distinct(StringComparer.Ordinal).Count()));
    }

    [Fact]
    public void TeamsAreNotRatingIdenticalCopiesOfEachOther()
    {
        var overview = LoadShippedLeague();

        var ratingProfiles = overview.Teams
            .Select(team => string.Join(",", team.Roster.Select(spot => spot.Overall)))
            .ToList();

        Assert.Equal(ratingProfiles.Count, ratingProfiles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AFranchiseIsNamedSeparatelyFromTheTeamItFields()
    {
        var overview = LoadShippedLeague();

        Assert.All(overview.Teams, team => Assert.NotEqual(team.FranchiseName, team.TeamName));
    }

    [Fact]
    public void RosterSizeFollowsTheRulesetFileRatherThanACompiledConstant()
    {
        var rulesetPath = WriteRuleset(maximumRosterPlayers: 11);

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(rulesetPath), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(11, result.Value.MaximumRosterPlayers);

        // Rosters are filled to varying depths below the file's maximum, so what the file controls is
        // the ceiling: no team exceeds it, and the fullest team sits exactly on it.
        Assert.All(result.Value.Teams, team => Assert.InRange(team.RosterCount, 1, 11));
        Assert.Equal(11, result.Value.Teams.Max(team => team.RosterCount));
    }

    /// <summary>
    /// A league where every roster is full is a league where free agency cannot be played, so the
    /// fixture leaves spots open — and takes one team below the roster minimum, which is the state a
    /// roster-slot hold exists to price.
    /// </summary>
    [Fact]
    public void TheFixtureLeavesRosterSpotsOpenAndTakesOneTeamBelowTheRosterMinimum()
    {
        var overview = LoadShippedLeague();

        Assert.Contains(overview.Teams, team => team.RosterCount == overview.MaximumRosterPlayers);
        Assert.Contains(overview.Teams, team => team.RosterCount < overview.MaximumRosterPlayers);
        Assert.Contains(overview.Teams, team => team.RosterCount < overview.MinimumRosterPlayers);
    }

    [Fact]
    public void CapThresholdsComeFromTheRulesetFile()
    {
        var rulesetPath = WriteRuleset(maximumRosterPlayers: 12, softCap: 99_000_000);

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(rulesetPath), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal(99_000_000, result.Value.CapThresholds.SoftCap);
    }

    [Fact]
    public void MissingRulesetFile_ExplainsItselfInsteadOfThrowing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"ballgm-missing-{Guid.NewGuid():N}", "ruleset.json");

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(missingPath), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("fixture.ruleset_file_missing", error.Code);
    }

    [Fact]
    public void MalformedRulesetFile_ExplainsItselfInsteadOfThrowing()
    {
        var path = Path.Combine(CreateTempDirectory(), "ruleset.json");
        File.WriteAllText(path, "{ this is not json");

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(path), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.malformed_file", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void RulesetWithThresholdsOutOfOrder_ExplainsItselfInsteadOfThrowing()
    {
        var path = Path.Combine(CreateTempDirectory(), "ruleset.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "schemaVersion": {{LeagueRuleset.CurrentSchemaVersion}},
              "name": "Broken Thresholds",
              "regularSeasonGameCount": 78,
              "minimumRosterPlayers": 10,
              "maximumRosterPlayers": 12,
              "softCap": 200000000,
              "luxuryTax": 100000000,
              "firstApron": 110000000,
              "secondApron": 120000000,
              "hardCap": 130000000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true,
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 1,
              "retainedRoundInterval": 2,
              "salaryMatchPercent": 125,
              "salaryMatchAllowance": 250000,
              "injuredPlayerTradeEligibility": "AllowedWithWarning",
              "secondApronBlocksSalaryIncrease": true
            }
            """);

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(path), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.cap_thresholds_out_of_order", Assert.Single(result.Errors).Code);
    }

    private static LeagueOverview LoadShippedLeague()
    {
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static string WriteRuleset(int maximumRosterPlayers, long softCap = 141_000_000)
    {
        var path = Path.Combine(CreateTempDirectory(), "ruleset.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "schemaVersion": {{LeagueRuleset.CurrentSchemaVersion}},
              "name": "Integration Test Ruleset",
              "regularSeasonGameCount": 78,
              "minimumRosterPlayers": 8,
              "maximumRosterPlayers": {{maximumRosterPlayers}},
              "softCap": {{softCap}},
              "luxuryTax": 172000000,
              "firstApron": 179000000,
              "secondApron": 190000000,
              "hardCap": 205000000,
              "draftRoundCount": 2,
              "draftLotteryEnabled": true,
              "draftLotteryWeights": [140, 125],
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 1,
              "retainedRoundInterval": 2,
              "salaryMatchPercent": 125,
              "salaryMatchAllowance": 250000,
              "injuredPlayerTradeEligibility": "AllowedWithWarning",
              "secondApronBlocksSalaryIncrease": true
            }
            """);

        return path;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ballgm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
