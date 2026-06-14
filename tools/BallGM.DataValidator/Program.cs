using BallGM.Mods.Manifests;

var manifest = new DataPackManifest(
    schemaVersion: DataPackManifest.CurrentSchemaVersion,
    name: "BallGM sample data pack");

Console.WriteLine($"BallGM data validator placeholder loaded schema v{manifest.SchemaVersion}: {manifest.Name}");
