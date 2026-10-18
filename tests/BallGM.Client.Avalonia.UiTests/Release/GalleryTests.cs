using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.UiTests.Harness;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Client.Avalonia.Views.Gallery;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>The Component Gallery (S33): every section renders, with no binding errors, and is captured.</summary>
[Trait("Category", "UiRelease")]
public sealed class GalleryTests
{
    [AvaloniaFact]
    public void Gallery_RendersEverySectionAndCapturesIt()
    {
        UiServices.ReducedMotion = true;
        var sink = BindingLogSink.Install();
        var viewModel = new ComponentGalleryViewModel();
        var window = new Window { Width = 1920, Height = 1080, Content = new ComponentGalleryView { DataContext = viewModel } };
        window.Classes.Add("reduced-motion");
        window.Show();
        var directory = RepoPaths.Screens("after");
        Directory.CreateDirectory(directory);
        var failures = new List<string>();

        foreach (var team in new[] { viewModel.Teams[0] })
        {
            viewModel.Team = team;
            for (var index = 0; index < viewModel.Sections.Count; index++)
            {
                sink.Clear();
                viewModel.SectionIndex = index;
                ShellDriver.Settle();
                using var frame = window.CaptureRenderedFrame()!;
                frame.Save(Path.Combine(directory, $"S33_gallery-{viewModel.Section.ToLowerInvariant()}_1920x1080@100.png"));

                // The whole section, for review of what sits below the fold.
                window.Height = 2400;
                ShellDriver.Settle();
                using var full = window.CaptureRenderedFrame()!;
                full.Save(Path.Combine(directory, $"S33_gallery-{viewModel.Section.ToLowerInvariant()}_full.png"));
                window.Height = 1080;
                ShellDriver.Settle();
                failures.AddRange(sink.Messages.Select(message => $"{viewModel.Section}: {message}"));
            }
        }

        window.Close();
        Assert.True(failures.Count == 0, "Binding warnings in the gallery:\n" + string.Join('\n', failures));
    }
}
