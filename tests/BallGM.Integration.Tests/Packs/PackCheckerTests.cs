using BallGM.Application.Packs;
using BallGM.Infrastructure.Packs;
using BallGM.Mods.Checking;
using BallGM.Mods.Loading;
using BallGM.Mods.Setups;

namespace BallGM.Integration.Tests.Packs;

/// <summary>
/// The pack checker end to end: a setup, packs on disk, the real content probe, and the report —
/// then the league the loader builds from it. Each test states one stability rule: what blocks, what
/// only warns, and that cosmetics never block.
/// </summary>
public sealed class PackCheckerTests : IDisposable
{
    /// <summary>The major preset, shrunk to fit a four-team flat test league.</summary>
    private static readonly Dictionary<string, object?> SmallLeagueSettings = new()
    {
        ["minimumRosterPlayers"] = 12,
        ["regularSeasonGameCount"] = 30,
        ["postseasonQualifyingTeamsPerConference"] = 2,
        ["postseasonSeriesLengths"] = new[] { 7 },
        ["draftLotteryWeights"] = new[] { 250, 200 },
    };

    private readonly PackFixture _packs = new();
    private readonly PackChecker _checker = new(new PackContentProbe());

    public void Dispose() => _packs.Dispose();

    [Fact]
    public void Check_AFullSetupThatLoadsAndPlaysIsStable()
    {
        _packs.League("test.league");
        var report = Check(Setup(), PackCheckOptions.Full);

        Assert.Equal(PackCheckVerdict.Stable, report.Verdict);
        Assert.Contains(report.Findings, finding => finding.Code == "season.smoke_test_passed");
        Assert.Equal([PackCheckStage.Settings, PackCheckStage.League, PackCheckStage.Season], report.LoadPlan.Select(step => step.Stage));
        Assert.NotNull(report.Plan);
    }

    [Fact]
    public void Check_BlocksWhenTheSetupNamesAPackThatIsNotInstalled()
    {
        var report = Check(Setup(league: "test.missing-league"));

        Assert.Equal(PackCheckVerdict.Blocked, report.Verdict);
        var finding = Assert.Single(report.Errors);
        Assert.Equal("setup.pack_not_installed", finding.Code);
        Assert.Null(report.Plan);
    }

    [Fact]
    public void Check_BlocksWhenTheInstalledVersionIsOutsideTheSetupsRange()
    {
        _packs.League("test.league", version: "1.4.0");
        var report = Check(Setup(leagueVersions: "^2.0.0"));

        Assert.Equal("setup.version_mismatch", Assert.Single(report.Errors).Code);
    }

    [Fact]
    public void Check_BlocksWhenAPackIsUsedAsTheWrongKind()
    {
        _packs.Pack("test.faces", "faces");
        var report = Check(Setup(league: "test.faces"));

        Assert.Equal("setup.wrong_pack_kind", Assert.Single(report.Errors).Code);
    }

    [Fact]
    public void Check_BlocksOnAMissingDependencyAndSaysWhetherItIsInstalled()
    {
        _packs.League("test.league");
        _packs.Pack("test.base-faces", "faces");
        _packs.Pack("test.faces", "faces", extra: new { requires = new[] { new { pack = "test.base-faces" } } });

        var report = Check(Setup(cosmetics: ["test.faces"]));

        var finding = Assert.Single(report.Errors);
        Assert.Equal("dependencies.missing", finding.Code);
        Assert.Contains("installed", finding.Message, StringComparison.Ordinal);
        Assert.Equal("Add 'test.base-faces' to the setup.", finding.Fix);
    }

    [Fact]
    public void Check_BlocksTwoPacksThatDeclareAConflict()
    {
        _packs.League("test.league");
        _packs.Pack("test.logos-a", "logos", extra: new { conflicts = new[] { new { pack = "test.logos-b" } } });
        _packs.Pack("test.logos-b", "logos");

        var report = Check(Setup(cosmetics: ["test.logos-a", "test.logos-b"]));

        Assert.Equal("dependencies.conflict", Assert.Single(report.Errors).Code);
    }

    [Fact]
    public void Check_LoadsARequiredCosmeticBeforeThePackThatBuildsOnIt()
    {
        _packs.League("test.league");
        var basePack = _packs.Pack("test.base-faces", "faces");
        var update = _packs.Pack("test.faces-update", "faces", extra: new { requires = new[] { new { pack = "test.base-faces" } } });
        PackFixture.Png(Path.Combine(basePack, "faces", "p0.png"));
        PackFixture.Png(Path.Combine(update, "faces", "p0.png"));

        var report = Check(Setup(cosmetics: ["test.faces-update", "test.base-faces"]));

        Assert.Contains(report.Findings, finding => finding.Code == "dependencies.load_order_adjusted");
        Assert.Equal(Path.Combine(update, "faces", "p0.png"), report.Plan!.PlayerPortraits["p0"]);
        Assert.Contains(report.Findings, finding => finding.Code == "cosmetics.overrides" && finding.PackId == "test.faces-update");
    }

