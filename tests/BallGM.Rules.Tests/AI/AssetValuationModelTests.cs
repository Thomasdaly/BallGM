using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests.AI;

public sealed class AssetValuationModelTests
{
    private static readonly Season CurrentSeason = new(2031);
    private static readonly TeamId TeamId = new("TEAM-A");

    private static readonly DevelopmentRules PeakRules =
        DevelopmentRules.Create(peakAgeStart: 24, peakAgeEnd: 29, growthCurve: null, declineCurve: null, varianceRange: 0).Value;

    private static readonly NegotiationRules PricedRules = NegotiationRules.Create(
        CapThresholds.Create(softCap: new Money(100_000_000)).Value,
        maximumContractSeasons: 5,
        maximumIncumbentContractSeasons: 6,
        maximumAnnualEscalationPercent: 8,
        maximumAnnualDeescalationPercent: 8,
        CompensationCeilingScale.Create([new ScaleBand(0, 25)]).Value,
        CompensationFloorScale.None,
        standardOverCapAllowance: null,
        standardOverCapAllowanceUnavailableAbove: null,
        allowanceMaySplitAcrossPlayers: false,
        MarketResolutionMode.ResolutionPoint,
        offerExpiryDays: null).Value;

    private static readonly Money SoftCap = new(100_000_000);

    [Fact]
    public void ProductionReadsExactlyTheRatingsOverall()
    {
        var player = Player(overall: 72, age: 27);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, PeakRules, PricedRules, SoftCap);

