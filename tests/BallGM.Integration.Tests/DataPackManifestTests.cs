using BallGM.Mods.Manifests;

namespace BallGM.Integration.Tests;

public sealed class DataPackManifestTests
{
    [Fact]
    public void DataPackManifestAcceptsCurrentSchemaVersion()
    {
        var manifest = new DataPackManifest(
            schemaVersion: DataPackManifest.CurrentSchemaVersion,
            name: "BallGM sample data pack");

        Assert.Equal(DataPackManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal("BallGM sample data pack", manifest.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DataPackManifestRejectsNonPositiveSchemaVersion(int schemaVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DataPackManifest(schemaVersion, name: "BallGM sample data pack"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void DataPackManifestRejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new DataPackManifest(DataPackManifest.CurrentSchemaVersion, name));
    }
}
