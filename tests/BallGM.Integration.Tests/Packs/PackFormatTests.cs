using BallGM.Application.Packs;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Packs;
using BallGM.Mods.Checking;
using BallGM.Mods.Library;
using BallGM.Mods.Manifests;
using BallGM.Mods.Setups;

namespace BallGM.Integration.Tests.Packs;

/// <summary>Manifests, versions, safe paths, the library scan, player keys, and the built-in presets.</summary>
public sealed class PackFormatTests : IDisposable
{
    private readonly PackFixture _packs = new();

    public void Dispose() => _packs.Dispose();

    [Fact]
    public void Manifest_ReadsAHandWrittenFileWithCommentsAndTrailingCommas()
    {
        var manifest = PackManifestReader.Read("""
            {
              // a face pack
              "schemaVersion": 2,
              "id": "someone.faces",
              "name": "Faces",
              "version": "1.2.3",
              "kind": "faces",
              "targets": [{ "pack": "someone.league", "versions": "^2.0.0" }],
            }
            """);

        Assert.True(manifest.IsSuccess);
        Assert.Equal(new PackVersion(1, 2, 3), manifest.Value.Version);
        Assert.Equal(PackKind.Faces, manifest.Value.Kind);
        Assert.Equal("faces", manifest.Value.Content.FolderFor(PackKind.Faces));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "faces" }""", PackManifestReader.UnsupportedSchemaCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "NoDots", "name": "x", "version": "1.0.0", "kind": "faces" }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0", "kind": "faces" }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "skin" }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "1" }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "ruleset", "targets": [{ "pack": "c.d" }] }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "faces", "fidelity": [] }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "faces", "requires": [{ "pack": "a.b" }] }""", PackManifestReader.InvalidFieldCode)]
    [InlineData("""{ "schemaVersion": 2, "id": "a.b", "name": "x", "version": "1.0.0", "kind": "faces", "script": "rm -rf" }""", PackManifestReader.MalformedCode)]
    public void Manifest_RefusesWhatItCannotTrustWithACode(string json, string code)
    {
        var manifest = PackManifestReader.Read(json);

        Assert.True(manifest.IsFailure);
        Assert.Equal(code, manifest.Errors[0].Code);
    }

    [Theory]
    [InlineData("*", "0.0.1", true)]
    [InlineData("1.4.0", "1.4.0", true)]
    [InlineData("1.4.0", "1.4.1", false)]
    [InlineData("^1.4.0", "1.9.9", true)]
    [InlineData("^1.4.0", "2.0.0", false)]
    [InlineData("^1.4.0", "1.3.9", false)]
    [InlineData(">=1.0.0 <2.0.0", "1.99.0", true)]
    [InlineData(">1.0.0 <=2.0.0", "1.0.0", false)]
    public void VersionRange_AdmitsExactlyWhatItSays(string range, string version, bool admitted)
    {
        Assert.True(VersionRange.TryParse(range, out var parsed));
        Assert.True(PackVersion.TryParse(version, out var parsedVersion));
        Assert.Equal(admitted, parsed.Admits(parsedVersion));
    }

    [Theory]
    [InlineData("~1.0.0")]
    [InlineData("1.x")]
    [InlineData("^1.0.0 || ^2.0.0")]
    [InlineData("1.0.0-beta")]
    public void VersionRange_RefusesSpellingsOutsideTheGrammar(string range) =>
        Assert.False(VersionRange.TryParse(range, out _));

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("faces/../../outside.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    public void PackPaths_RefusesAnythingThatLeavesThePackFolder(string path) =>
        Assert.False(PackPaths.TryResolve(_packs.UserFolder, path, out _, out _));

    [Fact]
    public void PackPaths_RefusesALinkThatLeadsOutOfThePack()
    {
        var pack = _packs.Pack("test.faces", "faces");
        var outside = Path.Combine(_packs.Root, "outside.png");
        PackFixture.Png(outside);
        Directory.CreateDirectory(Path.Combine(pack, "faces"));
        File.CreateSymbolicLink(Path.Combine(pack, "faces", "p0.png"), outside);

        Assert.False(PackPaths.TryResolve(pack, "faces/p0.png", out _, out var reason));
        Assert.Contains("link", reason, StringComparison.Ordinal);
        Assert.True(PackPaths.TryResolve(pack, "faces/other.png", out _, out _));
    }

    [Fact]
    public void Library_TwoPacksWithOneIdKeepTheHigherVersionAndSaySo()
    {
        _packs.Pack("test.faces", "faces", version: "1.0.0", folder: "old-faces");
        _packs.Pack("test.faces", "faces", version: "1.1.0", folder: "new-faces");

        var library = _packs.Library();

        Assert.Equal(new PackVersion(1, 1, 0), library.Find("test.faces")!.Manifest.Version);
        Assert.Contains(library.Findings, finding => finding.Code == "library.duplicate_pack_id");
    }

    [Fact]
    public void Library_ExplainsFoldersThatAreNotPacksAndRefusesReservedIds()
    {
        Directory.CreateDirectory(Path.Combine(_packs.UserFolder, "dropped-faces"));
        _packs.Pack("builtin.ruleset.mine", "ruleset", folder: "impostor");
        Directory.CreateDirectory(Path.Combine(_packs.UserFolder, "broken"));
        File.WriteAllText(Path.Combine(_packs.UserFolder, "broken", PackManifest.FileName), "{ not json");

        var library = _packs.Library();

        Assert.Contains(library.Findings, finding => finding.Code == "library.folder_without_manifest" && finding.Message.Contains("dropped-faces", StringComparison.Ordinal));
        Assert.Contains(library.Findings, finding => finding.Code == "library.reserved_pack_id");
        Assert.Contains(library.Findings, finding => finding.Code == PackManifestReader.MalformedCode);
        Assert.Null(library.Find("builtin.ruleset.mine"));
    }

    [Theory]
    [InlineData("Aldo Brenner", "aldo-brenner-2001-03-14")]
    [InlineData("José Ñúñez-Ortiz", "jose-nunez-ortiz-2001-03-14")]
    [InlineData("  O'Neil  Jr. ", "o-neil-jr-2001-03-14")]
    [InlineData("???", "player-2001-03-14")]
    public void Keys_DeriveAFileSafeKeyFromNameAndBirthDate(string name, string key)
    {
        var derived = LeaguePackKeys.Derive(name, new DateOnly(2001, 3, 14));

        Assert.Equal(key, derived);
        Assert.True(LeaguePackKeys.IsValid(derived));
    }

    [Fact]
    public void Keys_ALeaguePackStatingTheSameKeyTwiceDoesNotLoad()
    {
        var directory = _packs.League("test.league");
        var path = Path.Combine(directory, "league.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"key\": \"p1\"", "\"key\": \"p0\"", StringComparison.Ordinal));

        var loaded = new LeaguePackDataSource(path, PackFixture.MajorRuleset.Value).Load();

        Assert.Contains(loaded.Errors, error => error.Code == "league_pack.duplicate_player_key");
    }

    [Fact]
    public void Keys_ALeaguePackStatingAnUnsafeKeyDoesNotLoad()
    {
        var directory = _packs.League("test.league");
        var path = Path.Combine(directory, "league.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"key\": \"p1\"", "\"key\": \"../p1\"", StringComparison.Ordinal));

        var loaded = new LeaguePackDataSource(path, PackFixture.MajorRuleset.Value).Load();

        Assert.Contains(loaded.Errors, error => error.Message.Contains("not a valid key", StringComparison.Ordinal));
    }

    [Fact]
    public void Keys_EveryLoadedTeamAndPlayerIsKeyedAndTheKeysSurviveAReload()
    {
        var path = Path.Combine(_packs.League("test.league"), "league.json");

        var first = new LeaguePackDataSource(path, PackFixture.MajorRuleset.Value).Load().Value;
        var second = new LeaguePackDataSource(path, PackFixture.MajorRuleset.Value).Load().Value;

        Assert.Equal(first.Players.Count, first.Artwork.PlayerKeys.Count);
        Assert.Equal(first.Teams.Count, first.Artwork.TeamKeys.Count);
        Assert.Equal(first.Artwork.PlayerKeys.Values.Order(StringComparer.Ordinal), second.Artwork.PlayerKeys.Values.Order(StringComparer.Ordinal));
        Assert.Contains("p0", first.Artwork.PlayerKeys.Values);
    }

    [Fact]
    public void Presets_EveryBuiltInRulesetLoadsAndStatesItsFidelity()
    {
        var library = PackLibrary.Scan([new PackRoot(PackFolders.BuiltIn, PackOrigin.BuiltIn)]);
        var presets = library.OfKind(PackKind.Ruleset);

        Assert.Empty(library.Findings);
        Assert.Equal(5, presets.Count);
        Assert.All(presets, preset =>
        {
            var probed = new PackContentProbe().ProbeRuleset(File.ReadAllText(Path.Combine(preset.Directory, "ruleset.json")));
            Assert.True(probed.Loaded, preset.Id + ": " + string.Join("; ", probed.Findings.Select(finding => finding.Message)));
            Assert.NotEmpty(preset.Manifest.Fidelity);
        });
    }

    [Theory]
    [InlineData("builtin.setup.na-major-fictional")]
    [InlineData("builtin.setup.na-womens-fictional")]
    [InlineData("builtin.setup.euro-continental-fictional")]
    [InlineData("builtin.setup.iberian-domestic-fictional")]
    [InlineData("builtin.setup.australasian-fictional")]
    public void Presets_EveryBuiltInSetupPlaysAWholeSeasonCleanly(string setupId)
    {
        var library = PackLibrary.Scan([new PackRoot(PackFolders.BuiltIn, PackOrigin.BuiltIn)]);
        var setup = SetupLocator.Locate(setupId, library).Value;

        var report = new PackChecker(new PackContentProbe()).Check(setup, library);

        Assert.True(report.Verdict == PackCheckVerdict.Stable, PackCheckReportText.Format(report, includeInfo: false));
        Assert.Contains(report.Findings, finding => finding.Code == "season.smoke_test_passed");
    }

    [Fact]
    public void Presets_TheMajorPresetCarriesThe2026To27Figures()
    {
        var ruleset = PackFixture.MajorRuleset.Value;

        Assert.Equal(164_961_000, ruleset.CapThresholds.SoftCap!.SmallestUnits);
        Assert.Equal(200_428_000, ruleset.CapThresholds.LuxuryTax!.SmallestUnits);
        Assert.Equal(209_015_000, ruleset.CapThresholds.FirstApron!.SmallestUnits);
        Assert.Equal(221_686_000, ruleset.CapThresholds.SecondApron!.SmallestUnits);
        Assert.Equal(148_465_000, ruleset.CapThresholds.PayrollFloor!.SmallestUnits);
        Assert.Null(ruleset.CapThresholds.HardCap);
        Assert.Equal(41_240_250, ruleset.NegotiationRules.CompensationCeiling.CeilingFor(0, ruleset.CapThresholds.SoftCap)!.SmallestUnits);
        Assert.Equal(57_736_350, ruleset.NegotiationRules.CompensationCeiling.CeilingFor(10, ruleset.CapThresholds.SoftCap)!.SmallestUnits);
        Assert.Equal(1_357_763, ruleset.NegotiationRules.CompensationFloor.FloorFor(0)!.SmallestUnits);
        Assert.Equal(3_876_529, ruleset.NegotiationRules.CompensationFloor.FloorFor(14)!.SmallestUnits);
        Assert.Equal(15_044_000, ruleset.NegotiationRules.StandardOverCapAllowance!.SmallestUnits);
        Assert.Equal(82, ruleset.RegularSeasonGameCount);
    }

    [Fact]
    public void Probe_AFlatLeagueWithAPostseasonLoadsItsCapSheet()
    {
        // Regression: the cap ledger rebuilt the ruleset assuming conferences, so every flat league
        // with a postseason refused to load ("8 qualifiers per conference plus a final"). The pack
        // checker's first run over the single-table presets found it.
        var library = PackLibrary.Scan([new PackRoot(PackFolders.BuiltIn, PackOrigin.BuiltIn)]);
        var ruleset = File.ReadAllText(Path.Combine(library.Find("builtin.ruleset.euro-continental")!.Directory, "ruleset.json"));

        var findings = new PackContentProbe().SmokeTestSeason(new GeneratedLeagueSource(1, 20, 0, 1), ruleset, seed: 3);

        Assert.DoesNotContain(findings, finding => finding.Severity == PackFindingSeverity.Error);
    }
}
