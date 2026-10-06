using Avalonia.Headless.XUnit;

namespace BallGM.Client.Avalonia.Tests;

public sealed class TradeScreenTeamTests
{
    [AvaloniaFact]
    public void TradeScreen_SendsFromTheViewedTeam()
    {
        // Regression: the sending side always opened on the league's first team, whichever team the
        // GM was viewing.
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        var viewed = shell.Teams[2];

        shell.SelectedTeam = viewed;

        Assert.Equal(viewed.TeamId, shell.Trade!.SendingTeam!.TeamId);
        Assert.NotEqual(viewed.TeamId, shell.Trade.ReceivingTeam!.TeamId);
    }

    [AvaloniaFact]
    public void TradeScreen_MovesTheReceivingSideOffTheViewedTeam()
    {
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        var trade = shell.Trade!;
        var receiving = trade.ReceivingTeam!;

        shell.SelectedTeam = shell.Teams.Single(team => team.TeamId == receiving.TeamId);

        Assert.Equal(receiving.TeamId, trade.SendingTeam!.TeamId);
        Assert.NotEqual(trade.SendingTeam.TeamId, trade.ReceivingTeam!.TeamId);
    }

    [AvaloniaFact]
    public void TradeScreen_KeepsTheReceivingSideTheGmChose()
    {
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        var trade = shell.Trade!;
        var chosen = shell.Teams[3];
        trade.ReceivingTeam = chosen;

        shell.SelectedTeam = shell.Teams[1];

        Assert.Equal(chosen.TeamId, trade.ReceivingTeam!.TeamId);
    }
}
