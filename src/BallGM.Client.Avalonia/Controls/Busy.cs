using Avalonia;
using Avalonia.Controls;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// The loading state of an action (spec 03 section 4): a busy button shows a spinner in place of its
/// label and keeps its width, and does not accept another click.
/// </summary>
public static class Busy
{
    public static readonly AttachedProperty<bool> IsBusyProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsBusy", typeof(Busy));

    static Busy()
    {
        IsBusyProperty.Changed.AddClassHandler<Control>((control, change) =>
            control.Classes.Set("busy", change.GetNewValue<bool>()));
    }

    public static bool GetIsBusy(Control control) => control.GetValue(IsBusyProperty);

    public static void SetIsBusy(Control control, bool value) => control.SetValue(IsBusyProperty, value);
}
