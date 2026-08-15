using System.Windows.Input;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>Smallest possible <see cref="ICommand"/> so buttons can bind without an MVVM toolkit.</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
