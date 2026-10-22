using BallGM.Application.Leagues;
using BallGM.Application.Packs;
using BallGM.Domain.Common;
using BallGM.Mods.Cosmetics;
using BallGM.Mods.Library;
using BallGM.Mods.Manifests;
using BallGM.Mods.Settings;
using BallGM.Mods.Setups;

namespace BallGM.Mods.Checking;

/// <summary>What the checker does beyond reading files. Both default on; a screen that rechecks on every keystroke turns them off.</summary>
public sealed record PackCheckOptions(bool PlaySeason = true, bool InspectImages = true)
{
    public static PackCheckOptions Full { get; } = new();
}

/// <summary>
/// The pack checker: a game-stability check, not a policing one. Given a setup and the packs
/// installed, it answers one question — will this load, play, and look the way the packs say? — in
/// the same order the game loads: settings, then the league, then a played season, then the
/// cosmetic layers.
/// <para>
/// Three rules decide what blocks a load. <b>Anything that stops the game running blocks</b>: a
/// ruleset the reader refuses, a league that will not load under those settings, a season that
/// cannot start or finish. <b>Anything the setup asks for and cannot have blocks</b>: a pack that is
/// not installed, the wrong version, a missing dependency, a declared conflict — because loading
/// something other than what was asked for is how a career ends up on the wrong data without anybody
/// noticing. <b>Nothing cosmetic ever blocks</b>: a missing, broken or unmatched image is a fallback,
/// reported by pack and by count.
/// </para>
/// <para>
/// None of it asks what a pack contains beyond whether it works. Real names, real leagues, community
/// packs: the checker is neutral about content, and only reports on stability.
/// </para>
/// </summary>
public sealed class PackChecker
{
    private const double FaceMaximumAspect = 2.0;
    private const double LogoMaximumAspect = 3.0;

    private readonly IPackContentProbe _probe;

