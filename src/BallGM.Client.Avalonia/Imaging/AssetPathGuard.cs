namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// Refuses image paths that leave their pack's folder - by <c>..</c>, by being absolute outside it, or
/// through a symbolic link - before any file is opened (spec 05 section 5). Only path strings and link
/// metadata are read; file contents never are.
/// </summary>
public static class AssetPathGuard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// True when <paramref name="path"/> (absolute, or relative to <paramref name="packRoot"/>) stays
    /// inside the pack root after resolving <c>..</c> and every symbolic link on the way.
    /// </summary>
    public static bool IsInsidePack(string path, string packRoot, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(packRoot) || path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        // A mapping that names its own parent directory is refused outright, wherever it lands.
        if (path.Replace('\\', '/').Split('/').Any(part => part == ".."))
        {
            return false;
        }

        var rootReal = RealPath(Path.GetFullPath(packRoot));
        var candidate = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(packRoot, path));
        if (!IsUnder(candidate, Path.GetFullPath(packRoot)) && !IsUnder(candidate, rootReal))
        {
            return false;
        }

        var real = RealPath(candidate);
        if (!IsUnder(real, rootReal))
        {
            return false;
        }

        resolvedPath = real;
        return true;
    }

    private static bool IsUnder(string path, string root)
    {
        var withSeparator = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(withSeparator, PathComparison);
    }

    /// <summary>The path with every symbolic link along it resolved (a realpath built on link metadata).</summary>
    public static string RealPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < parts.Length; index++)
        {
            var next = Path.Combine(current, parts[index]);
            for (var hops = 0; hops < 32; hops++)
            {
                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (info.LinkTarget is not { } target)
                {
                    break;
                }

                next = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(Path.GetDirectoryName(next) ?? current, target));
                next = RealPath(next);
            }

            current = next;
        }

        return current;
    }
}
