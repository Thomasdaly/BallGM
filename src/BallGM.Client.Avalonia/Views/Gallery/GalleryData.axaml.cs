using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views.Gallery;

public sealed partial class GalleryData : UserControl
{
    public GalleryData()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
