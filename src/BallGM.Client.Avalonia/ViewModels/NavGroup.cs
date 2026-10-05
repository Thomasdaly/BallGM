namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One heading of the sidebar (squad, market, league) and the screens under it. Each group is its
/// own list, so its <see cref="Selected"/> is the shell's selection when that screen is in this
/// group and null otherwise; the null a list pushes when another group takes the selection is
/// ignored rather than clearing the screen.
/// </summary>
public sealed class NavGroup : ViewModelBase
{
    private readonly Func<string> _currentSection;
    private readonly Action<string> _select;

    public NavGroup(string name, IReadOnlyList<string> sections, Func<string> currentSection, Action<string> select)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(currentSection);
        ArgumentNullException.ThrowIfNull(select);

        Name = name;
        Sections = sections;
        _currentSection = currentSection;
        _select = select;
    }

    public string Name { get; }

    public IReadOnlyList<string> Sections { get; }

    public string? Selected
    {
        get => Sections.Contains(_currentSection()) ? _currentSection() : null;
        set
        {
            if (value is not null)
            {
                _select(value);
            }
        }
    }

    internal void SelectionChanged() => RaisePropertyChanged(nameof(Selected));
}
