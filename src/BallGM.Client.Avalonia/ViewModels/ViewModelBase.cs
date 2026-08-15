using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// Minimal change notification. Deliberately hand-rolled rather than pulling in an MVVM toolkit:
/// this milestone is a thin slice, and a UI framework dependency is a decision for the
/// UI-hardening milestone, not a side effect of the first playable screen.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
