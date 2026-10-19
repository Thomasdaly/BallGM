using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>
/// Every inventory screen (docs/ui/AUDIT.md section 4) and how the real shell reaches it. The render
/// matrix, the layout check, the keyboard test and the performance table all walk this list.
/// </summary>
internal static class ScreenCatalog
{
    public sealed record Screen(string Id, string Slug, bool IsP0, Action<ShellDriver> Reach);

    public static readonly IReadOnlyList<Screen> All =
    [
        new("S07", "roster", true, shell => shell.Navigate("Roster")),
        new("S10", "player-profile", true, OpenTopPlayer),
        new("S12", "calendar", true, shell => shell.Navigate("Calendar")),
        new("S17", "box-score", true, OpenLatestGame),
        new("S18", "trade", true, shell => shell.Navigate("Trade")),
        new("S19", "free-agency", true, shell => shell.Navigate("Free agency")),
        new("S20", "free-agency-board", false, shell => shell.Navigate("Free agency board")),
        new("S14", "leaders", false, shell => shell.Navigate("Leaders")),
        new("S23", "cap-sheet", false, shell => shell.Navigate("Cap sheet")),
        new("S34", "contracts", false, shell => shell.Navigate("Contracts")),
        new("S35", "pick-board", false, shell => shell.Navigate("Pick board")),
        new("S36", "front-office", false, shell => shell.Navigate("Front office")),
        new("S28", "search", false, OpenSearch),
    ];

    public static void OpenTopPlayer(ShellDriver shell)
    {
        shell.Navigate("Roster");
        var roster = (RosterViewModel)shell.ViewModel.CurrentScreen!;
        shell.ViewModel.OpenPlayer(roster.Rows.First().PlayerId);
        ShellDriver.Settle();
    }

    public static void OpenLatestGame(ShellDriver shell)
    {
        shell.Navigate("Calendar");
        var season = shell.ViewModel.Season!;
        if (season.RecentResults.LastOrDefault(fixture => fixture.Played) is { } game)
        {
            season.ShowGameCommand.Execute(game.GameId);
        }

        ShellDriver.Settle();
    }

    public static void OpenSearch(ShellDriver shell)
    {
        shell.Navigate("Roster");
        shell.ViewModel.IsSearchOpen = true;
        shell.ViewModel.SearchText = "Ok";
        ShellDriver.Settle();
    }
}
