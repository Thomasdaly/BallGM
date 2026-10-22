using System.Text.Json;
using System.Text.Json.Serialization;

namespace BallGM.Mods.Manifests;

/// <summary>
/// The JSON reading rules for every file a pack author writes by hand — manifests, setups, maps and
/// colour tables. Comments and trailing commas are allowed, because these are files people edit in a
/// text editor and a stray comma should not be a support ticket; unknown fields are not, because a
/// field this build does not know is an instruction this build would silently ignore.
/// </summary>
internal static class PackJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
