using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.UiTests.Harness;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>The data table under load (spec 06 section 3, Table_Virtualization).</summary>
[Trait("Category", "UiRelease")]
public sealed class TableTests
{
    [AvaloniaFact]
    public void Table_Virtualization()
    {
        UiServices.ReducedMotion = true;
        var gallery = new ComponentGalleryViewModel();
        var table = new DataTable { Columns = gallery.TableColumns, Rows = ComponentGalleryViewModel.SampleRows(1_000) };
        var window = new Window { Width = 1920, Height = 1080, Content = table };
        window.Classes.Add("reduced-motion");
        window.Show();
        ShellDriver.Settle();
        var list = table.RowList ?? throw new InvalidOperationException("The table has no row list.");
        var counts = new List<(string Where, int Realized)>();

        counts.Add(("top", list.GetRealizedContainers().Count()));
        foreach (var (where, index) in new[] { ("middle", 500), ("end", 999) })
        {
            list.ScrollIntoView(index);
            ShellDriver.Settle();
            counts.Add((where, list.GetRealizedContainers().Count()));
        }

        window.Close();
        Assert.Equal(1_000, table.RowViews.Count);
        foreach (var (where, realized) in counts)
        {
            Assert.True(realized is > 0 and < 100, $"{realized} row containers realized at the {where} of a 1,000-row table.");
        }
    }

    [AvaloniaFact]
    public void Table_SortsThroughItsOwnerAndShowsTheGlyph()
    {
        var gallery = new ComponentGalleryViewModel();
        var table = new DataTable { Columns = gallery.TableColumns, Rows = gallery.TableRows, SortKey = "ovr", SortCommand = gallery.TableSortCommand };
        var header = table.ScrollingHeaders.Single(view => view.Column.Key == "ovr");
        Assert.True(header.IsSorted);
        Assert.Equal("▼", header.Glyph);

        table.HeaderSortCommand.Execute(table.ScrollingHeaders.Single(view => view.Column.Key == "age"));
        Assert.Equal("age", gallery.TableSort);
    }

    [AvaloniaFact]
    public void Table_ColumnChooserHidesAndRemembersColumns()
    {
        var gallery = new ComponentGalleryViewModel();
        var table = new DataTable { Columns = gallery.TableColumns, Rows = gallery.TableRows };
        table.ColumnChoices.Single(choice => choice.Column.Key == "pts").IsShown = false;

        Assert.Contains("pts", table.HiddenColumns!);
        Assert.DoesNotContain(table.ScrollingHeaders, header => header.Column.Key == "pts");
    }

    [AvaloniaFact]
    public void Table_WithNoRowsShowsItsEmptyState()
    {
        var gallery = new ComponentGalleryViewModel();
        var table = new DataTable { Columns = gallery.TableColumns, Rows = [] };
        Assert.Equal(TableState.Empty, table.State);
    }
}
