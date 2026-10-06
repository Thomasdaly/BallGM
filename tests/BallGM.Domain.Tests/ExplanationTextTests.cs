using BallGM.Domain.Common;
using BallGM.Domain.Players;

namespace BallGM.Domain.Tests;

public sealed class ExplanationTextTests
{
    [Theory]
    [InlineData(10_000_000, "$10.0M")]
    [InlineData(2_537_526, "$2.54M")]
    [InlineData(40_000, "$0.04M")]
    [InlineData(0, "$0.0M")]
    [InlineData(-3_100_000, "-$3.1M")]
    public void Money_IsWrittenInMillionsAGmReads(long smallestUnits, string expected)
    {
        Assert.Equal(expected, ExplanationText.Money(smallestUnits));
    }

    [Theory]
    [InlineData(1, "1 season")]
    [InlineData(0, "0 seasons")]
    [InlineData(4, "4 seasons")]
    public void Count_TakesARealPluralRatherThanParentheses(int count, string expected)
    {
        Assert.Equal(expected, ExplanationText.Count(count, "season"));
    }

    [Fact]
    public void Count_UsesAStatedPluralWhereAddingSIsWrong()
    {
        Assert.Equal("2 series", ExplanationText.Count(2, "series", "series"));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(102, "102nd")]
    public void Ordinal_TakesTheRightSuffix(int value, string expected)
    {
        Assert.Equal(expected, ExplanationText.Ordinal(value));
    }

    [Fact]
    public void Position_IsAWordNotAnEnumName()
    {
        Assert.Equal("point guard", ExplanationText.Position(Position.PointGuard));
        Assert.Equal("centre", ExplanationText.Position(Position.Center));
    }
}
