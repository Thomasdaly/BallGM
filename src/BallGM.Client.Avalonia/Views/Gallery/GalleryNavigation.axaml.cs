using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views.Gallery;

public sealed partial class GalleryNavigation : UserControl
{
    public GalleryNavigation()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
