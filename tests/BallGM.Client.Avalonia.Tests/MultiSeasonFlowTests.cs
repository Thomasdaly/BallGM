using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Tests;

/// <summary>
/// A multi-season save in the client (<c>docs/franchise-realism.md</c> → F8): Continue starts a season,
/// plays it to its end, concludes it — awards and the summer's moves on the season review — and starts
/// the next one.
/// </summary>
public sealed class MultiSeasonFlowTests
{
    [AvaloniaFact]
    public void ContinueConcludesAFinishedSeason_ShowsItsReview_AndStartsTheNext()
    {
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        var season = shell.Season!;

        shell.ContinueCommand.Execute(null);
        Assert.True(season.HasSeason);
        season.AdvanceToEndCommand.Execute(null);
        Assert.True(season.IsSeasonComplete);
        Assert.Equal("CONCLUDE SEASON", shell.ContinueLabel);

        shell.ContinueCommand.Execute(null);

        Assert.True(season.HasReview, season.Message);
        var review = season.Review!;
        Assert.StartsWith("Champion: ", review.ChampionLine, StringComparison.Ordinal);
        Assert.Contains(review.Awards, award => award.AwardName == "Most Valuable Player" && award.PlayerName.Length > 0);
        Assert.Contains("re-signed", review.Summer, StringComparison.Ordinal);
        Assert.Equal("START SEASON", shell.ContinueLabel);

        shell.ContinueCommand.Execute(null);

        Assert.True(season.HasSeason);
        Assert.False(season.HasReview);
        Assert.False(season.IsSeasonComplete);
    }
}
