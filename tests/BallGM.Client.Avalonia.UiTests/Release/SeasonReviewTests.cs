using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.UiTests.Harness;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>
/// The season review (franchise F8) on the calendar screen: a full season of the UI fixture league played
/// to its end and concluded through the shell's Continue, rendered at the baseline sizes with no binding
/// errors. Captures go to <c>artifacts/ui-screens/after</c> for visual review.
/// </summary>
public sealed class SeasonReviewTests
{
    [AvaloniaFact]
    public void TheSeasonReviewRendersAfterAConcludedSeason()
    {
        var sink = BindingLogSink.Install();
        var shell = ShellDriver.Open(1920, 1080, midSeason: false);
        var season = shell.ViewModel.Season!;
        season.StartSeasonCommand.Execute(null);
        season.AdvanceToEndCommand.Execute(null);
        shell.ViewModel.ContinueCommand.Execute(null);
        shell.Navigate("Calendar");
        ShellDriver.Settle();

        Assert.True(season.HasReview, season.Message);
        sink.Clear();
        foreach (var (width, height) in ScreenCapture.BaselineSizes)
        {
            shell.Resize(width, height);
            ScreenCapture.Save(shell, RepoPaths.Screens("after"), ScreenCapture.FileName("S12", "season-review", width, height, 100));
        }

        Assert.Empty(sink.Messages);

        // Leaving the calendar raises its known unload warnings (FOLLOWUPS, the S18 render-matrix
        // entries); only the history screen's own render is checked here.
        shell.Navigate("History");
        ShellDriver.Settle();
        sink.Clear();
        foreach (var (width, height) in ScreenCapture.BaselineSizes)
        {
            shell.Resize(width, height);
            ScreenCapture.Save(shell, RepoPaths.Screens("after"), ScreenCapture.FileName("S27", "history", width, height, 100));
        }

        Assert.Empty(sink.Messages);
        shell.Close();
    }
}
