using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class PlayerProfileView : UserControl
{
    public PlayerProfileView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
