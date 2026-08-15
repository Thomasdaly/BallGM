using BallGM.Domain.Common;

namespace BallGM.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void ConstructorRejectsNegativeAmounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1));
    }

    [Fact]
    public void ComparisonOperatorsOrderBySmallestUnits()
    {
        var smaller = new Money(100);
        var larger = new Money(200);

        Assert.True(smaller < larger);
        Assert.True(larger > smaller);
        Assert.True(smaller <= new Money(100));
        Assert.True(larger >= new Money(200));
    }

    [Fact]
    public void ZeroIsNonNegativeIdentity()
    {
        Assert.Equal(0, Money.Zero.SmallestUnits);
    }
}
