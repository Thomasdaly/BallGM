using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// The embedded Barlow faces, registered as one collection so a family name plus a weight finds the
/// right file. A plain <c>avares://…#Barlow Condensed</c> lookup matched only the faces whose legacy
/// family name is exactly that (Bold), so SemiBold headings silently fell back.
/// <para>
/// Registered by <see cref="App"/> itself rather than by an <c>AppBuilder</c> call, so every host of
/// the app - the desktop program and the headless test host alike - has it: a typeface key that
/// names an unregistered collection throws while measuring text instead of falling back.
/// </para>
/// </summary>
internal sealed class BallGmFontCollection : EmbeddedFontCollection
{
    public BallGmFontCollection()
        : base(new Uri("fonts:BallGM", UriKind.Absolute), new Uri("avares://BallGM.Client.Avalonia/Assets/Fonts", UriKind.Absolute))
    {
    }
}

internal static class BallGmFonts
{
    public static void Register() => FontManager.Current.AddFontCollection(new BallGmFontCollection());
}
