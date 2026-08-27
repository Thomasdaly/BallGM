using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class CapThresholdsTests
{
    [Fact]
    public void CreateSucceedsWithNonDecreasingThresholds()
    {
        var result = CapThresholds.Create(
            softCap: new Money(100),
            luxuryTax: new Money(120),
            firstApron: new Money(130),
            secondApron: new Money(140),
            hardCap: new Money(150));

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value.SoftCap?.SmallestUnits);
        Assert.Equal(150, result.Value.HardCap?.SmallestUnits);
    }

    [Fact]
    public void CreateReturnsStructuredFailureWhenThresholdsAreOutOfOrderInsteadOfThrowing()
    {
        var result = CapThresholds.Create(
            softCap: new Money(150),
            luxuryTax: new Money(120),
            firstApron: new Money(130),
            secondApron: new Money(140),
            hardCap: new Money(150));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ruleset.cap_thresholds_out_of_order", error.Code);
    }

    [Fact]
    public void CreateAllowsEqualAdjacentThresholds()
    {
        var result = CapThresholds.Create(
            softCap: new Money(100),
            luxuryTax: new Money(100),
            firstApron: new Money(100),
            secondApron: new Money(100),
            hardCap: new Money(100));

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A league with no cap system is a configuration, not a set of zeroes. The distinction is the
    /// whole point of the type: zeroes satisfy the ordering check and leave every team over every
    /// line.
    /// </summary>
    [Fact]
    public void UncappedConfiguresNothingAtAll()
    {
        Assert.True(CapThresholds.Uncapped.IsUncapped);
        Assert.Empty(CapThresholds.Uncapped.Configured);
        Assert.Null(CapThresholds.Uncapped.SoftCap);
    }

    [Fact]
    public void APartiallyConfiguredLeagueOrdersOnlyTheThresholdsItHas()
    {
        var result = CapThresholds.Create(
            softCap: new Money(100),
            luxuryTax: new Money(120));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsUncapped);
        Assert.Equal(
            [CapThresholdKind.SoftCap, CapThresholdKind.LuxuryTax],
            result.Value.Configured.Select(entry => entry.Kind));
    }

    /// <summary>
    /// The ordering check applies to the present thresholds in the same fixed sequence, so a
    /// contradiction between two non-adjacent lines is still caught when the ones between them are
    /// absent.
    /// </summary>
    [Fact]
    public void OrderingIsCheckedAcrossAbsentThresholds()
    {
        var result = CapThresholds.Create(
            softCap: new Money(150),
            hardCap: new Money(100));

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.cap_thresholds_out_of_order", Assert.Single(result.Errors).Code);
    }

    /// <summary>The floor extends the chain downward rather than being inserted into it.</summary>
    [Fact]
    public void ThePayrollFloorIsTheFirstLinkInTheChain()
    {
        var result = CapThresholds.Create(
            payrollFloor: new Money(90),
            softCap: new Money(100),
            hardCap: new Money(150));

        Assert.True(result.IsSuccess);
        Assert.Equal(CapThresholdKind.PayrollFloor, result.Value.Configured[0].Kind);
        Assert.Equal(90, result.Value.PayrollFloor?.SmallestUnits);
    }

    [Fact]
    public void APayrollFloorAboveTheSoftCapIsRejected()
    {
        var result = CapThresholds.Create(
            payrollFloor: new Money(120),
            softCap: new Money(100));

        Assert.True(result.IsFailure);
        Assert.Equal("ruleset.cap_thresholds_out_of_order", Assert.Single(result.Errors).Code);
    }

    /// <summary>A floor with nothing above it is a coherent league: spend at least this much.</summary>
    [Fact]
    public void ALeagueMayConfigureAFloorAndNoCeiling()
    {
        var result = CapThresholds.Create(payrollFloor: new Money(90));

        Assert.True(result.IsSuccess);
        Assert.Equal(CapThresholdKind.PayrollFloor, Assert.Single(result.Value.Configured).Kind);
    }
}
