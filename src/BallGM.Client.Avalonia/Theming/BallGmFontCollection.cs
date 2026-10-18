using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// The embedded faces, as two collections so a family name plus a weight finds the right file.
/// <para>
/// One collection was not enough: the embedded collection folds "Barlow Condensed" into "Barlow" and
/// matches on weight and style only, so every display heading silently rendered in regular-width
/// Barlow. Display (Barlow Condensed) and body (Barlow) faces now live in their own folders and
/// collections, so a weight lookup can only land on a face of the right width.
/// </para>
/// <para>
/// Registered by <see cref="App"/> itself rather than by an <c>AppBuilder</c> call, so every host of
/// the app - the desktop program and the headless test host alike - has it: a typeface key that
/// names an unregistered collection throws while measuring text instead of falling back.
/// </para>
/// </summary>
internal sealed class BallGmFontCollection : EmbeddedFontCollection
{
    public BallGmFontCollection(string key, string folder)
        : base(new Uri("fonts:" + key, UriKind.Absolute), new Uri("avares://BallGM.Client.Avalonia/Assets/Fonts/" + folder, UriKind.Absolute))
    {
    }
}

internal static class BallGmFonts
{
    public const string DisplayCollection = "BallGMDisplay";
    public const string BodyCollection = "BallGMBody";

    public static void Register()
    {
        FontManager.Current.AddFontCollection(new BallGmFontCollection(DisplayCollection, "Display"));
        FontManager.Current.AddFontCollection(new BallGmFontCollection(BodyCollection, "Body"));
    }
}
