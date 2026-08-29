using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class SeasonView : UserControl
{
    public SeasonView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
