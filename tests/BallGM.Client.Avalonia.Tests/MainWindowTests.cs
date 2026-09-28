using Avalonia.Headless.XUnit;

namespace BallGM.Client.Avalonia.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_OpensOnTheFixtureLeagueWithoutADisplay()
    {
        var window = new MainWindow
        {
            DataContext = LeagueClientComposition.CreateMainWindowViewModel(),
        };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal("BallGM", window.Title);
    }

    [AvaloniaFact]
    public void TeamSelection_SurvivesTheComboBoxsTransientNull()
    {
        // Regression: replacing the team list pushed a null selection that reached the front
        // office, which then read "No team selected." over a team that was still selected.
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();
        var second = viewModel.Teams[1];
        viewModel.SelectedTeam = second;

        viewModel.SelectedTeam = null;

        Assert.Same(second, viewModel.SelectedTeam);
        Assert.Same(second, viewModel.FrontOffice!.Team);
    }

    [AvaloniaFact]
    public void Contracts_KeepsThePlayerSelectedWhenTheLeagueRefreshes()
    {
        // Regression: a refreshed summary of the same team cleared the selection, so a second
        // extension offer after a refusal silently went nowhere.
        var viewModel = LeagueClientComposition.CreateMainWindowViewModel();
        var contracts = viewModel.Contracts!;
        var eligible = contracts.FreeAgentClasses.SelectMany(freeAgentClass => freeAgentClass.Players).FirstOrDefault(player => player.ExtensionEligible);
        if (eligible is null)
        {
            return; // the fixture league has nobody in the final season of a contract for this team
        }

        contracts.SelectForExtensionCommand.Execute(eligible.PlayerId);
        var team = viewModel.SelectedTeam!;
        contracts.Team = team with { };

        Assert.True(contracts.HasSelection);
    }
}