    public PackChecker(IPackContentProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public PackCheckReport Check(LeagueSetup setup, PackLibrary library, PackCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(library);
        options ??= PackCheckOptions.Full;

        var findings = new List<PackFinding>(library.Findings);
        var steps = new List<LoadStep>();
        var coverage = new List<CosmeticCoverage>();
        var report = () => new PackCheckReport(setup.Name, PackCheckReport.VerdictFor(findings), findings, steps, coverage);

        // Setup: every pack the setup names is installed, of the right kind, at an admitted version.
        var rulesetPack = Select(setup.Ruleset, [PackKind.Ruleset], "the league settings", library, findings);
        var leaguePack = setup.League.Pack is null ? null : Select(setup.League.Pack, [PackKind.League], "the league", library, findings);
        var cosmetics = new List<InstalledPack>();
        foreach (var reference in setup.Cosmetics)
        {
            if (cosmetics.Any(pack => pack.Id == reference.PackId))
            {
                findings.Add(PackFinding.Warning("setup.duplicate_cosmetic", PackCheckStage.Setup, $"'{reference.PackId}' is listed twice among the cosmetics; it loads once, at its first position.", reference.PackId));
                continue;
            }

            if (Select(reference, [PackKind.Faces, PackKind.Logos, PackKind.Colours], "a cosmetic layer", library, findings) is { } pack)
            {
                cosmetics.Add(pack);
            }
        }

        // Dependencies, conflicts and targets across the whole selection.
        var selected = new[] { rulesetPack, leaguePack }.OfType<InstalledPack>().Concat(cosmetics).ToList();
        CheckDependencies(selected, library, findings);
        CheckTargets(rulesetPack, leaguePack, setup.League.Generated is not null, cosmetics, findings);
        cosmetics = OrderCosmetics(cosmetics, findings);

        if (rulesetPack is null)
        {
            return report();
        }

        // Settings: the preset, the overrides, and whether the result is a ruleset this build runs.
        var rulesetJson = ReadContent(rulesetPack, findings);
        if (rulesetJson is null)
        {
            return report();
        }

        var resolved = LeagueSettingsResolver.Resolve(rulesetJson, setup.Settings, rulesetPack.Id);
        findings.AddRange(resolved.Findings);
        foreach (var note in rulesetPack.Manifest.Fidelity)
        {
            findings.Add(PackFinding.Info(
                note.Handling == FidelityHandling.Approximated ? "settings.rule_approximated" : "settings.rule_not_modelled",
                PackCheckStage.Settings,
                $"{note.Rule} — {(note.Handling == FidelityHandling.Approximated ? "approximated" : "not modelled")}: {note.Detail}",
                rulesetPack.Id));
        }

        if (resolved.RulesetJson is null || HasErrors(findings, PackCheckStage.Settings))
        {
            return report();
        }

        var ruleset = _probe.ProbeRuleset(resolved.RulesetJson);
        findings.AddRange(ruleset.Findings.Select(finding => finding with { PackId = finding.PackId ?? rulesetPack.Id }));
        if (!ruleset.Loaded)
        {
            return report();
        }

        steps.Add(new LoadStep(
            steps.Count + 1,
            PackCheckStage.Settings,
            $"League settings: {ruleset.Value!.Name}, from {rulesetPack.Manifest.Label}{(setup.Settings.Count == 0 ? string.Empty : $", with {ExplanationText.Count(setup.Settings.Count, "override")}")}.",
            rulesetPack.Id));

        // League: the content, loaded under exactly those settings.
        LeagueSource source;
        if (leaguePack is not null)
        {
            if (!PackPaths.TryResolve(leaguePack.Directory, leaguePack.Manifest.Content.FileFor(PackKind.League), out var leagueFile, out var reason))
            {
                findings.Add(PackFinding.Error("league.unsafe_path", PackCheckStage.League, $"The league file was refused: {reason}.", leaguePack.Id));
                return report();
            }

            source = new LeaguePackSource(leagueFile);
        }
        else if (setup.League.Generated is { } generated)
        {
            source = generated;
        }
        else
        {
            return report();
        }

        var league = _probe.ProbeLeague(source, resolved.RulesetJson);
        findings.AddRange(league.Findings.Select(finding => finding with { PackId = finding.PackId ?? leaguePack?.Id }));
        if (!league.Loaded)
        {
            return report();
        }

        var snapshot = league.Value!;
        steps.Add(new LoadStep(
            steps.Count + 1,
            PackCheckStage.League,
            $"{(leaguePack is null ? "Generated fictional league" : leaguePack.Manifest.Label)}: {snapshot.League.Name}, {ExplanationText.Count(snapshot.Teams.Count, "team")}, {ExplanationText.Count(snapshot.Players.Count, "player")}, season {snapshot.CurrentSeason.Year}.",
            leaguePack?.Id));

        // Season: actually play it.
        if (options.PlaySeason && !findings.Any(finding => finding.Severity == PackFindingSeverity.Error))
        {
            var played = _probe.SmokeTestSeason(source, resolved.RulesetJson, setup.SmokeTestSeed);
            findings.AddRange(played);
            if (played.All(finding => finding.Severity != PackFindingSeverity.Error))
            {
                steps.Add(new LoadStep(steps.Count + 1, PackCheckStage.Season, "Test season played start to finish and rolled over (not part of your career)."));
            }
        }

        // Cosmetics: layered on the league pack's own artwork, last pack winning.
        var assignments = ApplyCosmetics(snapshot, cosmetics, options, findings, steps, coverage);

        var finished = report();
        return finished.Verdict == PackCheckVerdict.Blocked
            ? finished
            : finished with
            {
                Plan = new PackLoadPlan(resolved.RulesetJson, source, assignments.Logos, assignments.Faces, assignments.Colours, assignments.LeagueLogo),
            };
    }

    private static InstalledPack? Select(PackReference reference, PackKind[] kinds, string role, PackLibrary library, List<PackFinding> findings)
    {
        var pack = library.Find(reference.PackId);
        var kindNames = string.Join(" or ", kinds.Select(kind => kind.ToString().ToLowerInvariant()));
        if (pack is null)
        {
            var alternatives = library.Packs.Where(candidate => kinds.Contains(candidate.Manifest.Kind)).Select(candidate => candidate.Id).ToList();
            findings.Add(PackFinding.Error(
                "setup.pack_not_installed",
                PackCheckStage.Setup,
                $"The setup uses '{reference.PackId}' for {role}, and no pack with that id is installed.",
                reference.PackId,
                alternatives.Count == 0
                    ? $"Install it into {PackFolders.User}."
                    : $"Install it, or use one of the installed {kindNames} packs: {CosmeticPackReader.Examples(alternatives, 5)}."));
            return null;
        }

        if (!kinds.Contains(pack.Manifest.Kind))
        {
            findings.Add(PackFinding.Error(
                "setup.wrong_pack_kind",
                PackCheckStage.Setup,
                $"'{pack.Id}' is a {pack.Manifest.Kind.ToString().ToLowerInvariant()} pack; the setup uses it for {role}, which needs a {kindNames} pack.",
                pack.Id));
            return null;
        }

        if (!reference.Versions.Admits(pack.Manifest.Version))
        {
            findings.Add(PackFinding.Error(
                "setup.version_mismatch",
                PackCheckStage.Setup,
                $"The setup wants '{pack.Id}' {reference.Versions}, and version {pack.Manifest.Version} is installed.",
                pack.Id,
                $"Install a version the setup admits, or change the setup's range for '{pack.Id}'."));
            return null;
        }

        return pack;
    }

    private static void CheckDependencies(IReadOnlyList<InstalledPack> selected, PackLibrary library, List<PackFinding> findings)
    {
        foreach (var pack in selected)
        {
            foreach (var requirement in pack.Manifest.Requires)
            {
                if (selected.Any(candidate => requirement.IsSatisfiedBy(candidate.Manifest)))
                {
                    continue;
                }

                var installed = library.Find(requirement.PackId);
                var inSetup = selected.FirstOrDefault(candidate => candidate.Id == requirement.PackId);
                var message = inSetup is not null
                    ? $"{pack.Manifest.Label} requires '{requirement.PackId}' {requirement.Versions}; the setup has version {inSetup.Manifest.Version}."
                    : installed is not null
                        ? $"{pack.Manifest.Label} requires '{requirement}', which is installed ({installed.Manifest.Version}) but not part of this setup."
                        : $"{pack.Manifest.Label} requires '{requirement}', which is not installed.";
                var fix = inSetup is not null || (installed is not null && !requirement.Versions.Admits(installed.Manifest.Version))
                    ? $"Install a version of '{requirement.PackId}' in {requirement.Versions}."
                    : installed is not null ? $"Add '{requirement.PackId}' to the setup." : $"Install '{requirement.PackId}'.";
                findings.Add(PackFinding.Error("dependencies.missing", PackCheckStage.Dependencies, message, pack.Id, fix));
            }

            foreach (var conflict in pack.Manifest.Conflicts)
            {
                foreach (var other in selected.Where(other => other != pack && conflict.IsSatisfiedBy(other.Manifest)))
                {
                    findings.Add(PackFinding.Error(
                        "dependencies.conflict",
                        PackCheckStage.Dependencies,
                        $"{pack.Manifest.Label} declares that it does not work with {other.Manifest.Label}, and the setup uses both.",
                        pack.Id,
                        "Remove one of the two from the setup."));
                }
            }
        }
    }

    private static void CheckTargets(InstalledPack? rulesetPack, InstalledPack? leaguePack, bool generated, IReadOnlyList<InstalledPack> cosmetics, List<PackFinding> findings)
    {
        if (leaguePack is not null && rulesetPack is not null && leaguePack.Manifest.Targets.Count > 0 &&
            !leaguePack.Manifest.Targets.Any(target => target.IsSatisfiedBy(rulesetPack.Manifest)))
        {
            findings.Add(PackFinding.Info(
                "dependencies.league_authored_for_other_settings",
                PackCheckStage.Dependencies,
                $"{leaguePack.Manifest.Label} was authored for {string.Join(" or ", leaguePack.Manifest.Targets)}; this setup plays it under {rulesetPack.Manifest.Label}. How well it fits is checked below.",
                leaguePack.Id));
        }

        foreach (var pack in cosmetics.Where(pack => pack.Manifest.Targets.Count > 0))
        {
            if (leaguePack is not null && pack.Manifest.Targets.Any(target => target.IsSatisfiedBy(leaguePack.Manifest)))
            {
                continue;
            }

            findings.Add(PackFinding.Warning(
                "dependencies.cosmetic_targets_other_league",
                PackCheckStage.Dependencies,
                $"{pack.Manifest.Label} was made for {string.Join(" or ", pack.Manifest.Targets)}, and this setup's league is {(generated ? "a generated one" : leaguePack?.Manifest.Label ?? "not loaded")}. Only keys the two happen to share will match; coverage is reported below.",
                pack.Id,
                "Use the league pack it was made for, or a version of this pack made for your league."));
        }
    }

    /// <summary>
    /// The setup's cosmetic order, except that a pack always loads after a cosmetic pack it requires —
    /// a pack that builds on another one means to win over it. Moving one is said, not done quietly.
    /// </summary>
    private static List<InstalledPack> OrderCosmetics(List<InstalledPack> cosmetics, List<PackFinding> findings)
    {
        var ordered = new List<InstalledPack>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(InstalledPack pack)
        {
            if (ordered.Contains(pack))
            {
                return true;
            }

            if (!visiting.Add(pack.Id))
            {
                findings.Add(PackFinding.Error("dependencies.cycle", PackCheckStage.Dependencies, $"{pack.Manifest.Label} is part of a circle of packs that each require the next.", pack.Id));
                return false;
            }

            foreach (var required in cosmetics.Where(other => pack.Manifest.Requires.Any(requirement => requirement.IsSatisfiedBy(other.Manifest))))
            {
                if (!ordered.Contains(required) && cosmetics.IndexOf(required) > cosmetics.IndexOf(pack))
                {
                    findings.Add(PackFinding.Info(
                        "dependencies.load_order_adjusted",
                        PackCheckStage.Dependencies,
                        $"{required.Manifest.Label} now loads before {pack.Manifest.Label}, which requires it.",
                        pack.Id));
                }

                if (!Visit(required))
                {
                    return false;
                }
            }

            visiting.Remove(pack.Id);
            ordered.Add(pack);
            return true;
        }

        foreach (var pack in cosmetics)
        {
            if (!Visit(pack))
            {
                break;
            }
        }

        return ordered;
    }

    private static string? ReadContent(InstalledPack pack, List<PackFinding> findings)
    {
        var relative = pack.Manifest.Content.FileFor(pack.Manifest.Kind);
        if (!PackPaths.TryResolve(pack.Directory, relative, out var path, out var reason))
        {
            findings.Add(PackFinding.Error("settings.unsafe_path", PackCheckStage.Settings, $"The ruleset file was refused: {reason}.", pack.Id));
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            findings.Add(PackFinding.Error(
                "settings.ruleset_unreadable",
                PackCheckStage.Settings,
                $"The ruleset file '{relative}' could not be read: {exception.Message}",
                pack.Id,
                $"A ruleset pack keeps its ruleset in '{relative}' next to {PackManifest.FileName}."));
            return null;
        }
    }

    private sealed record Assignments(
        IReadOnlyDictionary<string, string> Faces,
        IReadOnlyDictionary<string, string> Logos,
        IReadOnlyDictionary<string, TeamColours> Colours,
        string? LeagueLogo);

    private static Assignments ApplyCosmetics(
        LeagueSnapshot league,
        IReadOnlyList<InstalledPack> cosmetics,
        PackCheckOptions options,
        List<PackFinding> findings,
        List<LoadStep> steps,
        List<CosmeticCoverage> coverage)
    {
        var artwork = league.Artwork;
        var teamKeys = artwork.TeamKeys.Values.ToHashSet(StringComparer.Ordinal);
        var playerKeys = artwork.PlayerKeys.Values.ToHashSet(StringComparer.Ordinal);

        // The league pack's own artwork is the bottom layer, re-keyed from loaded ids to pack keys.
        var faces = artwork.PlayerPortraits.Where(entry => artwork.PlayerKeys.ContainsKey(entry.Key))
            .ToDictionary(entry => artwork.PlayerKeys[entry.Key], entry => (Path: entry.Value, Pack: (string?)null), StringComparer.Ordinal);
        var logos = artwork.TeamLogos.Where(entry => artwork.TeamKeys.ContainsKey(entry.Key))
            .ToDictionary(entry => artwork.TeamKeys[entry.Key], entry => (Path: entry.Value, Pack: (string?)null), StringComparer.Ordinal);
        var colours = artwork.TeamColours.Where(entry => artwork.TeamKeys.ContainsKey(entry.Key))
            .ToDictionary(entry => artwork.TeamKeys[entry.Key], entry => (Colours: entry.Value, Pack: (string?)null), StringComparer.Ordinal);
        string? leagueLogo = null;

        foreach (var pack in cosmetics)
        {
            var layer = CosmeticPackReader.Read(pack, findings);
            var kind = pack.Manifest.Kind;
            var keys = kind == PackKind.Faces ? playerKeys : teamKeys;
            var noun = kind == PackKind.Faces ? "player" : "team";
            var provided = kind == PackKind.Colours ? layer.Colours.Keys.ToList() : layer.Images.Keys.ToList();

            var orphans = provided.Where(key => !keys.Contains(key)).Order(StringComparer.Ordinal).ToList();
            if (orphans.Count > 0)
            {
                findings.Add(PackFinding.Warning(
                    "cosmetics.unmatched_keys",
                    PackCheckStage.Cosmetics,
                    $"{ExplanationText.Count(orphans.Count, "entry", "entries")} {(orphans.Count == 1 ? "matches" : "match")} no {noun} in this league and {(orphans.Count == 1 ? "is" : "are")} unused: {CosmeticPackReader.Examples(orphans)}.",
                    pack.Id,
                    $"Files are named after the league's {noun} keys; 'ballgm-packs keys' lists them."));
            }

            var overridden = new Dictionary<string, int>(StringComparer.Ordinal);
            var applied = 0;
            if (kind == PackKind.Colours)
            {
                foreach (var key in provided.Where(keys.Contains))
                {
                    Count(overridden, colours.GetValueOrDefault(key).Pack, colours.ContainsKey(key));
                    colours[key] = (layer.Colours[key], pack.Id);
                    applied++;
                }
            }
            else
            {
                var target = kind == PackKind.Faces ? faces : logos;
                var problems = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var key in provided.Where(keys.Contains).Order(StringComparer.Ordinal))
                {
                    var path = layer.Images[key];
                    if (options.InspectImages && !Usable(path, kind, key, problems))
                    {
                        continue;
                    }

                    Count(overridden, target.GetValueOrDefault(key).Pack, target.ContainsKey(key));
                    target[key] = (path, pack.Id);
                    applied++;
                }

                ReportImageProblems(pack, problems, findings);

                if (layer.LeagueLogo is not null)
                {
                    var leagueProblems = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    if (!options.InspectImages || Usable(layer.LeagueLogo, kind, "league logo", leagueProblems))
                    {
                        leagueLogo = layer.LeagueLogo;
                    }

                    ReportImageProblems(pack, leagueProblems, findings);
                }
            }

            foreach (var (previous, count) in overridden.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                findings.Add(PackFinding.Info(
                    "cosmetics.overrides",
                    PackCheckStage.Cosmetics,
                    $"{pack.Manifest.Label} replaces {ExplanationText.Count(count, kind == PackKind.Colours ? "team's colours" : "image")} from {(previous.Length == 0 ? "the league pack itself" : previous)}.",
                    pack.Id));
            }

            steps.Add(new LoadStep(
                steps.Count + 1,
                PackCheckStage.Cosmetics,
                $"{pack.Manifest.Label}: {kind.ToString().ToLowerInvariant()} for {applied:N0} of {keys.Count:N0} {noun}s{(layer.LeagueLogo is not null && leagueLogo == layer.LeagueLogo ? ", and the league logo" : string.Empty)}.",
                pack.Id));
        }

        coverage.Add(new CosmeticCoverage("faces", faces.Count, playerKeys.Count));
        coverage.Add(new CosmeticCoverage("logos", logos.Count, teamKeys.Count));
        coverage.Add(new CosmeticCoverage("colours", colours.Count, teamKeys.Count));

        if (cosmetics.Any(pack => pack.Manifest.Kind == PackKind.Faces) && faces.Count < playerKeys.Count)
        {
            findings.Add(PackFinding.Info(
                "cosmetics.fallback_faces",
                PackCheckStage.Cosmetics,
                $"{ExplanationText.Count(playerKeys.Count - faces.Count, "player")} have no face in any pack and show the drawn silhouette in team colours."));
        }

        if (cosmetics.Any(pack => pack.Manifest.Kind == PackKind.Logos) && logos.Count < teamKeys.Count)
        {
            findings.Add(PackFinding.Info(
                "cosmetics.fallback_logos",
                PackCheckStage.Cosmetics,
                $"{ExplanationText.Count(teamKeys.Count - logos.Count, "team")} have no logo in any pack and show the drawn monogram plate."));
        }

        return new Assignments(
            faces.ToDictionary(entry => entry.Key, entry => entry.Value.Path, StringComparer.Ordinal),
            logos.ToDictionary(entry => entry.Key, entry => entry.Value.Path, StringComparer.Ordinal),
            colours.ToDictionary(entry => entry.Key, entry => entry.Value.Colours, StringComparer.Ordinal),
            leagueLogo);
    }

