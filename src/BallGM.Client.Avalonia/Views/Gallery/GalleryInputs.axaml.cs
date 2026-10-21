using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BallGM.Client.Avalonia.Views.Gallery;

public sealed partial class GalleryInputs : UserControl
{
    public GalleryInputs()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Shows the keyboard focus ring on a text field (the adorner needs real keyboard focus).</summary>
    protected override void OnPropertyChanged(global::Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                () => this.FindControl<TextBox>("FocusField")?.Focus(global::Avalonia.Input.NavigationMethod.Tab),
                global::Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}