    [Fact]
    public void Check_AnOverrideThatTheLeagueCannotLoadUnderBlocksAtTheLeagueStage()
    {
        _packs.League("test.league");
        var settings = new Dictionary<string, object?>(SmallLeagueSettings) { ["maximumRosterPlayers"] = 11, ["minimumRosterPlayers"] = 10 };

        var report = Check(Setup(settings: settings));

        Assert.Equal(PackCheckVerdict.Blocked, report.Verdict);
        Assert.Contains(report.Errors, finding => finding.Stage == PackCheckStage.League);
    }

    [Fact]
    public void Check_AnUnknownSettingIsAnErrorThatSuggestsTheNearestOne()
    {
        _packs.League("test.league");
        var settings = new Dictionary<string, object?>(SmallLeagueSettings) { ["softcap"] = 1 };

        var report = Check(Setup(settings: settings));

        var finding = Assert.Single(report.Errors);
        Assert.Equal("settings.unknown_setting", finding.Code);
        Assert.Equal("Did you mean 'softCap'?", finding.Fix);
    }

    [Fact]
    public void Check_ASeasonThatCannotStartBlocks()
    {
        _packs.League("test.league");

        // A ruleset the reader accepts, a league that loads, and still no season: thirty games each
        // cannot be fitted into ten days. Only playing it finds that out.
        var settings = new Dictionary<string, object?>(SmallLeagueSettings)
        {
            ["regularSeasonDays"] = 10,
            ["playoffEligibilityCutoffDay"] = null,
            ["inSeasonSigningWindowOpensDay"] = null,
            ["inSeasonSigningWindowClosesDay"] = null,
        };
        var report = Check(Setup(settings: settings), PackCheckOptions.Full);

        Assert.Equal(PackCheckVerdict.Blocked, report.Verdict);
        var finding = Assert.Single(report.Errors);
        Assert.Equal((PackCheckStage.Season, "schedule.not_enough_days"), (finding.Stage, finding.Code));
        Assert.Null(report.Plan);
    }

    [Fact]
    public void Check_BrokenAndUnmatchedCosmeticsWarnButNeverBlock()
    {
        _packs.League("test.league");
        var faces = _packs.Pack("test.faces", "faces", extra: new { targets = new[] { new { pack = "test.league" } } });
        PackFixture.Png(Path.Combine(faces, "faces", "p0.png"));
        File.WriteAllBytes(Path.Combine(faces, "faces", "p1.png"), []);
        PackFixture.Png(Path.Combine(faces, "faces", "nobody-in-this-league.png"));
        File.WriteAllText(Path.Combine(faces, "faces", "readme.txt"), "not an image");

        var report = Check(Setup(cosmetics: ["test.faces"]));

        Assert.Equal(PackCheckVerdict.StableWithWarnings, report.Verdict);
        Assert.Contains(report.Warnings, finding => finding.Code == "cosmetics.image_empty");
        Assert.Contains(report.Warnings, finding => finding.Code == "cosmetics.unmatched_keys");
        Assert.Contains(report.Warnings, finding => finding.Code == "cosmetics.not_an_image");
        Assert.Equal(["p0"], report.Plan!.PlayerPortraits.Keys);
        Assert.Equal(1, report.Coverage.Single(item => item.Kind == "faces").Covered);
    }

    [Fact]
    public void Check_AFaceTooWideToFrameFallsBackButAWideLogoIsLetterboxed()
    {
        _packs.League("test.league");
        var faces = _packs.Pack("test.faces", "faces");
        var logos = _packs.Pack("test.logos", "logos");
        PackFixture.Png(Path.Combine(faces, "faces", "p0.png"), width: 900, height: 300);
        PackFixture.Jpeg(Path.Combine(logos, "logos", "G00.jpg"), width: 1200, height: 300);

        var report = Check(Setup(cosmetics: ["test.faces", "test.logos"]));

        Assert.Contains(report.Warnings, finding => finding.Code == "cosmetics.face_aspect");
        Assert.Contains(report.Findings, finding => finding.Code == "cosmetics.logo_letterboxed");
        Assert.Empty(report.Plan!.PlayerPortraits);
        Assert.Single(report.Plan.TeamLogos);
    }

    [Fact]
    public void Check_AMapEntryThatClimbsOutOfThePackIsRefusedBeforeAnyFileIsOpened()
    {
        _packs.League("test.league");
        var faces = _packs.Pack("test.faces", "faces", extra: new { content = new { map = "map.json" } });
        File.WriteAllText(Path.Combine(faces, "map.json"), """{ "p0": "../../../secrets.png", "p1": "faces/ok.png" }""");
        PackFixture.Png(Path.Combine(faces, "faces", "ok.png"));

        var report = Check(Setup(cosmetics: ["test.faces"]));

        Assert.Contains(report.Warnings, finding => finding.Code == "cosmetics.unsafe_path");
        Assert.Equal(["p1"], report.Plan!.PlayerPortraits.Keys);
    }

