using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Tests;

public sealed class FreeAgencyBoardTableTests
{
    private static (MainWindowViewModel Shell, FreeAgencyBoardViewModel Board) OpenBoard()
    {
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        var board = shell.FreeAgencyBoard!;
        return (shell, board);
    }

    [AvaloniaFact]
    public void Table_ListsEveryFreeAgentOnceBestFirst()
    {
        var (_, board) = OpenBoard();

        Assert.NotEmpty(board.Rows);
        Assert.Equal(board.Rows.Count, board.Rows.Select(row => row.PlayerId).Distinct().Count());
        Assert.Equal(board.Rows.Select(row => row.Overall).OrderByDescending(overall => overall), board.Rows.Select(row => row.Overall));
    }

    [AvaloniaFact]
    public void PickingAPositionFromTheDepthStrip_NarrowsTheTableToIt()
    {
        var (_, board) = OpenBoard();
        var column = board.Columns.First(candidateColumn => candidateColumn.HasCandidates);

        board.SelectedColumn = column;

        Assert.Equal(column.Position, board.PositionFilter);
        Assert.All(board.Rows, row => Assert.Equal(column.Position, row.Position));

        board.SelectedColumn = null; // the strip clearing itself is not "show all"
        Assert.Equal(column.Position, board.PositionFilter);

        board.ShowAllPositionsCommand.Execute(null);
        Assert.Null(board.PositionFilter);
        Assert.True(board.IsShowingAllPositions);
    }

    [AvaloniaFact]
    public void SortingByAColumnTwice_ReversesIt()
    {
        var (_, board) = OpenBoard();

        board.SortCommand.Execute(FreeAgencyBoardViewModel.SortAge);
        var oldestFirst = board.Rows.Select(row => row.Age).ToList();
        board.SortCommand.Execute(FreeAgencyBoardViewModel.SortAge);
        var youngestFirst = board.Rows.Select(row => row.Age).ToList();

        Assert.Equal(oldestFirst.OrderByDescending(age => age), oldestFirst);
        Assert.Equal(youngestFirst.OrderBy(age => age), youngestFirst);
    }

    [AvaloniaFact]
    public void PlacingAnOffer_KeepsThePlayerSelectedWhileTheTableIsRebuilt()
    {
        // Regression: the rebuilt table replaced the list's items, the list pushed a null selection,
        // and the panel dropped the player the GM had just made an offer to.
        var (shell, board) = OpenBoard();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.SelectedSection = board.Title;

        var player = board.Rows[0];
        board.Candidate = player;
        board.OfferCommand.Execute(null);

        Assert.NotNull(board.Candidate);
        Assert.Equal(player.PlayerId, board.Candidate!.PlayerId);
    }
}
