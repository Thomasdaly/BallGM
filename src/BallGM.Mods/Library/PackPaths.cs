namespace BallGM.Mods.Library;

/// <summary>
/// Turns a path a pack author wrote into a file the game may open — or refuses it. A pack can only
/// ever name files inside its own folder: no absolute paths, no <c>..</c>, and no link that leads out
/// of the folder once followed. Checked before any file is touched, so an unsafe path is never even
/// probed for existence.
/// </summary>
public static class PackPaths
{
    public static bool TryResolve(string packDirectory, string? relativePath, out string fullPath, out string reason)
    {
        fullPath = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            reason = "the path is empty";
            return false;
        }

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            reason = $"'{relativePath}' is an absolute path; a pack may only name files inside its own folder";
            return false;
        }

        var segments = relativePath.Split('/', '\\');
        if (segments.Any(segment => segment == ".."))
        {
            reason = $"'{relativePath}' climbs out of the pack folder with '..'";
            return false;
        }

        var root = Path.GetFullPath(packDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!IsInside(root, candidate))
        {
            reason = $"'{relativePath}' resolves outside the pack folder";
            return false;
        }

        // A link inside the folder that points outside it is an escape the string checks cannot see.
        try
        {
            var target = File.Exists(candidate) || Directory.Exists(candidate)
                ? new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)
                : null;
            if (target is not null && !IsInside(root, Path.GetFullPath(target.FullName)))
            {
                reason = $"'{relativePath}' is a link to a file outside the pack folder";
                return false;
            }
        }
        catch (IOException exception)
        {
            reason = $"'{relativePath}' could not be followed: {exception.Message}";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static bool IsInside(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }
}
