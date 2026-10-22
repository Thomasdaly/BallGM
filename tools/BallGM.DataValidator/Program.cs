using System.Globalization;
using System.Text.Json;
using BallGM.Application.Packs;
using BallGM.Domain.Common;
using BallGM.Infrastructure.Packs;
using BallGM.Mods.Checking;
using BallGM.Mods.Library;
using BallGM.Mods.Manifests;
using BallGM.Mods.Settings;
using BallGM.Mods.Setups;

// ballgm-packs: the pack checker outside the game. Everything it prints is what the game would say
// about the same packs, because it runs the same checker against the same probe.
//
//   check <setup-file|setup-pack-id> [--packs DIR]... [--no-season] [--no-images] [--strict] [--quiet]
//   list [--packs DIR]...
//   presets [--packs DIR]...
//   settings [GROUP]
//   match <ruleset-pack-id> [--packs DIR]... [--league ID --write FILE]
//   keys <league-pack-id> [--ruleset ID] [--packs DIR]...
//   init <folder> --kind KIND --id ID --name NAME [--targets ID]
//
// Exit codes: 0 stable (or stable with warnings), 1 warnings under --strict, 2 blocked, 64 usage.
const int Usage = 64;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return args.Length == 0 ? Usage : 0;
}

var command = args[0];
var rest = args.Skip(1).ToList();
var extraFolders = Values(rest, "--packs");
var probe = new PackContentProbe();

try
{
    return command switch
    {
        "check" => Check(rest),
        "list" => List(),
        "presets" => Presets(),
        "settings" => Settings(rest),
        "match" => Match(rest),
        "keys" => Keys(rest),
        "init" => Init(rest),
        _ => Fail($"Unknown command '{command}'."),
    };
}
catch (ArgumentException exception)
{
    return Fail(exception.Message);
}

int Check(List<string> options)
{
    var target = Positional(options) ?? throw new ArgumentException("check needs a setup file or setup pack id.");
    var library = PackLibrary.Scan(PackFolders.Default(extraFolders));
    var setup = SetupLocator.Locate(target, library);
    if (setup.IsFailure)
    {
        foreach (var error in setup.Errors)
        {
            Console.Error.WriteLine($"{error.Code}: {error.Message}");
        }

        return 2;
    }

    var report = new PackChecker(probe).Check(
        setup.Value,
        library,
        new PackCheckOptions(PlaySeason: !options.Contains("--no-season"), InspectImages: !options.Contains("--no-images")));
    Console.Write(PackCheckReportText.Format(report, includeInfo: !options.Contains("--quiet")));

    return report.Verdict switch
    {
        PackCheckVerdict.Blocked => 2,
        PackCheckVerdict.StableWithWarnings when options.Contains("--strict") => 1,
        _ => 0,
    };
}

int List()
{
    var library = PackLibrary.Scan(PackFolders.Default(extraFolders));
    Console.WriteLine($"Built-in packs: {PackFolders.BuiltIn}");
    Console.WriteLine($"Your packs:     {PackFolders.User}");
    foreach (var kind in Enum.GetValues<PackKind>())
    {
        var packs = library.OfKind(kind);
        if (packs.Count == 0)
        {
            continue;
        }

        Console.WriteLine();
        Console.WriteLine($"{kind}:");
        foreach (var pack in packs)
        {
            var targets = pack.Manifest.Targets.Count == 0 ? string.Empty : $"  for {string.Join(", ", pack.Manifest.Targets)}";
            Console.WriteLine($"  {pack.Id,-44} {pack.Manifest.Version,-10} {pack.Origin,-8} {pack.Manifest.Name}{targets}");
        }
    }

    foreach (var finding in library.Findings)
    {
        Console.WriteLine($"{finding.Severity}: {finding.Code}: {finding.Message}");
    }

    return library.Findings.Any(finding => finding.Severity == PackFindingSeverity.Error) ? 2 : 0;
}

int Presets()
{
    var library = PackLibrary.Scan(PackFolders.Default(extraFolders));
    foreach (var pack in library.OfKind(PackKind.Ruleset))
    {
        var manifest = pack.Manifest;
        Console.WriteLine($"{manifest.Id}  {manifest.Version}  — {manifest.Name}");
        if (manifest.Description is not null)
        {
            Console.WriteLine($"    {manifest.Description}");
        }

        var approximated = manifest.Fidelity.Count(note => note.Handling == FidelityHandling.Approximated);
        var missing = manifest.Fidelity.Count - approximated;
        Console.WriteLine($"    {ExplanationText.Count(approximated, "rule")} approximated, {missing} not modelled; 'check' lists each.");
        Console.WriteLine();
    }

    return 0;
}

