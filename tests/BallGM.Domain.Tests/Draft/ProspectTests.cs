using BallGM.Domain.Draft;
using BallGM.Domain.Players;

namespace BallGM.Domain.Tests.Draft;

public sealed class ProspectTests
{
    [Fact]
    public void CreateSucceedsWithValidAttributes()
    {
        var result = Prospect.Create(
            new ProspectId("prospect-001"),
            "Fictional Prospect",
            Position.PointGuard,
            new DateOnly(2005, 7, 1),
            new PlayerRating(overall: 65));

        Assert.True(result.IsSuccess);
        Assert.Equal("Fictional Prospect", result.Value.FullName);
        Assert.Equal(65, result.Value.TrueRating.Overall);
    }

    [Fact]
    public void CreateFailsWithNoBirthDate()
    {
        var result = Prospect.Create(
            new ProspectId("prospect-001"),
            "Fictional Prospect",
            Position.PointGuard,
            default,
            new PlayerRating(overall: 65));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "prospect.missing_birth_date");
    }

    [Fact]
    public void AgeOnComputesCompletedYears()
    {
        var prospect = Prospect.Create(
            new ProspectId("prospect-001"),
            "Fictional Prospect",
            Position.PointGuard,
            new DateOnly(2005, 7, 1),
            new PlayerRating(overall: 65)).Value;

        Assert.Equal(17, prospect.AgeOn(new DateOnly(2023, 6, 30)));
        Assert.Equal(18, prospect.AgeOn(new DateOnly(2023, 7, 1)));
    }
}
