using BallGM.Infrastructure.Saves;

namespace BallGM.Integration.Tests;

public sealed class LeagueSaveSerializerTests
{
    [Fact]
    public void RoundTripPreservesVersionedLeagueSaveEnvelope()
    {
        var serializer = new LeagueSaveSerializer();
        var saveEnvelope = new LeagueSaveEnvelope(
            schemaVersion: LeagueSaveEnvelope.CurrentSchemaVersion,
            leagueName: "Metro Hardwood Association",
            currentSeasonYear: 2032);

        var json = serializer.Serialize(saveEnvelope);
        var roundTripped = serializer.Deserialize(json);

        Assert.Equal(saveEnvelope, roundTripped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LeagueSaveEnvelopeRejectsNonPositiveSchemaVersion(int schemaVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LeagueSaveEnvelope(
                schemaVersion,
                leagueName: "Metro Hardwood Association",
                currentSeasonYear: 2032));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LeagueSaveEnvelopeRejectsBlankLeagueName(string leagueName)
    {
        Assert.Throws<ArgumentException>(() =>
            new LeagueSaveEnvelope(
                LeagueSaveEnvelope.CurrentSchemaVersion,
                leagueName,
                currentSeasonYear: 2032));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LeagueSaveEnvelopeRejectsNonPositiveSeasonYear(int currentSeasonYear)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LeagueSaveEnvelope(
                LeagueSaveEnvelope.CurrentSchemaVersion,
                leagueName: "Metro Hardwood Association",
                currentSeasonYear));
    }
}
