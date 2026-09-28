using System.Windows.Input;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary><see cref="RelayCommand"/>'s sibling for a button that says which item it is for.</summary>
public sealed class ParameterCommand<T>(Action<T> execute) : ICommand
{
    private readonly Action<T> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            _execute(value);
        }
    }
}
