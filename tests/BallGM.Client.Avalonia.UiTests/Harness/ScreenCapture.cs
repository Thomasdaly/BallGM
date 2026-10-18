namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>Screenshot file naming per spec 06 section 2: {ScreenID}_{slug}_{W}x{H}@{scale}.png.</summary>
internal static class ScreenCapture
{
    public static readonly (int Width, int Height)[] BaselineSizes = [(1280, 720), (1920, 1080), (2560, 1440)];

    public static string FileName(string screenId, string slug, int width, int height, int scalePercent, string? suffix = null) =>
        $"{screenId}_{slug}_{width}x{height}@{scalePercent}{(suffix is null ? string.Empty : "_" + suffix)}.png";

    public static string Save(ShellDriver shell, string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var frame = shell.Capture();
        frame.Save(path);
        return path;
    }
}
