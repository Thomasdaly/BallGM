using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

/// <summary>
/// The negotiation section of a ruleset. Every field is optional by absence, and the contradictions
/// that absence cannot excuse are refused at load rather than reinterpreted at run time — the same
/// bargain the draft rules already strike with "no draft, but here is a retained round".
/// </summary>
public sealed class NegotiationRulesTests
{
    private static readonly CapThresholds Capped = CapThresholds.Create(
        softCap: new Money(100_000_000),
        firstApron: new Money(130_000_000)).Value;

    /// <summary>
    /// The headline reading. A league that configures none of this is an open market — anyone may be
    /// offered anything for any term — and emphatically not a league where nobody may sign.
    /// </summary>
    [Fact]
    public void ALeagueThatConfiguresNothingIsAnOpenMarketRatherThanALeagueWithNoSignings()
    {
        var rules = NegotiationRules.OpenMarket;

        Assert.Null(rules.MaximumContractSeasons);
        Assert.Null(rules.MaximumSeasonsFor(isIncumbentTeam: true));
        Assert.False(rules.HasTermLimit);
        Assert.False(rules.HasEscalationLimit);
        Assert.False(rules.HasStandardOverCapAllowance);
        Assert.False(rules.CompensationFloor.IsConfigured);
        Assert.False(rules.CompensationCeiling.IsConfigured);
        Assert.Null(rules.CompensationFloor.FloorFor(0));
        Assert.Null(rules.CompensationCeiling.CeilingFor(0, new Money(100_000_000)));
    }

    /// <summary>
    /// Market resolution is a mode, not a limit, so its absence is a documented default rather than
    /// "this league does not resolve offers". Every league resolves offers somehow.
    /// </summary>
    [Fact]
    public void AnAbsentMarketResolutionModeDefaultsRatherThanMeaningNoSuchRule()
    {
        Assert.Equal(MarketResolutionMode.ResolutionPoint, NegotiationRules.ParseMarketResolution(null).Value);
        Assert.Equal(MarketResolutionMode.ResolutionPoint, NegotiationRules.ParseMarketResolution("  ").Value);
        Assert.Equal(MarketResolutionMode.Immediate, NegotiationRules.ParseMarketResolution("Immediate").Value);
        Assert.True(NegotiationRules.ParseMarketResolution("Whenever").IsFailure);
    }

    [Fact]
    public void AnIncumbentTeamMayOfferTheLongerTermWhereTheLeagueAllowsOne()
    {
        var rules = Build(maximumContractSeasons: 5, maximumIncumbentContractSeasons: 6);

        Assert.Equal(5, rules.MaximumSeasonsFor(isIncumbentTeam: false));
        Assert.Equal(6, rules.MaximumSeasonsFor(isIncumbentTeam: true));
    }

    [Fact]
    public void AnIncumbentTermLimitWithNoGeneralLimitIsRefusedAsAFileThatHasLostAField()
    {
        var result = Create(maximumIncumbentContractSeasons: 6);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.incumbent_term_without_base_term", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AnIncumbentAllowanceShorterThanTheGeneralLimitIsRefused()
    {
        var result = Create(maximumContractSeasons: 5, maximumIncumbentContractSeasons: 4);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.incumbent_term_below_base_term", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// A ceiling expressed as a share of the soft cap is not expressible in a league with no soft
    /// cap. A file stating both is a contradiction, and is refused as one rather than loaded and
    /// quietly reinterpreted as no ceiling at all.
    /// </summary>
    [Fact]
    public void ACompensationCeilingWithoutASoftCapIsRefused()
    {
        var result = NegotiationRules.Create(
            CapThresholds.Uncapped,
            null,
            null,
            null,
            null,
            CompensationCeilingScale.Create([new ScaleBand(0, 25)]).Value,
            CompensationFloorScale.None,
            null,
            null,
            false,
            MarketResolutionMode.ResolutionPoint,
            null);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.ceiling_requires_soft_cap", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AnOverCapAllowanceWithoutASoftCapIsRefusedBecauseThereIsNoCapToBeOver()
    {
        var result = NegotiationRules.Create(
            CapThresholds.Uncapped,
            null,
            null,
            null,
            null,
            null,
            null,
            new Money(12_000_000),
            null,
            false,
            MarketResolutionMode.ResolutionPoint,
            null);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.allowance_requires_soft_cap", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void WithdrawingAnAllowanceAboveALineTheLeagueDoesNotConfigureIsRefused()
    {
        var result = NegotiationRules.Create(
            CapThresholds.Create(softCap: new Money(100_000_000)).Value,
            null,
            null,
            null,
            null,
            null,
            null,
            new Money(12_000_000),
            CapThresholdKind.FirstApron,
            false,
            MarketResolutionMode.ResolutionPoint,
            null);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.allowance_limit_threshold_not_configured", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SettingsThatOnlyMakeSenseAlongsideAnAllowanceAreRefusedWithoutOne()
    {
        var result = NegotiationRules.Create(
            Capped,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            allowanceMaySplitAcrossPlayers: true,
            MarketResolutionMode.ResolutionPoint,
            null);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.allowance_setting_without_allowance", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// Zero is a legal band value on a scale of amounts, but a ceiling of nought percent says no
    /// player may be paid anything, which no ruleset means to say.
    /// </summary>
    [Fact]
    public void ACeilingBandOfNoughtPercentIsRefused()
    {
        var result = CompensationCeilingScale.Create([new ScaleBand(0, 0)]);

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.non_positive_ceiling_percent", Assert.Single(result.Errors).Code);
    }

    /// <summary>The ceiling truncates: one that rounded up would be a line a team could sit above.</summary>
    [Fact]
    public void TheCeilingIsAShareOfTheSoftCapAndTruncatesRatherThanRounding()
    {
        var ceiling = CompensationCeilingScale.Create([new ScaleBand(0, 25), new ScaleBand(7, 30)]).Value;

        Assert.Equal(25_000_000, ceiling.CeilingFor(0, new Money(100_000_000))?.SmallestUnits);
        Assert.Equal(30_000_000, ceiling.CeilingFor(9, new Money(100_000_000))?.SmallestUnits);
        Assert.Equal(25, ceiling.CeilingFor(0, new Money(101))?.SmallestUnits);
        Assert.Null(ceiling.CeilingFor(0, null));
    }

    private static NegotiationRules Build(
        int? maximumContractSeasons = null,
        int? maximumIncumbentContractSeasons = null) =>
        Create(maximumContractSeasons, maximumIncumbentContractSeasons).Value;

    private static DomainOperationResult<NegotiationRules> Create(
        int? maximumContractSeasons = null,
        int? maximumIncumbentContractSeasons = null) =>
        NegotiationRules.Create(
            Capped,
            maximumContractSeasons,
            maximumIncumbentContractSeasons,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            MarketResolutionMode.ResolutionPoint,
            null);
}