        Assert.Equal(72, valuation.Factor(ValuationFactorKind.Production)!.Reading);
    }

    [Fact]
    public void TrajectoryReadsFullWithinThePeakWindow()
    {
        var player = Player(overall: 60, age: 26);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, PeakRules, PricedRules, SoftCap);

        var trajectory = valuation.Factor(ValuationFactorKind.Trajectory)!;
        Assert.Equal(ValuationContribution.MaximumReading, trajectory.Reading);
        Assert.Equal("ai_valuation.trajectory_peak", trajectory.RuleCode);
    }

    [Fact]
    public void TrajectoryDropsBelowThePeakWindowForAYoungPlayer()
    {
        var player = Player(overall: 60, age: 20);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, PeakRules, PricedRules, SoftCap);

        var trajectory = valuation.Factor(ValuationFactorKind.Trajectory)!;
        Assert.Equal("ai_valuation.trajectory_ascending", trajectory.RuleCode);
        Assert.True(trajectory.Reading < ValuationContribution.MaximumReading);
    }

    [Fact]
    public void TrajectoryDropsPastThePeakWindowForAnAgingPlayer()
    {
        var player = Player(overall: 60, age: 36);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, PeakRules, PricedRules, SoftCap);

        var trajectory = valuation.Factor(ValuationFactorKind.Trajectory)!;
        Assert.Equal("ai_valuation.trajectory_declining", trajectory.RuleCode);
        Assert.True(trajectory.Reading < ValuationContribution.MaximumReading);
    }

    [Fact]
    public void TrajectoryReadsNeutralWhenNoDevelopmentCurveIsConfigured()
    {
        var player = Player(overall: 60, age: 40);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, DevelopmentRules.None, PricedRules, SoftCap);

        Assert.Equal("ai_valuation.trajectory_unconfigured", valuation.Factor(ValuationFactorKind.Trajectory)!.RuleCode);
    }

    [Fact]
    public void CostReadsNeutralWithNoContract()
    {
        var player = Player(overall: 60, age: 27);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, PeakRules, PricedRules, SoftCap);

        Assert.Equal("ai_valuation.cost_unpriced", valuation.Factor(ValuationFactorKind.Cost)!.RuleCode);
    }

    [Fact]
    public void CostReadsHighHeadroomForACheapContractAgainstTheCeiling()
    {
        var player = Player(overall: 60, age: 27);
        var contract = Contract(player.Id, salary: 5_000_000);

        var valuation = AssetValuationModel.ValuePlayer(player, contract, CurrentSeason, PeakRules, PricedRules, SoftCap);

        var cost = valuation.Factor(ValuationFactorKind.Cost)!;
        Assert.Equal("ai_valuation.cost", cost.RuleCode);
        Assert.True(cost.Reading > 50);
    }

    [Fact]
    public void CostReadsLowHeadroomForAContractAtTheCeiling()
    {
        var player = Player(overall: 60, age: 27);
        var contract = Contract(player.Id, salary: 25_000_000);

        var valuation = AssetValuationModel.ValuePlayer(player, contract, CurrentSeason, PeakRules, PricedRules, SoftCap);

        Assert.Equal(0, valuation.Factor(ValuationFactorKind.Cost)!.Reading);
    }

    [Fact]
    public void ControlReadsZeroWithNoContract()
    {
        var player = Player(overall: 60, age: 27);

        var valuation = AssetValuationModel.ValuePlayer(player, contract: null, CurrentSeason, PeakRules, PricedRules, SoftCap);

        var control = valuation.Factor(ValuationFactorKind.Control)!;
        Assert.Equal(ValuationContribution.MinimumReading, control.Reading);
        Assert.Equal("ai_valuation.control_unsigned", control.RuleCode);
    }

    [Fact]
    public void ControlReadsFullAtFourOrMoreSeasonsRemaining()
    {
        var player = Player(overall: 60, age: 27);
        var contract = MultiSeasonContract(player.Id, seasons: 4);

        var valuation = AssetValuationModel.ValuePlayer(player, contract, CurrentSeason, PeakRules, PricedRules, SoftCap);

        Assert.Equal(ValuationContribution.MaximumReading, valuation.Factor(ValuationFactorKind.Control)!.Reading);
    }

    [Fact]
    public void ControlReadsPartialForOneRemainingSeason()
    {
        var player = Player(overall: 60, age: 27);
        var contract = MultiSeasonContract(player.Id, seasons: 1);

        var valuation = AssetValuationModel.ValuePlayer(player, contract, CurrentSeason, PeakRules, PricedRules, SoftCap);

        var control = valuation.Factor(ValuationFactorKind.Control)!;
        Assert.True(control.Reading > 0 && control.Reading < ValuationContribution.MaximumReading);
    }

    [Fact]
    public void FirstRoundPickValuesHigherThanLaterRounds()
    {
        var firstRound = Pick(round: 1, draftYear: 2031);
        var secondRound = Pick(round: 2, draftYear: 2031);

        var firstValuation = AssetValuationModel.ValuePick(firstRound, CurrentSeason);
        var secondValuation = AssetValuationModel.ValuePick(secondRound, CurrentSeason);

        Assert.True(
            firstValuation.Factor(PickValuationFactorKind.Round)!.Reading
            > secondValuation.Factor(PickValuationFactorKind.Round)!.Reading);
    }

    [Fact]
    public void PickFurtherOutValuesLowerOnDistance()
    {
        var thisYear = Pick(round: 1, draftYear: 2031);
        var futurePick = Pick(round: 1, draftYear: 2034);

        var nearValuation = AssetValuationModel.ValuePick(thisYear, CurrentSeason);
        var farValuation = AssetValuationModel.ValuePick(futurePick, CurrentSeason);

        Assert.True(
            nearValuation.Factor(PickValuationFactorKind.YearsOut)!.Reading
            > farValuation.Factor(PickValuationFactorKind.YearsOut)!.Reading);
    }

    private static Player Player(int overall, int age) => Domain.Players.Player.Create(
        new PlayerId("PLAYER-1"),
        "Test Player",
        Position.SmallForward,
        new PlayerRating(overall),
        new DateOnly(CurrentSeason.Year - age, 10, 1),
        seasonsOfService: Math.Max(0, age - 19)).Value;

    private static Contract Contract(PlayerId playerId, long salary) => Domain.Contracts.Contract.Create(
        new ContractId("CONTRACT-1"),
        TeamId,
        playerId,
        [new ContractSeasonTerm(CurrentSeason, new Money(salary), new Money(salary))]).Value;

    private static Contract MultiSeasonContract(PlayerId playerId, int seasons) => Domain.Contracts.Contract.Create(
        new ContractId("CONTRACT-2"),
        TeamId,
        playerId,
        Enumerable.Range(0, seasons)
            .Select(offset => new ContractSeasonTerm(
                new Season(CurrentSeason.Year + offset),
                new Money(5_000_000),
                new Money(5_000_000)))).Value;

    private static DraftPick Pick(int round, int draftYear) => DraftPick.Create(
        new DraftPickId($"PICK-{round}-{draftYear}"),
        new LeagueId("LEAGUE-1"),
        new Season(draftYear),
        round,
        new FranchiseId("FRANCHISE-A")).Value;
}
