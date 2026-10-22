namespace BallGM.Mods.Manifests;

/// <summary>
/// The on-disk shape of <c>ballgm-pack.json</c>. Untrusted input: everything nullable, nothing
/// validated here; <see cref="PackManifestReader"/> is the trust boundary.
/// </summary>
public sealed record PackManifestEnvelope(
    int SchemaVersion,
    string? Id,
    string? Name,
    string? Version,
    string? Kind,
    string? Author = null,
    string? Description = null,
    IReadOnlyList<PackReferenceEnvelope>? Requires = null,
    IReadOnlyList<PackReferenceEnvelope>? Conflicts = null,
    IReadOnlyList<PackReferenceEnvelope>? Targets = null,
    PackContentEnvelope? Content = null,
    IReadOnlyList<FidelityNoteEnvelope>? Fidelity = null);

/// <summary>Another pack, by id, and a version range (absent means any version).</summary>
public sealed record PackReferenceEnvelope(string? Pack, string? Versions = null);

/// <summary>Where a pack keeps its content; see <see cref="PackContent"/> for which field applies to which kind.</summary>
public sealed record PackContentEnvelope(string? File = null, string? Folder = null, string? Map = null, string? LeagueLogo = null);

/// <summary>One rule a ruleset pack expresses inexactly: its name, <c>Approximated</c> or <c>NotModelled</c>, and how.</summary>
public sealed record FidelityNoteEnvelope(string? Rule, string? Handling, string? Detail);
