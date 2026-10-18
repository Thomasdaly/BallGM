using BallGM.Client.Avalonia.Imaging;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// The few app-wide services the component library reads (the asset resolver, motion and entity-ID
/// preferences). Set once by the composition root - or by a test - before any window opens; controls
/// fall back to working defaults when nothing has been set.
/// </summary>
public static class UiServices
{
    private static IAssetResolver? _assets;

    public static IAssetResolver Assets
    {
        get => _assets ??= new AssetResolver(() => AssetCatalog.Empty);
        set => _assets = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Reduced motion: no slides, scaling, stagger or count-ups (spec 01 section 10).</summary>
    public static bool ReducedMotion { get; set; }

    /// <summary>Show entity IDs on profiles and hover cards (Settings, Mods).</summary>
    public static bool ShowEntityIds { get; set; }

    /// <summary>Raised when a preference controls draw from changes, so live controls refresh.</summary>
    public static event EventHandler? PreferencesChanged;

    public static void RaisePreferencesChanged() => PreferencesChanged?.Invoke(null, EventArgs.Empty);
}
