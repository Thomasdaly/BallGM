using BallGM.Domain.Franchises;

namespace BallGM.Domain.Tests;

public sealed class FranchiseTests
{
    [Fact]
    public void CreateSucceedsWithAName()
    {
        var result = Franchise.Create(new FranchiseId("franchise-001"), "Fictional City Athletics");

        Assert.True(result.IsSuccess);
        Assert.Equal("Fictional City Athletics", result.Value.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateThrowsForBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => Franchise.Create(new FranchiseId("franchise-001"), name));
    }
}
