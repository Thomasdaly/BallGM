using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class RosterView : UserControl
{
    public RosterView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
