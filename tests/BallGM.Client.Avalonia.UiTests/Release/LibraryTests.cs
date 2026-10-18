using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.UiTests.Harness;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Client.Avalonia.Views.Gallery;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>The component library's own gates (spec 06 section 3): focus-visible themes and a complete gallery.</summary>
[Trait("Category", "UiRelease")]
public sealed class LibraryTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Every Req component of spec 03 sections 3-8, the gallery section it appears in, and how to find it
    /// in the rendered tree. "If data" components the game has data for (numeric input, slider, cap bar,
    /// shot chart, bracket, timeline) are included too.
    /// </summary>
    internal static readonly (string Component, string Section, Func<Visual, bool> Found)[] Registry =
    [
        ("Text styles", "Foundations", Classed<TextBlock>("hero")),
        ("Icon", "Foundations", Is<PathIcon>()),
        ("SkewBorder", "Foundations", Is<SkewBorder>()),
        ("CutCornerBorder", "Foundations", Is<CutCornerBorder>()),
        ("Divider", "Foundations", Is<Divider>()),
        ("Button", "Actions", Classed<Button>("primary")),
        ("ContinueButton", "Actions", Is<ContinueButton>()),
        ("SplitButton", "Actions", Is<SplitButton>()),
        ("EntityLink", "Actions", Is<EntityLink>()),
        ("ToggleSwitch", "Actions", Is<ToggleSwitch>()),
        ("CheckBox", "Actions", Is<CheckBox>()),
        ("RadioButton", "Actions", Is<RadioButton>()),
        ("NavRail", "Navigation", Is<NavRail>()),
        ("PageHeader", "Navigation", Is<PageHeader>()),
        ("SkewTabStrip", "Navigation", Is<SkewTabStrip>()),
        ("SegmentedControl", "Navigation", Is<SegmentedControl>()),
        ("Breadcrumb", "Navigation", Is<Breadcrumb>()),
        ("Stepper", "Navigation", Is<Stepper>()),
        ("Badge", "Navigation", Is<Badge>()),
        ("TextBox", "Inputs", Is<TextBox>()),
        ("Search box", "Inputs", Is<SearchBox>()),
        ("ComboBox", "Inputs", Is<ComboBox>()),
        ("NumericUpDown", "Inputs", Is<NumericUpDown>()),
        ("Slider", "Inputs", Is<Slider>()),
        ("RangeFilter", "Inputs", Is<RangeFilter>()),
        ("FilterBar", "Inputs", Is<FilterBar>()),
        ("ChipToggle", "Inputs", Classed<ToggleButton>("chip")),
        ("Card / Tile", "Data", Is<Tile>()),
        ("StatTile", "Data", Is<Controls.StatTile>()),
        ("RatingBadge", "Data", Is<RatingBadge>()),
        ("AttributeBar", "Data", Is<AttributeBar>()),
        ("PositionChip", "Data", Is<Controls.PositionChip>()),
        ("StatusChip", "Data", Is<StatusChip>()),
        ("KeyValueList", "Data", Is<KeyValueList>()),
        ("Table styling", "Tables", Is<DataTable>()),
        ("ColumnChooser", "Tables", visual => visual is Button button && global::Avalonia.Automation.AutomationProperties.GetName(button) == "Choose columns"),
        ("PlayerHeadshot", "Media", Is<PlayerHeadshot>()),
        ("TeamLogo", "Media", Is<TeamLogo>()),
        ("PlayerCard", "Media", Is<PlayerCard>()),
        ("TeamCard", "Media", Is<TeamCard>()),
        ("HoverCard", "Media", Is<HoverCard>()),
        ("Sparkline", "Sports", Is<FormSparkline>()),
        ("W/L strip", "Sports", Is<WinLossStrip>()),
        ("ComparisonBar", "Sports", Is<ComparisonBar>()),
        ("CapBar", "Sports", Is<CapBar>()),
        ("ShotChart", "Sports", Is<ShotChart>()),
        ("Linescore", "Sports", Is<Linescore>()),
        ("ScoreBug", "Sports", Is<ScoreBug>()),
        ("Ticker", "Sports", Is<Ticker>()),
        ("BracketView", "Sports", Is<BracketView>()),
        ("CalendarMonth", "Sports", Is<CalendarMonth>()),
        ("Timeline", "Sports", Is<Timeline>()),
        ("NewsCard / InboxItem", "Sports", Is<NewsCard>()),
        ("Toast", "Feedback", Is<ToastHost>()),
        ("Dialog", "Feedback", Is<DialogHost>()),
        ("ProgressOverlay", "Feedback", Is<ProgressOverlay>()),
        ("Skeleton", "Feedback", Is<Skeleton>()),
        ("EmptyState", "Feedback", Is<EmptyState>()),
        ("ErrorState", "Feedback", Is<ErrorState>()),
        ("Banner", "Feedback", Is<Banner>()),
        ("CrashDialog", "Feedback", Is<CrashDialog>()),
        ("Scrollbar", "Tables", Is<ScrollBar>()),
    ];

    [AvaloniaFact]
    public void Gallery_Complete()
    {
        UiServices.ReducedMotion = true;
        var viewModel = new ComponentGalleryViewModel();
        var window = new Window { Width = 1920, Height = 2400, Content = new ComponentGalleryView { DataContext = viewModel } };
        window.Classes.Add("reduced-motion");
        window.Show();
        var missing = new List<string>();

        foreach (var section in Registry.GroupBy(entry => entry.Section))
        {
            viewModel.SectionIndex = viewModel.Sections.ToList().IndexOf(section.Key);
            ShellDriver.Settle();
            var shown = window.GetVisualDescendants().Where(visual => visual.IsEffectivelyVisible).ToList();
            missing.AddRange(section.Where(entry => !shown.Any(entry.Found)).Select(entry => $"{entry.Component} (section {entry.Section})"));
        }

        // Button is shown in every state the theme defines.
        viewModel.SectionIndex = viewModel.Sections.ToList().IndexOf("Actions");
        ShellDriver.Settle();
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        foreach (var state in new[] { ":pointerover", ":pressed", ":focus-visible", ":disabled" })
        {
            if (!buttons.Any(button => ((IPseudoClasses)button.Classes).Contains(state) || button.Classes.Contains(state)))
            {
                missing.Add($"Button in state {state}");
            }
        }

        if (!buttons.Any(button => button.Classes.Contains("busy")))
        {
            missing.Add("Button in its loading state");
        }

        window.Close();
        Assert.True(missing.Count == 0, "Missing from the gallery:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void FocusVisible_Themes()
    {
        var problems = new List<string>();
        var clientAssembly = typeof(App).Assembly;
        var avalonia = typeof(Button).Assembly;
        foreach (var file in UiLint.ClientFiles("*.axaml", includeThemes: true).Where(path => path.Replace('\\', '/').Contains("/Themes/", StringComparison.Ordinal)))
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var theme in document.Descendants().Where(element => element.Name.LocalName == "ControlTheme"))
            {
                var target = theme.Attribute("TargetType")?.Value ?? string.Empty;
                var name = target.Contains(':', StringComparison.Ordinal) ? target[(target.IndexOf(':', StringComparison.Ordinal) + 1)..] : target;
                var type = clientAssembly.GetTypes().FirstOrDefault(candidate => candidate.Name == name)
                    ?? avalonia.GetTypes().FirstOrDefault(candidate => candidate.Name == name && candidate.Namespace?.StartsWith("Avalonia.Controls", StringComparison.Ordinal) == true);
                if (type is null || !IsInteractive(type))
                {
                    continue;
                }

                var basedOnInteractive = theme.Attribute("BasedOn") is not null;
                var hasFocusVisible = theme.Descendants().Any(element => element.Name.LocalName == "Style" && (element.Attribute("Selector")?.Value.Contains(":focus-visible", StringComparison.Ordinal) ?? false));
                if (!hasFocusVisible && !basedOnInteractive)
                {
                    var key = theme.Attribute(Xaml + "Key")?.Value ?? target;
                    problems.Add($"{UiLint.Relative(file)}: control theme {key} ({target}) has no :focus-visible style");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    private static bool IsInteractive(Type type) =>
        typeof(Button).IsAssignableFrom(type) || typeof(ListBoxItem).IsAssignableFrom(type) || typeof(TabStripItem).IsAssignableFrom(type)
        || typeof(ToggleButton).IsAssignableFrom(type) || typeof(TextBox).IsAssignableFrom(type) || typeof(MenuItem).IsAssignableFrom(type);

    private static Func<Visual, bool> Is<T>() => visual => visual is T;

    private static Func<Visual, bool> Classed<T>(string className) where T : StyledElement => visual => visual is T element && element.Classes.Contains(className);
}
