using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class FrontOfficeView : UserControl
{
    public FrontOfficeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