    [Fact]
    public void Check_ACosmeticMadeForAnotherLeagueWarnsAndStillMatchesWhatItCan()
    {
        _packs.League("test.league");
        var colours = _packs.Pack("test.colours", "colours", extra: new { targets = new[] { new { pack = "test.other-league", versions = "^1.0.0" } } });
        File.WriteAllText(Path.Combine(colours, "colours.json"), """{ "G00": { "primary": "#112233" }, "G01": { "primary": "red" } }""");

        var report = Check(Setup(cosmetics: ["test.colours"]));

        Assert.Contains(report.Warnings, finding => finding.Code == "dependencies.cosmetic_targets_other_league");
        Assert.Contains(report.Warnings, finding => finding.Code == "cosmetics.invalid_colour");
        Assert.Equal("#112233", report.Plan!.TeamColours["G00"].Primary);
    }

    [Fact]
    public void Loader_AppliesTheCheckedCosmeticsToAFreshLoadByKey()
    {
        _packs.League("test.league");
        var faces = _packs.Pack("test.faces", "faces");
        var logos = _packs.Pack("test.logos", "logos", extra: new { content = new { leagueLogo = "league.png" } });
        PackFixture.Png(Path.Combine(faces, "faces", "p1.png"));
        PackFixture.Png(Path.Combine(logos, "logos", "G02.png"));
        PackFixture.Png(Path.Combine(logos, "league.png"));
        var report = Check(Setup(cosmetics: ["test.faces", "test.logos"]));

        var loaded = new CheckedLeagueDataSource(report, new PackContentProbe()).Load();

        Assert.True(loaded.IsSuccess);
        var artwork = loaded.Value.Artwork;
        var playerId = artwork.PlayerKeys.Single(entry => entry.Value == "p1").Key;
        var teamId = artwork.TeamKeys.Single(entry => entry.Value == "G02").Key;
        Assert.Equal(Path.Combine(faces, "faces", "p1.png"), artwork.PlayerPortraits[playerId]);
        Assert.Equal(Path.Combine(logos, "logos", "G02.png"), artwork.TeamLogos[teamId]);
        Assert.Equal(Path.Combine(logos, "league.png"), artwork.LeagueLogo);
    }

    [Fact]
    public void Loader_RefusesABlockedSetupWithTheCheckersOwnErrors()
    {
        var report = Check(Setup(league: "test.missing-league"));

        var loaded = new CheckedLeagueDataSource(report, new PackContentProbe()).Load();

        Assert.True(loaded.IsFailure);
        Assert.Equal("setup.pack_not_installed", Assert.Single(loaded.Errors).Code);
    }

    [Fact]
    public void Check_ReportsPlayersKeyedByNameAndBirthDate()
    {
        _packs.League("test.league", statedKeys: 2);

        var report = Check(Setup());

        var finding = Assert.Single(report.Findings, finding => finding.Code == "league.derived_player_keys");
        Assert.StartsWith("46 players state no key", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matcher_ListsTheLeaguesThatFitTheSettingsAndTheCosmeticsMadeForEach()
    {
        _packs.League("test.small-league", teams: 4);
        _packs.League("test.big-roster", teams: 2);
        _packs.Pack("test.faces", "faces", extra: new { targets = new[] { new { pack = "test.small-league" } } });
        var library = _packs.Library();
        var ruleset = File.ReadAllText(Path.Combine(library.Find("builtin.ruleset.na-major")!.Directory, "ruleset.json"));

        var fits = PackMatcher.LeaguesFor(ruleset, library, new PackContentProbe());

        Assert.Equal(["test.big-roster", "test.small-league"], fits.Select(fit => fit.Pack.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["test.faces"], PackMatcher.CosmeticsFor(library.Find("test.small-league")!, library).Select(pack => pack.Id));
        Assert.Empty(PackMatcher.CosmeticsFor(library.Find("test.big-roster")!, library));
    }

    private PackCheckReport Check(string setupPath, PackCheckOptions? options = null)
    {
        var library = _packs.Library();
        var setup = SetupLocator.Locate(setupPath, library);
        Assert.True(setup.IsSuccess, string.Join("; ", setup.Errors.Select(error => error.Message)));
        return _checker.Check(setup.Value, library, options ?? new PackCheckOptions(PlaySeason: false));
    }

    private string Setup(
        string league = "test.league",
        string? leagueVersions = null,
        IReadOnlyList<string>? cosmetics = null,
        IReadOnlyDictionary<string, object?>? settings = null) =>
        _packs.Setup(new
        {
            schemaVersion = LeagueSetup.CurrentSchemaVersion,
            name = "Test setup",
            ruleset = new { pack = "builtin.ruleset.na-major" },
            settings = settings ?? SmallLeagueSettings,
            league = new { pack = league, versions = leagueVersions },
            cosmetics = (cosmetics ?? []).Select(id => new { pack = id }).ToList(),
        });

}
