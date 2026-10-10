using BallGM.Application.Players;
using BallGM.Domain.Players;

namespace BallGM.Application.Tests;

public sealed class PersonalityTests
{
    [Fact]
    public void PersonalityReadsTheHiddenLayerMostTellingFirst()
    {
        var player = new PlayerRating(60);

        Assert.Equal("Balanced", Personality.Describe(player));
        Assert.Equal("Model professional", Personality.Describe(player.With(HiddenAttribute.Professionalism, 85).With(HiddenAttribute.Ambition, 75)));
        Assert.Equal("Volatile", Personality.Describe(player.With(HiddenAttribute.Temperament, 10).With(HiddenAttribute.Ambition, 90)));
        Assert.Equal("Driven", Personality.Describe(player.With(HiddenAttribute.Ambition, 90)));
    }
}
