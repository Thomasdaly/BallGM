using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class DraftRulesTests
{
    [Fact]
    public void CreateRejectsANegativeRoundCount()
    {
        var result = DraftRules.Create(
            roundCount: -1,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 1,
            retainedRoundInterval: 2);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.invalid_draft_round_count", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ALeagueWithNoDraftIsConfiguredByLeavingTheDraftOut()
    {
        var result = DraftRules.Create(
            roundCount: 0,
            lotteryEnabled: false,
            tradableFutureDraftHorizon: 0,
            retainedRoundNumber: 0,
            retainedRoundInterval: 0);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasDraft);
        Assert.Equal(0, result.Value.RoundCount);
    }

    [Fact]
    public void ALeagueWithNoDraftCannotAlsoConfigureDraftRestrictions()
    {
        var result = DraftRules.Create(
            roundCount: 0,
            lotteryEnabled: false,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 0,
            retainedRoundInterval: 0);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.draft_restrictions_without_draft", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void CreateAcceptsAFullyConfiguredDraft()
    {
        var result = DraftRules.Create(
            roundCount: 2,
            lotteryEnabled: false,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 1,
            retainedRoundInterval: 2);

        Assert.True(result.IsSuccess);

        var draftRules = result.Value;
        Assert.True(draftRules.HasDraft);
        Assert.Equal(2, draftRules.RoundCount);
        Assert.False(draftRules.LotteryEnabled);
        Assert.Equal(5, draftRules.TradableFutureDraftHorizon);
        Assert.Equal(1, draftRules.RetainedRoundNumber);
        Assert.Equal(2, draftRules.RetainedRoundInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void CreateRejectsANonPositiveTradableHorizonInALeagueThatDrafts(int horizon)
    {
        var result = DraftRules.Create(
            roundCount: 2,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: horizon,
            retainedRoundNumber: 1,
            retainedRoundInterval: 2);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_tradable_draft_horizon");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void CreateRejectsARetainedRoundTheDraftDoesNotHave(int retainedRound)
    {
        var result = DraftRules.Create(
            roundCount: 2,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: retainedRound,
            retainedRoundInterval: 2);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_retained_round");
    }

    [Fact]
    public void CreateRejectsARetentionIntervalBelowOne()
    {
        var result = DraftRules.Create(
            roundCount: 2,
            lotteryEnabled: true,
            tradableFutureDraftHorizon: 5,
            retainedRoundNumber: 1,
            retainedRoundInterval: 0);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_retained_round_interval");
    }
}
