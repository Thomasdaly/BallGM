using BallGM.Application.Packs;
using BallGM.Mods.Library;
using BallGM.Mods.Manifests;
using BallGM.Mods.Setups;

namespace BallGM.Mods.Checking;

/// <summary>How one installed league pack fares under a set of league settings.</summary>
public sealed record LeagueFit(InstalledPack Pack, PackCheckVerdict Verdict, IReadOnlyList<PackFinding> Findings);

/// <summary>
/// Settings first, then packs: given the league settings a player has chosen, which installed leagues
/// load under them, and which cosmetic packs were made for each league. The way round Football
/// Manager does not offer — there a player picks files and finds out at game start whether they
/// agree; here the settings narrow the choice before anything is picked.
/// </summary>
public static class PackMatcher
{
    /// <summary>Every installed league pack, loaded under <paramref name="rulesetJson"/>, best fit first.</summary>
    public static IReadOnlyList<LeagueFit> LeaguesFor(string rulesetJson, PackLibrary library, IPackContentProbe probe)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(probe);

        return library.OfKind(PackKind.League)
            .Select(pack =>
            {
                if (!PackPaths.TryResolve(pack.Directory, pack.Manifest.Content.FileFor(PackKind.League), out var file, out var reason))
                {
                    return new LeagueFit(pack, PackCheckVerdict.Blocked, [PackFinding.Error("league.unsafe_path", PackCheckStage.League, $"The league file was refused: {reason}.", pack.Id)]);
                }

                var probed = probe.ProbeLeague(new LeaguePackSource(file), rulesetJson);
                return new LeagueFit(pack, PackCheckReport.VerdictFor(probed.Findings), probed.Findings);
            })
            .OrderBy(fit => fit.Verdict)
            .ThenBy(fit => fit.Pack.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The cosmetic packs that target <paramref name="league"/> (at its installed version), colours then logos then faces.</summary>
    public static IReadOnlyList<InstalledPack> CosmeticsFor(InstalledPack league, PackLibrary library)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(library);

        return library.Packs
            .Where(pack => pack.Manifest.IsCosmetic && pack.Manifest.Targets.Any(target => target.IsSatisfiedBy(league.Manifest)))
            .OrderBy(pack => pack.Manifest.Kind switch { PackKind.Colours => 0, PackKind.Logos => 1, _ => 2 })
            .ThenBy(pack => pack.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A setup that plays <paramref name="league"/> under <paramref name="ruleset"/> with every
    /// cosmetic pack made for that league, in a sensible order — what a player saves as a starting point
    /// and edits, rather than writing one from nothing.
    /// </summary>
    public static LeagueSetupEnvelope Compose(string name, InstalledPack ruleset, InstalledPack league, PackLibrary library) =>
        new(
            LeagueSetup.CurrentSchemaVersion,
            name,
            new PackReferenceEnvelope(ruleset.Id, $"^{ruleset.Manifest.Version}"),
            new Dictionary<string, System.Text.Json.JsonElement>(),
            new LeagueChoiceEnvelope(league.Id, $"^{league.Manifest.Version}"),
            CosmeticsFor(league, library).Select(pack => new PackReferenceEnvelope(pack.Id, $"^{pack.Manifest.Version}")).ToList());
}
