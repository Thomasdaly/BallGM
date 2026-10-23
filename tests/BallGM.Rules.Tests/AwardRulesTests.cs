using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class AwardRulesTests
{
    [Fact]
    public void CreateSucceedsWithDistinctCodes()
    {
        var result = AwardRules.Create([
            new AwardDefinition("mvp", "Most Valuable Player", AwardStatBasis.TotalPoints),
            new AwardDefinition("apg-leader", "Assists Leader", AwardStatBasis.TotalAssists),
        ]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
        Assert.Equal(2, result.Value.Awards.Count);
    }

    [Fact]
    public void CreateWithNoAwardsIsNone()
    {
        var result = AwardRules.Create([]);

        Assert.True(result.IsSuccess);
        Assert.Same(AwardRules.None, result.Value);
    }

    [Fact]
    public void CreateRejectsAMissingCode()
    {
        var result = AwardRules.Create([new AwardDefinition("", "Most Valuable Player", AwardStatBasis.TotalPoints)]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_missing_code");
    }

    [Fact]
    public void CreateRejectsAMissingName()
    {
        var result = AwardRules.Create([new AwardDefinition("mvp", "", AwardStatBasis.TotalPoints)]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_missing_name");
    }

    [Fact]
    public void CreateRejectsADuplicateCode()
    {
        var result = AwardRules.Create([
            new AwardDefinition("mvp", "Most Valuable Player", AwardStatBasis.TotalPoints),
            new AwardDefinition("mvp", "Most Valuable Player Again", AwardStatBasis.TotalAssists),
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_duplicate_code");
    }

    [Fact]
    public void AKindedAwardNeedsNoStatBasis_ButAStatLeaderDoes()
    {
        Assert.True(AwardRules.Create([new AwardDefinition("bench", "Sixth Man", null) { Kind = AwardKind.Bench, MinimumGamesPercent = 79, MinimumMinutesPerGame = 20 }]).IsSuccess);

        var leader = AwardRules.Create([new AwardDefinition("leader", "Scoring Leader", null)]);
        Assert.Contains(leader.Errors, error => error.Code == "ruleset.award_missing_stat_basis");
    }

    [Theory]
    [InlineData(AwardKind.MostValuable, 3, "ruleset.award_invalid_teams")]
    [InlineData(AwardKind.AllLeague, 0, "ruleset.award_invalid_teams")]
    [InlineData(AwardKind.AllLeague, 6, "ruleset.award_invalid_teams")]
    public void OnlyATeamsAwardNamesTeams_AndNeverTooMany(AwardKind kind, int teams, string code)
    {
        var result = AwardRules.Create([new AwardDefinition("award", "Award", null) { Kind = kind, Teams = teams }]);

        Assert.Contains(result.Errors, error => error.Code == code);
    }

    [Theory]
    [InlineData(101, 0)]
    [InlineData(-1, 0)]
    [InlineData(50, 49)]
    public void EligibilityIsAShareOfGamesAndAMinutesFloorInsideAGame(int percent, int minutes)
    {
        var result = AwardRules.Create([new AwardDefinition("mvp", "MVP", null) { Kind = AwardKind.MostValuable, MinimumGamesPercent = percent, MinimumMinutesPerGame = minutes }]);

        Assert.Contains(result.Errors, error => error.Code == "ruleset.award_invalid_eligibility");
    }
}