int Settings(List<string> options)
{
    var group = Positional(options);
    var settings = LeagueSettingCatalogue.All
        .Where(setting => group is null || setting.Group.Contains(group, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (settings.Count == 0)
    {
        return Fail($"No settings group matches '{group}'. Groups: {string.Join(", ", LeagueSettingCatalogue.Groups)}.");
    }

    foreach (var byGroup in settings.GroupBy(setting => setting.Group))
    {
        Console.WriteLine(byGroup.Key);
        foreach (var setting in byGroup)
        {
            var range = setting.Minimum is null && setting.Maximum is null ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" [{setting.Minimum?.ToString("N0", CultureInfo.InvariantCulture) ?? "…"}–{setting.Maximum?.ToString("N0", CultureInfo.InvariantCulture) ?? "…"}]");
            var choices = setting.Choices.Count == 0 ? string.Empty : $" ({string.Join(" | ", setting.Choices)})";
            var columns = setting.Columns.Count == 0 ? string.Empty : $" rows of {{{string.Join(", ", setting.Columns)}}}";
            Console.WriteLine($"  {setting.Key}  — {setting.Label}: {setting.Kind.ToString().ToLowerInvariant()}{range}{choices}{columns}{(setting.Optional ? ", can be off" : string.Empty)}");
            Console.WriteLine($"      {setting.Description}");
        }

        Console.WriteLine();
    }

    return 0;
}

int Match(List<string> options)
{
    var rulesetId = Positional(options) ?? throw new ArgumentException("match needs a ruleset pack id.");
    var library = PackLibrary.Scan(PackFolders.Default(extraFolders));
    var ruleset = library.Find(rulesetId) is { Manifest.Kind: PackKind.Ruleset } found ? found : throw new ArgumentException($"'{rulesetId}' is not an installed ruleset pack.");
    PackPaths.TryResolve(ruleset.Directory, ruleset.Manifest.Content.FileFor(PackKind.Ruleset), out var rulesetFile, out _);
    var rulesetJson = File.ReadAllText(rulesetFile);

    var fits = PackMatcher.LeaguesFor(rulesetJson, library, probe);
    Console.WriteLine($"League packs under {ruleset.Manifest.Label}:");
    if (fits.Count == 0)
    {
        Console.WriteLine("  (no league packs installed — a setup can use a generated fictional league instead)");
    }

    foreach (var fit in fits)
    {
        Console.WriteLine($"  {fit.Pack.Id,-40} {fit.Verdict}");
        foreach (var finding in fit.Findings.Where(finding => finding.Severity != PackFindingSeverity.Info))
        {
            Console.WriteLine($"      {finding.Severity}: {finding.Message}");
        }

        foreach (var cosmetic in PackMatcher.CosmeticsFor(fit.Pack, library))
        {
            Console.WriteLine($"      + {cosmetic.Manifest.Kind.ToString().ToLowerInvariant()}: {cosmetic.Manifest.Label}");
        }
    }

    var leagueId = Value(options, "--league");
    var output = Value(options, "--write");
    if (leagueId is null || output is null)
    {
        return 0;
    }

    var league = library.Find(leagueId) is { Manifest.Kind: PackKind.League } chosen ? chosen : throw new ArgumentException($"'{leagueId}' is not an installed league pack.");
    var setup = PackMatcher.Compose($"{league.Manifest.Name} under {ruleset.Manifest.Name}", ruleset, league, library);
    File.WriteAllText(output, JsonSerializer.Serialize(setup, WriteOptions));
    Console.WriteLine($"Wrote {output}. Check it with: ballgm-packs check {output}");
    return 0;
}

int Keys(List<string> options)
{
    var leagueId = Positional(options) ?? throw new ArgumentException("keys needs a league pack id.");
    var library = PackLibrary.Scan(PackFolders.Default(extraFolders));
    var league = library.Find(leagueId) is { Manifest.Kind: PackKind.League } found ? found : throw new ArgumentException($"'{leagueId}' is not an installed league pack.");
    var rulesetId = Value(options, "--ruleset") ?? league.Manifest.Targets.FirstOrDefault()?.PackId ?? "builtin.ruleset.na-major";
    var ruleset = library.Find(rulesetId) is { Manifest.Kind: PackKind.Ruleset } rules ? rules : throw new ArgumentException($"'{rulesetId}' is not an installed ruleset pack.");
    PackPaths.TryResolve(ruleset.Directory, ruleset.Manifest.Content.FileFor(PackKind.Ruleset), out var rulesetFile, out _);
    PackPaths.TryResolve(league.Directory, league.Manifest.Content.FileFor(PackKind.League), out var leagueFile, out _);

    var probed = probe.ProbeLeague(new LeaguePackSource(leagueFile), File.ReadAllText(rulesetFile));
    if (!probed.Loaded)
    {
        foreach (var finding in probed.Findings)
        {
            Console.Error.WriteLine($"{finding.Code}: {finding.Message}");
        }

        return 2;
    }

    var loaded = probed.Value!;
    var names = loaded.Players.ToDictionary(player => player.Id.Value, player => player.FullName);
    Console.WriteLine("Teams (logos/<key>.png, colours.json keys):");
    foreach (var team in loaded.Teams.OrderBy(team => loaded.Artwork.TeamKeys[team.Id.Value], StringComparer.Ordinal))
    {
        Console.WriteLine($"  {loaded.Artwork.TeamKeys[team.Id.Value],-24} {team.Name}");
    }

    Console.WriteLine();
    Console.WriteLine("Players (faces/<key>.png):");
    foreach (var (id, key) in loaded.Artwork.PlayerKeys.OrderBy(entry => entry.Value, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {key,-48} {names[id]}");
    }

    return 0;
}

int Init(List<string> options)
{
    var folder = Positional(options) ?? throw new ArgumentException("init needs a folder.");
    var kind = Value(options, "--kind") ?? throw new ArgumentException("init needs --kind (ruleset, league, faces, logos, colours, setup).");
    var id = Value(options, "--id") ?? throw new ArgumentException("init needs --id, like yourname.pack-name.");
    var name = Value(options, "--name") ?? id;
    var targets = Value(options, "--targets");

    var manifest = new PackManifestEnvelope(
        PackManifest.CurrentSchemaVersion,
        id,
        name,
        "1.0.0",
        kind.ToLowerInvariant(),
        Author: Environment.UserName,
        Targets: targets is null ? null : [new PackReferenceEnvelope(targets, "*")]);
    var text = JsonSerializer.Serialize(manifest, WriteOptions);
    var check = PackManifestReader.Read(text);
    if (check.IsFailure)
    {
        return Fail(string.Join(Environment.NewLine, check.Errors.Select(error => error.Message)));
    }

    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, PackManifest.FileName);
    if (File.Exists(path))
    {
        return Fail($"{path} already exists; not overwriting it.");
    }

    File.WriteAllText(path, text);
    if (check.Value.Kind is PackKind.Faces or PackKind.Logos)
    {
        Directory.CreateDirectory(Path.Combine(folder, check.Value.Content.FolderFor(check.Value.Kind)));
    }

    Console.WriteLine($"Wrote {path}.");
    return 0;
}

static string? Positional(List<string> options)
{
    for (var index = 0; index < options.Count; index++)
    {
        if (options[index].StartsWith("--", StringComparison.Ordinal))
        {
            if (options[index] is "--packs" or "--league" or "--write" or "--ruleset" or "--kind" or "--id" or "--name" or "--targets")
            {
                index++;
            }

            continue;
        }

        return options[index];
    }

    return null;
}

static string? Value(List<string> options, string flag)
{
    var index = options.IndexOf(flag);
    return index >= 0 && index + 1 < options.Count ? options[index + 1] : null;
}

static List<string> Values(List<string> options, string flag) =>
    options.Select((option, index) => (option, index)).Where(entry => entry.option == flag && entry.index + 1 < options.Count).Select(entry => options[entry.index + 1]).ToList();

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Run 'ballgm-packs help' for usage.");
    return Usage;
}

static void PrintUsage() => Console.WriteLine("""
    ballgm-packs — the BallGM pack checker

      check <setup file | setup pack id>   Will this setup load, play and look right? Plays a test season.
            [--no-season] [--no-images] [--strict] [--quiet]
      list                                 Every installed pack, and every folder that is not one.
      presets                              The built-in league-setting presets.
      settings [group]                     Every league setting, with what it does and its range.
      match <ruleset pack id>              Which installed leagues fit these settings, and their cosmetics.
            [--league <id> --write <file>] ...and write a setup that uses them.
      keys <league pack id> [--ruleset id] The team and player keys image packs name their files after.
      init <folder> --kind <kind> --id <id> [--name <name>] [--targets <league id>]
                                           Start a new pack.

      --packs <dir>   Also read packs from this folder (repeatable).
    """);

/// <summary>How the tool writes files people then edit by hand: indented, nulls left out, and real characters rather than \u escapes.</summary>
internal static partial class Program
{
    internal static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
