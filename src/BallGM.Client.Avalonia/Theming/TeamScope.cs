using Avalonia;
using Avalonia.Controls;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.Imaging;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// Scopes a part of a page to one team's colours (spec 01 section 4, element scope): the team brushes
/// under the control resolve to that team's derived theme, the rest of the page keeps its own.
/// </summary>
public static class TeamScope
{
    public static readonly AttachedProperty<string?> TeamIdProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("TeamId", typeof(TeamScope));

    static TeamScope()
    {
        TeamIdProperty.Changed.AddClassHandler<Control>((control, change) => Apply(control, change.GetNewValue<string?>()));
    }

    public static string? GetTeamId(Control control) => control.GetValue(TeamIdProperty);

    public static void SetTeamId(Control control, string? value) => control.SetValue(TeamIdProperty, value);

    private static void Apply(Control control, string? teamId)
    {
        var entry = string.IsNullOrEmpty(teamId) ? null : UiServices.Assets?.Describe(new AssetKey(AssetEntityKind.Team, teamId));
        ThemeResources.ScopeTeam(control, TeamTheme.From(entry?.PrimaryColour, entry?.SecondaryColour));
    }
}
