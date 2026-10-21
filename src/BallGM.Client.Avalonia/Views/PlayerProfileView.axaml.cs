using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BallGM.Client.Avalonia.Theming;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Views;

public sealed partial class PlayerProfileView : UserControl
{
    public PlayerProfileView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The page takes the player's team colours (spec 01 section 4, page scope); a free agent's page is neutral.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var colours = (DataContext as PlayerProfileViewModel)?.TeamColours;
        ThemeResources.ScopeTeam(this, TeamTheme.From(colours?.Primary, colours?.Secondary));
    }
}
