using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class PickBoardView : UserControl
{
    public PickBoardView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
