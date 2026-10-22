namespace BallGM.Mods.Library;

/// <summary>
/// The two places packs live: the game's own <c>data/packs</c> next to the executable (read-only,
/// built-in presets), and <c>mods</c> under the player's data folder — the same per-user root the
/// client keeps settings and saves in, overridable with <see cref="DataRootEnvironmentVariable"/>.
/// One folder per pack, on every platform; no registry keys, no paths to copy from a forum post.
/// </summary>
public static class PackFolders
{
    public const string DataRootEnvironmentVariable = "BALLGM_DATA_ROOT";

    public static string BuiltIn => Path.Combine(AppContext.BaseDirectory, "data", "packs");

    public static string User
    {
        get
        {
            var stated = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            var root = string.IsNullOrWhiteSpace(stated)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BallGM")
                : stated;
            return Path.Combine(root, "mods");
        }
    }

    /// <summary>The built-in folder, then the user folder, then any extra folders given.</summary>
    public static IReadOnlyList<PackRoot> Default(IEnumerable<string>? extraUserFolders = null) =>
        new[] { new PackRoot(BuiltIn, PackOrigin.BuiltIn), new PackRoot(User, PackOrigin.User) }
            .Concat((extraUserFolders ?? []).Select(folder => new PackRoot(folder, PackOrigin.User)))
            .ToList();
}
