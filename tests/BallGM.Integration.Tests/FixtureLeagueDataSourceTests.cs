using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;

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
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var overview = result.Value;
        Assert.NotEmpty(overview.Teams);
        Assert.All(overview.Teams, team =>
        {
            Assert.False(string.IsNullOrWhiteSpace(team.TeamName));
            Assert.False(string.IsNullOrWhiteSpace(team.FranchiseName));
            Assert.InRange(team.RosterCount, overview.MinimumRosterPlayers, overview.MaximumRosterPlayers);
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

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(rulesetPath), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(11, result.Value.MaximumRosterPlayers);
        Assert.All(result.Value.Teams, team => Assert.Equal(11, team.RosterCount));
    }

    [Fact]
    public void CapThresholdsComeFromTheRulesetFile()
    {
        var rulesetPath = WriteRuleset(maximumRosterPlayers: 12, softCap: 99_000_000);

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(rulesetPath), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal(99_000_000, result.Value.CapThresholds.SoftCap);
    }

    [Fact]
    public void MissingRulesetFile_ExplainsItselfInsteadOfThrowing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"ballgm-missing-{Guid.NewGuid():N}", "ruleset.json");

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(missingPath), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("fixture.ruleset_file_missing", error.Code);
    }

    [Fact]
    public void MalformedRulesetFile_ExplainsItselfInsteadOfThrowing()
    {
        var path = Path.Combine(CreateTempDirectory(), "ruleset.json");
        File.WriteAllText(path, "{ this is not json");

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(path), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.malformed_file", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void RulesetWithThresholdsOutOfOrder_ExplainsItselfInsteadOfThrowing()
    {
        var path = Path.Combine(CreateTempDirectory(), "ruleset.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
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
              "retainedRoundInterval": 2
            }
            """);

        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(path), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.cap_thresholds_out_of_order", Assert.Single(result.Errors).Code);
    }

    private static LeagueOverview LoadShippedLeague()
    {
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

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
              "schemaVersion": 2,
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
              "tradableFutureDraftHorizon": 5,
              "retainedRoundNumber": 1,
              "retainedRoundInterval": 2
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
