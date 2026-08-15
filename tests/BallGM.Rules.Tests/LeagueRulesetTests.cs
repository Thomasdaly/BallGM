using BallGM.Domain.Common;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class LeagueRulesetTests
{
    [Fact]
    public void ConstructorAcceptsFullyConfiguredRuleset()
    {
        var ruleset = new LeagueRuleset(
            schemaVersion: LeagueRuleset.CurrentSchemaVersion,
            name: "Standard Fictional Ruleset",
            regularSeasonGameCount: 82,
            rosterLimits: new RosterSizeLimits(minimumPlayers: 12, maximumPlayers: 15),
            capThresholds: CapThresholds.Create(
                new Money(100),
                new Money(120),
                new Money(130),
                new Money(140),
                new Money(150)).Value,
            draftRules: new DraftRules(roundCount: 2, lotteryEnabled: true, tradableFutureDraftHorizon: 5, retainedRoundNumber: 1, retainedRoundInterval: 2));

        Assert.Equal(82, ruleset.RegularSeasonGameCount);
        Assert.Equal(15, ruleset.RosterLimits.MaximumPlayers);
        Assert.Equal(2, ruleset.DraftRules.RoundCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsNonPositiveGameCount(int gameCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeagueRuleset(
            LeagueRuleset.CurrentSchemaVersion,
            "Standard Fictional Ruleset",
            gameCount,
            new RosterSizeLimits(minimumPlayers: 12, maximumPlayers: 15),
            CapThresholds.Create(new Money(100), new Money(120), new Money(130), new Money(140), new Money(150)).Value,
            new DraftRules(roundCount: 2, lotteryEnabled: true, tradableFutureDraftHorizon: 5, retainedRoundNumber: 1, retainedRoundInterval: 2)));
    }
}
