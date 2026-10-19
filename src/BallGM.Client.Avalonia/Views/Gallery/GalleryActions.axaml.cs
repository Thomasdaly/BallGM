using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views.Gallery;

public sealed partial class GalleryActions : UserControl
{
    public GalleryActions()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Shows the keyboard focus ring on a stock toggle (pinned states cannot fake real focus).</summary>
    protected override void OnPropertyChanged(global::Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                () => this.FindControl<ToggleSwitch>("FocusSample")?.Focus(global::Avalonia.Input.NavigationMethod.Tab),
                global::Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}
