namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>Where the repository is, found by walking up from the test binaries to the solution file.</summary>
internal static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BallGM.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("BallGM.slnx was not found above the test binaries.");
    });

    public static string Root => RootPath.Value;

    /// <summary>The fictional 30-team league pack (tests/Fixtures/UiLeague), loaded through the real pack loader.</summary>
    public static string UiLeaguePack => Path.Combine(Root, "tests", "Fixtures", "UiLeague", "ui-league.json");

    /// <summary>artifacts/ui-screens/{set}: gitignored screenshot output.</summary>
    public static string Screens(string set) => Path.Combine(Root, "artifacts", "ui-screens", set);
}
