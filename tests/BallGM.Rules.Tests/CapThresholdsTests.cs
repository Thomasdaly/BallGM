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
        Assert.Equal(100, result.Value.SoftCap.SmallestUnits);
        Assert.Equal(150, result.Value.HardCap.SmallestUnits);
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
}
