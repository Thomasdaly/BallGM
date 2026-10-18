using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.UiTests.Harness;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.UiTests;

/// <summary>
/// The screens that existed before the UI overhaul, driven through the real shell and captured at the
/// three 100% sizes (spec 06 section 2), with the good mod pack enabled. With <c>BALLGM_UI_CAPTURE_BEFORE=1</c> the images go to
/// <c>artifacts/ui-screens/before</c> — run that once, on the untouched UI. Otherwise they go to a
/// temporary folder, so the test still proves every baseline screen renders.
/// </summary>
public sealed class BaselineCaptureTests
{
    /// <summary>Screen ID, file slug, and how the shell reaches it. IDs follow docs/ui/AUDIT.md section 4.</summary>
    internal static readonly (string Id, string Slug, Action<ShellDriver> Reach)[] BaselineScreens =
    [
        ("S07", "roster", shell => shell.Navigate("Roster")),
        ("S10", "player-profile", OpenTopPlayerProfile),
        ("S23", "cap-sheet", shell => shell.Navigate("Cap sheet")),
        ("S34", "contracts", shell => shell.Navigate("Contracts")),
        ("S35", "pick-board", shell => shell.Navigate("Pick board")),
        ("S18", "trade", shell => shell.Navigate("Trade")),
        ("S19", "free-agency", shell => shell.Navigate("Free agency")),
        ("S20", "free-agency-board", shell => shell.Navigate("Free agency board")),
        ("S12", "calendar", shell => shell.Navigate("Calendar")),
        ("S17", "box-score", OpenLatestUserGame),
        ("S14", "leaders", shell => shell.Navigate("Leaders")),
        ("S36", "front-office", shell => shell.Navigate("Front office")),
    ];

    [AvaloniaFact]
    public void Baseline_EveryScreenRendersAtTheThreeBaselineSizes()
    {
        var capturingBefore = Environment.GetEnvironmentVariable("BALLGM_UI_CAPTURE_BEFORE") == "1";
        var directory = capturingBefore
            ? RepoPaths.Screens("before")
            : Path.Combine(Path.GetTempPath(), "ballgm-ui-baseline-" + Guid.NewGuid().ToString("N"));
        var sink = BindingLogSink.Install();
        var bindingLog = new List<string>();
        var written = new List<string>();

        // One league for every size: identifiers are minted per load, so two loads are two different
        // leagues (CLAUDE.md), and the three sizes should show the same one.
        var (firstWidth, firstHeight) = ScreenCapture.BaselineSizes[0];
        var shell = ShellDriver.Open(firstWidth, firstHeight, ModFixtures.Build(ModFixtures.Kind.Good));
        var failed = ShellDriver.Open(firstWidth, firstHeight, Path.Combine(Path.GetTempPath(), "ballgm-missing-pack-" + Guid.NewGuid().ToString("N") + ".json"));
        foreach (var (width, height) in ScreenCapture.BaselineSizes)
        {
            shell.Resize(width, height);
            foreach (var (id, slug, reach) in BaselineScreens)
            {
                sink.Clear();
                reach(shell);
                written.Add(ScreenCapture.Save(shell, directory, ScreenCapture.FileName(id, slug, width, height, 100)));
                bindingLog.AddRange(sink.Messages.Select(message => $"{id} {width}x{height}: {message}"));
            }

            failed.Resize(width, height);
            written.Add(ScreenCapture.Save(failed, directory, ScreenCapture.FileName("S37", "load-error", width, height, 100)));
        }

        shell.Close();
        failed.Close();

        File.WriteAllLines(Path.Combine(directory, "binding-log.txt"), bindingLog);
        Assert.Equal((BaselineScreens.Length + 1) * ScreenCapture.BaselineSizes.Length, written.Count(File.Exists));
    }

    private static void OpenTopPlayerProfile(ShellDriver shell)
    {
        shell.Navigate("Roster");
        var roster = Assert.IsType<RosterViewModel>(shell.ViewModel.CurrentScreen);
        var top = roster.Rows.First();
        roster.OpenProfileCommand.Execute(top.PlayerId);
        ShellDriver.Settle();
    }

    private static void OpenLatestUserGame(ShellDriver shell)
    {
        shell.Navigate("Calendar");
        var season = shell.ViewModel.Season!;
        var game = season.RecentResults.LastOrDefault(fixture => fixture.Played);
        if (game is not null)
        {
            season.ShowGameCommand.Execute(game.GameId);
        }

        ShellDriver.Settle();
    }
}
