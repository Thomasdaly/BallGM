using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

public sealed class SeasonAwardCalculatorTests
{
    private static readonly PlayerId Scorer = new("player-scorer");
    private static readonly PlayerId Passer = new("player-passer");

    private static readonly PlayerSeasonStatLine[] StatLines =
    [
        new(Scorer, CountingStats.None with { Games = 10, Minutes = 300, Points = 250, DefensiveRebounds = 40, Assists = 20 }),
        new(Passer, CountingStats.None with { Games = 10, Minutes = 300, Points = 100, DefensiveRebounds = 30, Assists = 90 }),
    ];

    [Fact]
    public void CalculateAwardsEachToItsStatLeader()
    {
        var rules = AwardRules.Create([
            new AwardDefinition("scoring-leader", "Scoring Leader", AwardStatBasis.TotalPoints),
            new AwardDefinition("assists-leader", "Assists Leader", AwardStatBasis.TotalAssists),
        ]).Value;

        var results = SeasonAwardCalculator.Calculate(StatLines, rules);

        var scoring = Assert.Single(results, result => result.AwardCode == "scoring-leader");
        Assert.Equal(Scorer, scoring.WinnerId);

        var assists = Assert.Single(results, result => result.AwardCode == "assists-leader");
        Assert.Equal(Passer, assists.WinnerId);
    }

    [Fact]
    public void CalculateWithNoConfiguredAwardsReturnsNoResults()
    {
        var results = SeasonAwardCalculator.Calculate(StatLines, AwardRules.None);

        Assert.Empty(results);
    }

    [Fact]
    public void CalculateWithNoStatLinesReportsNoWinner()
    {
        var rules = AwardRules.Create([new AwardDefinition("mvp", "Most Valuable Player", AwardStatBasis.TotalPoints)]).Value;

        var results = SeasonAwardCalculator.Calculate([], rules);

        var result = Assert.Single(results);
        Assert.Null(result.WinnerId);
        Assert.Equal("award.no_stat_lines", result.Finding.RuleCode);
    }

    [Fact]
    public void CalculateBreaksATieByPlayerIdOrdinal()
    {
        var tied = new[]
        {
            new PlayerSeasonStatLine(new PlayerId("player-b"), CountingStats.None with { Games = 5, Minutes = 100, Points = 50, DefensiveRebounds = 10, Assists = 10 }),
            new PlayerSeasonStatLine(new PlayerId("player-a"), CountingStats.None with { Games = 5, Minutes = 100, Points = 50, DefensiveRebounds = 10, Assists = 10 }),
        };
        var rules = AwardRules.Create([new AwardDefinition("mvp", "Most Valuable Player", AwardStatBasis.TotalPoints)]).Value;

        var result = Assert.Single(SeasonAwardCalculator.Calculate(tied, rules));

        Assert.Equal("player-a", result.WinnerId!.Value);
    }
}