    private static void Count(Dictionary<string, int> overridden, string? previousPack, bool existed)
    {
        if (!existed)
        {
            return;
        }

        var key = previousPack ?? string.Empty;
        overridden[key] = overridden.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// Whether the client will be able to draw the file. A file it cannot draw is dropped from its
    /// layer, so whatever is beneath it — another pack's image, the league pack's own — still shows:
    /// a broken file never hides a good one.
    /// </summary>
    private static bool Usable(string path, PackKind kind, string key, Dictionary<string, List<string>> problems)
    {
        var inspection = ImageFileInspector.Inspect(path, kind == PackKind.Faces ? FaceMaximumAspect : LogoMaximumAspect);
        var usable = inspection.Usable || (inspection.Problem == ImageProblem.TooWide && kind == PackKind.Logos);

        if (inspection.Problem != ImageProblem.None)
        {
            var label = inspection.Problem == ImageProblem.TooWide && kind == PackKind.Logos ? "LetterboxedLogo" : inspection.Problem.ToString();
            Add(problems, label, key);
        }

        if (inspection.ExtensionMismatch)
        {
            Add(problems, "ExtensionMismatch", key);
        }

        return usable;
    }

    private static void Add(Dictionary<string, List<string>> problems, string problem, string key)
    {
        if (!problems.TryGetValue(problem, out var keys))
        {
            problems[problem] = keys = [];
        }

        keys.Add(key);
    }

    private static void ReportImageProblems(InstalledPack pack, Dictionary<string, List<string>> problems, List<PackFinding> findings)
    {
        foreach (var (problem, keys) in problems.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var count = ExplanationText.Count(keys.Count, "image");
            var examples = CosmeticPackReader.Examples(keys);
            var one = keys.Count == 1;
            string Pick(string single, string plural) => one ? single : plural;
            findings.Add(problem switch
            {
                nameof(ImageProblem.Missing) => PackFinding.Warning("cosmetics.image_missing", PackCheckStage.Cosmetics, $"{count} named by the map {Pick("does", "do")} not exist ({examples}); fallback art shows instead.", pack.Id),
                nameof(ImageProblem.Empty) => PackFinding.Warning("cosmetics.image_empty", PackCheckStage.Cosmetics, $"{count} {Pick("is an empty file", "are empty files")} ({examples}); fallback art shows instead.", pack.Id, "Re-download the pack: a zero-byte file is usually an interrupted copy."),
                nameof(ImageProblem.Unreadable) => PackFinding.Warning("cosmetics.image_unreadable", PackCheckStage.Cosmetics, $"{count} could not be read or {Pick("is", "are")} truncated ({examples}); fallback art shows instead.", pack.Id),
                nameof(ImageProblem.NotPngOrJpeg) => PackFinding.Warning("cosmetics.image_not_png_or_jpeg", PackCheckStage.Cosmetics, $"{count} {Pick("is", "are")} not really PNG or JPEG, whatever the name says ({examples}); fallback art shows instead.", pack.Id, "Re-save as PNG or JPEG."),
                nameof(ImageProblem.TooWide) => PackFinding.Warning("cosmetics.face_aspect", PackCheckStage.Cosmetics, $"{count} {Pick("is", "are")} more than twice as wide as tall or tall as wide ({examples}) and cannot be framed as a face; fallback art shows instead.", pack.Id, "Crop to roughly square, head and shoulders."),
                "LetterboxedLogo" => PackFinding.Info("cosmetics.logo_letterboxed", PackCheckStage.Cosmetics, $"{count} {Pick("is", "are")} more than three times as wide as tall ({examples}); {Pick("it is", "they are")} letterboxed in square slots.", pack.Id),
                nameof(ImageProblem.Oversized) => PackFinding.Info("cosmetics.image_oversized", PackCheckStage.Cosmetics, $"{count} {Pick("is", "are")} larger than {ImageFileInspector.LargestSide}px or 16 MB ({examples}); {Pick("it is", "they are")} drawn from a downscaled copy and slow first display.", pack.Id),
                _ => PackFinding.Info("cosmetics.extension_mismatch", PackCheckStage.Cosmetics, $"{count} {Pick("has", "have")} the wrong extension for {Pick("its", "their")} format ({examples}); {Pick("it is", "they are")} read by content.", pack.Id),
            });
        }
    }

    private static bool HasErrors(IEnumerable<PackFinding> findings, PackCheckStage stage) =>
        findings.Any(finding => finding.Stage == stage && finding.Severity == PackFindingSeverity.Error);
}
