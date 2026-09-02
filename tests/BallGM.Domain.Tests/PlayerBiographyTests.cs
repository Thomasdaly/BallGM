using BallGM.Domain.Players;

namespace BallGM.Domain.Tests;

public sealed class PlayerBiographyTests
{
    [Fact]
    public void UnknownHasNoDraftRecord()
    {
        Assert.False(PlayerBiography.Unknown.WasDrafted);
    }

    [Fact]
    public void WasDraftedIsTrueOnceADraftSeasonIsRecorded()
    {
        var biography = new PlayerBiography("Harbourline", "Verdanmoor Institute", 2028, 1, 4);

        Assert.True(biography.WasDrafted);
    }
}
