using System.Text.Json;
using BallGM.Domain.Common;

namespace BallGM.Mods.Manifests;

/// <summary>
/// Reads <c>ballgm-pack.json</c>. Never throws on content: every problem in the file is collected and
/// returned, so a manifest with four mistakes reports four.
/// </summary>
public static class PackManifestReader
{
    public const string MalformedCode = "pack.manifest_malformed";
    public const string UnsupportedSchemaCode = "pack.manifest_unsupported_schema";
    public const string InvalidFieldCode = "pack.manifest_invalid_field";

    /// <summary>The longest pack id: room for <c>author.pack-name</c> and then some.</summary>
    public const int MaximumIdLength = 64;

    public static DomainOperationResult<PackManifest> Read(string json)
    {
        PackManifestEnvelope? envelope;
        try
        {
            envelope = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PackManifestEnvelope>(json, PackJson.Options);
        }
        catch (JsonException exception)
        {
            return Fail(MalformedCode, $"The manifest is not valid: {exception.Message}");
        }

        if (envelope is null)
        {
            return Fail(MalformedCode, "The manifest is empty.");
        }

        if (envelope.SchemaVersion != PackManifest.CurrentSchemaVersion)
        {
            return Fail(
                UnsupportedSchemaCode,
                $"The manifest is schema version {envelope.SchemaVersion}; this build reads version {PackManifest.CurrentSchemaVersion}.");
        }

        var errors = new List<DomainError>();

        if (!IsValidId(envelope.Id))
        {
            errors.Add(Invalid("id", $"'{envelope.Id}' is not a pack id: 3 to {MaximumIdLength} lower-case letters, digits, '.' and '-', with at least one '.', like 'yourname.pack-name'."));
        }

        if (string.IsNullOrWhiteSpace(envelope.Name))
        {
            errors.Add(Invalid("name", "Every pack needs a name."));
        }

        if (!PackVersion.TryParse(envelope.Version, out var version))
        {
            errors.Add(Invalid("version", $"'{envelope.Version}' is not a version: three numbers, like 1.0.0."));
        }

        if (!Enum.TryParse<PackKind>(envelope.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || envelope.Kind!.Any(char.IsAsciiDigit))
        {
            errors.Add(Invalid("kind", $"'{envelope.Kind}' is not a pack kind. One of: {string.Join(", ", Enum.GetNames<PackKind>().Select(name => name.ToLowerInvariant()))}."));
        }

        var requires = References(envelope.Requires, "requires", errors);
        var conflicts = References(envelope.Conflicts, "conflicts", errors);
        var targets = References(envelope.Targets, "targets", errors);
        var content = Content(envelope.Content, kind, errors);
        var fidelity = Fidelity(envelope.Fidelity, kind, errors);

        if (envelope.Id is not null && requires.Concat(conflicts).Any(reference => reference.PackId == envelope.Id))
        {
            errors.Add(Invalid("requires", "A pack cannot require or conflict with itself."));
        }

        if (targets.Count > 0 && kind is not (PackKind.Faces or PackKind.Logos or PackKind.Colours or PackKind.League))
        {
            errors.Add(Invalid("targets", "Only a cosmetic pack (which targets league packs) or a league pack (which targets ruleset packs) states targets."));
        }

        return errors.Count > 0
            ? DomainOperationResult<PackManifest>.Failure(errors.ToArray())
            : DomainOperationResult<PackManifest>.Success(new PackManifest(
                envelope.SchemaVersion,
                envelope.Id!,
                envelope.Name!.Trim(),
                version,
                kind,
                Blank(envelope.Author),
                Blank(envelope.Description),
                requires,
                conflicts,
                targets,
                content,
                fidelity));
    }

    /// <summary>Lower-case ASCII letters, digits, '.' and '-'; starts with a letter; at least one '.'; no empty segment.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: >= 3 and <= MaximumIdLength } &&
        char.IsAsciiLetterLower(id[0]) &&
        id.Contains('.', StringComparison.Ordinal) &&
        id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-') &&
        id.Split('.').All(segment => segment.Length > 0);

    private static List<PackReference> References(IReadOnlyList<PackReferenceEnvelope>? references, string field, List<DomainError> errors)
    {
        var parsed = new List<PackReference>();
        foreach (var (reference, index) in (references ?? []).Select((reference, index) => (reference, index)))
        {
            if (!IsValidId(reference.Pack))
            {
                errors.Add(Invalid($"{field}[{index}]", $"'{reference.Pack}' is not a pack id."));
                continue;
            }

            if (!VersionRange.TryParse(reference.Versions, out var range))
            {
                errors.Add(Invalid($"{field}[{index}]", $"'{reference.Versions}' is not a version range. Write '*', '1.2.0', '^1.2.0', or comparators like '>=1.2.0 <2.0.0'."));
                continue;
            }

            parsed.Add(new PackReference(reference.Pack!, range));
        }

        return parsed;
    }

    private static PackContent Content(PackContentEnvelope? content, PackKind kind, List<DomainError> errors)
    {
        if (content is null)
        {
            return PackContent.None;
        }

        var fileKind = kind is PackKind.Ruleset or PackKind.League or PackKind.Setup or PackKind.Colours;
        var imageKind = kind is PackKind.Faces or PackKind.Logos;
        if (content.File is not null && !fileKind)
        {
            errors.Add(Invalid("content.file", $"A {kind.ToString().ToLowerInvariant()} pack keeps its images in a folder; 'file' does not apply."));
        }

        if ((content.Folder is not null || content.Map is not null) && !imageKind)
        {
            errors.Add(Invalid("content", $"'folder' and 'map' apply only to faces and logos packs, not to a {kind.ToString().ToLowerInvariant()} pack."));
        }

        if (content.LeagueLogo is not null && kind != PackKind.Logos)
        {
            errors.Add(Invalid("content.leagueLogo", "Only a logos pack carries a league logo."));
        }

        return new PackContent(content.File, content.Folder, content.Map, content.LeagueLogo);
    }

    private static List<FidelityNote> Fidelity(IReadOnlyList<FidelityNoteEnvelope>? notes, PackKind kind, List<DomainError> errors)
    {
        if (notes is null)
        {
            return [];
        }

        if (kind != PackKind.Ruleset)
        {
            errors.Add(Invalid("fidelity", "Only a ruleset pack states how faithfully it expresses a league's rules."));
            return [];
        }

        var parsed = new List<FidelityNote>();
        foreach (var (note, index) in notes.Select((note, index) => (note, index)))
        {
            if (string.IsNullOrWhiteSpace(note.Rule) || string.IsNullOrWhiteSpace(note.Detail) ||
                !Enum.TryParse<FidelityHandling>(note.Handling, ignoreCase: true, out var handling) || !Enum.IsDefined(handling) ||
                note.Handling!.Any(char.IsAsciiDigit))
            {
                errors.Add(Invalid($"fidelity[{index}]", "Each note states a rule, a handling ('approximated' or 'notModelled'), and a detail."));
                continue;
            }

            parsed.Add(new FidelityNote(note.Rule.Trim(), handling, note.Detail.Trim()));
        }

        return parsed;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static DomainError Invalid(string field, string message) => new(InvalidFieldCode, $"{field}: {message}");

    private static DomainOperationResult<PackManifest> Fail(string code, string message) =>
        DomainOperationResult<PackManifest>.Failure(new DomainError(code, message));
}
