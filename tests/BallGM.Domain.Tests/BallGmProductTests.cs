using BallGM.Domain;

namespace BallGM.Domain.Tests;

public sealed class BallGmProductTests
{
    [Fact]
    public void ProductNameUsesAcceptedProjectName()
    {
        Assert.Equal("BallGM", BallGmProduct.Name);
    }
}
