using BallGM.Domain.Common;
using BallGM.Mods.Library;
using BallGM.Mods.Manifests;

namespace BallGM.Mods.Setups;

/// <summary>
/// Finds a setup by what a player would type: a path to a setup file, or the id of an installed
/// setup pack (one the mod scene shipped, or a built-in one).
/// </summary>
public static class SetupLocator
{
    public const string NotFoundCode = "setup.not_found";

    public static DomainOperationResult<LeagueSetup> Locate(string fileOrPackId, PackLibrary library)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileOrPackId);
        ArgumentNullException.ThrowIfNull(library);

        string path;
        if (File.Exists(fileOrPackId))
        {
            path = fileOrPackId;
        }
        else if (library.Find(fileOrPackId) is { } pack)
        {
            if (pack.Manifest.Kind != PackKind.Setup)
            {
                return Fail($"'{fileOrPackId}' is a {pack.Manifest.Kind.ToString().ToLowerInvariant()} pack, not a setup.");
            }

            if (!PackPaths.TryResolve(pack.Directory, pack.Manifest.Content.FileFor(PackKind.Setup), out path, out var reason))
            {
                return Fail($"The setup file of '{fileOrPackId}' was refused: {reason}.");
            }
        }
        else
        {
            var setups = library.OfKind(PackKind.Setup).Select(candidate => candidate.Id).ToList();
            return Fail($"'{fileOrPackId}' is neither a setup file nor an installed setup pack.{(setups.Count == 0 ? string.Empty : $" Installed setups: {string.Join(", ", setups)}.")}");
        }

        try
        {
            return LeagueSetupReader.Read(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"The setup file '{path}' could not be read: {exception.Message}");
        }
    }

    private static DomainOperationResult<LeagueSetup> Fail(string message) =>
        DomainOperationResult<LeagueSetup>.Failure(new DomainError(NotFoundCode, message));
}
