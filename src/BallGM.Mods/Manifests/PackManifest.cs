namespace BallGM.Mods.Manifests;

/// <summary>
/// What a pack is. A pack is exactly one kind: a league setup that wants a roster, a ruleset and
/// some faces names three packs, rather than one pack carrying all three — which is what lets a face
/// pack outlive the roster update it was first made for, and lets the checker say which layer a
/// problem is in.
/// </summary>
public enum PackKind
{
    /// <summary>League settings: a complete ruleset file. The built-in presets are packs of this kind.</summary>
    Ruleset,

    /// <summary>Teams, players, contracts and alignment: a league pack file.</summary>
    League,

    /// <summary>Player portraits, matched to a league's player keys.</summary>
    Faces,

    /// <summary>Team logos, matched to a league's team keys, and optionally the league's own.</summary>
    Logos,

    /// <summary>Team colours, matched to a league's team keys.</summary>
    Colours,

    /// <summary>A ready-made league setup naming the other packs — how the mod scene ships "the whole thing" as one download.</summary>
    Setup,
}

/// <summary>How faithfully a ruleset pack's league is expressed, for one rule the pack author knows about.</summary>
public enum FidelityHandling
{
    /// <summary>Expressed, but not exactly as the league does it; <see cref="FidelityNote.Detail"/> says how it differs.</summary>
    Approximated,

    /// <summary>The league has this rule; this build has nowhere to put it.</summary>
    NotModelled,
}

/// <summary>
/// One rule of the league a ruleset pack describes that the game cannot express exactly. The checker
/// repeats every one of these in its report: a preset that silently drops a rule is the
/// silent-wrong-rule failure the ruleset format refuses everywhere else.
/// </summary>
public sealed record FidelityNote(string Rule, FidelityHandling Handling, string Detail);

/// <summary>Another pack, by id, and the versions of it that will do.</summary>
public sealed record PackReference(string PackId, VersionRange Versions)
{
    public bool IsSatisfiedBy(PackManifest manifest) =>
        string.Equals(manifest.Id, PackId, StringComparison.Ordinal) && Versions.Admits(manifest.Version);

    public override string ToString() => Versions == VersionRange.Any ? PackId : $"{PackId} {Versions}";
}

/// <summary>
/// Where a pack's content lives inside its folder. Which fields apply depends on the kind; each has a
/// default, so the common pack needs no <c>content</c> block at all:
/// <list type="bullet">
/// <item>ruleset — <see cref="File"/>, default <c>ruleset.json</c>.</item>
/// <item>league — <see cref="File"/>, default <c>league.json</c>.</item>
/// <item>setup — <see cref="File"/>, default <c>setup.json</c>.</item>
/// <item>colours — <see cref="File"/>, default <c>colours.json</c>.</item>
/// <item>faces — <see cref="Folder"/>, default <c>faces</c>; optional <see cref="Map"/>.</item>
/// <item>logos — <see cref="Folder"/>, default <c>logos</c>; optional <see cref="Map"/> and <see cref="LeagueLogo"/>.</item>
/// </list>
/// </summary>
public sealed record PackContent(string? File = null, string? Folder = null, string? Map = null, string? LeagueLogo = null)
{
    public static PackContent None { get; } = new();

    /// <summary>The content file for a file-based kind, with its default applied.</summary>
    public string FileFor(PackKind kind) => File ?? kind switch
    {
        PackKind.Ruleset => "ruleset.json",
        PackKind.League => "league.json",
        PackKind.Setup => "setup.json",
        PackKind.Colours => "colours.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind of pack has no content file."),
    };

    /// <summary>The image folder for an image kind, with its default applied.</summary>
    public string FolderFor(PackKind kind) => Folder ?? kind switch
    {
        PackKind.Faces => "faces",
        PackKind.Logos => "logos",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind of pack has no image folder."),
    };
}

/// <summary>
/// A pack's manifest, <c>ballgm-pack.json</c>, after validation: who made it, what it is, what it
/// needs, and what it was made for. Everything the checker decides about a pack before opening its
/// content is decided from here.
/// <para>
/// <see cref="Requires"/> names packs that must be part of the same setup; <see cref="Conflicts"/>
/// packs that must not be; <see cref="Targets"/>, for a cosmetic pack, the league packs whose keys it
/// was written against, and for a league pack the ruleset packs its contracts and rosters were
/// authored under. The three are separate on purpose: a face pack does not <em>need</em> a
/// roster in order to load — it needs one in order to <em>match</em> — and the checker reports a
/// mismatch as fallbacks rather than a refusal.
/// </para>
/// </summary>
public sealed record PackManifest(
    int SchemaVersion,
    string Id,
    string Name,
    PackVersion Version,
    PackKind Kind,
    string? Author,
    string? Description,
    IReadOnlyList<PackReference> Requires,
    IReadOnlyList<PackReference> Conflicts,
    IReadOnlyList<PackReference> Targets,
    PackContent Content,
    IReadOnlyList<FidelityNote> Fidelity)
{
    /// <summary>The file name a pack folder's manifest must have.</summary>
    public const string FileName = "ballgm-pack.json";

    /// <summary>
    /// Version 1 was a placeholder holding only a name, never written to disk by anything; version 2
    /// is the first manifest a pack actually ships with.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Ids starting with this belong to the packs the game ships; a pack in a user folder may not claim one.</summary>
    public const string BuiltInIdPrefix = "builtin.";

    public bool IsCosmetic => Kind is PackKind.Faces or PackKind.Logos or PackKind.Colours;

    public string Label => $"{Name} ({Id} {Version})";
}
