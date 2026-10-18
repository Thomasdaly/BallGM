using BallGM.Client.Avalonia.Formatting;

namespace BallGM.Client.Avalonia.Settings;

public enum Density
{
    Comfortable,
    Compact,
}

public enum RatingPalette
{
    Default,
    ColourVisionSafe,
}

/// <summary>A remembered window: position, size, and whether it was maximised or full screen.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximised, bool FullScreen);

/// <summary>
/// The interface's own options (spec 04 S29). Presentation only - none of these reach the league,
/// the rules or a save. Persisted by <see cref="UiSettingsStore"/>.
/// </summary>
public sealed record UiSettings
{
    public const int MinimumScale = 90;
    public const int MaximumScale = 150;

    public int UiScalePercent { get; init; } = 100;

    public Density Density { get; init; } = Density.Comfortable;

    public bool RememberWindow { get; init; } = true;

    public WindowPlacement? Window { get; init; }

    public bool TeamColours { get; init; } = true;

    public RatingPalette RatingPalette { get; init; } = RatingPalette.Default;

    public bool Ticker { get; init; } = true;

    public bool ReducedMotion { get; init; }

    public UnitSystem Units { get; init; } = UnitSystem.Imperial;

    public string DatePattern { get; init; } = "ddd d MMM";

    public bool ShowEntityIds { get; init; }

    /// <summary>Null follows the window width (collapsed below 1440); true or false is the user's choice.</summary>
    public bool? NavCollapsed { get; init; }

    public int CacheBudgetMegabytes { get; init; } = 256;

    /// <summary>Visible columns per table, by table key.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Columns { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string> RecentSearches { get; init; } = [];

    public UiSettings Normalised() => this with
    {
        UiScalePercent = Math.Clamp(UiScalePercent, MinimumScale, MaximumScale),
        CacheBudgetMegabytes = Math.Clamp(CacheBudgetMegabytes, 64, 2048),
        DatePattern = DatePattern is "ddd d MMM" or "MMM d" or "d MMM yyyy" or "yyyy-MM-dd" ? DatePattern : "ddd d MMM",
        RecentSearches = RecentSearches.Take(10).ToList(),
    };
}
