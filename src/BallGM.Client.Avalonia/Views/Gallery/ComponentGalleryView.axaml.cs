using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views.Gallery;

public sealed partial class ComponentGalleryView : UserControl
{
    public ComponentGalleryView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
