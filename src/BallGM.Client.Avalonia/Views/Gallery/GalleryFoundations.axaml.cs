using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views.Gallery;

public sealed partial class GalleryFoundations : UserControl
{
    public GalleryFoundations()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
