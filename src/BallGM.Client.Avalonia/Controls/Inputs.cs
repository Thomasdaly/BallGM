using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// A search field: magnifier on the left, a clear button on the right when there is text, and Esc
/// clears it (spec 03 section 6). Keeps the TextBox theme, adding the parts as inner content.
/// </summary>
public sealed class SearchBox : TextBox
{
    public SearchBox()
    {
        PlaceholderText = "Search";
        Classes.Add("search");
        InnerLeftContent = new PathIcon { Classes = { "small" }, Data = global::Avalonia.Application.Current?.FindResource("Icon.Search") as global::Avalonia.Media.Geometry, Margin = new Thickness(8, 0, 0, 0) };
        var clear = new Button { Classes = { "icon", "small" }, Content = new PathIcon { Classes = { "small" }, Data = global::Avalonia.Application.Current?.FindResource("Icon.Close") as global::Avalonia.Media.Geometry }, Margin = new Thickness(0, 0, 4, 0), IsVisible = false, Focusable = false };
        ToolTip.SetTip(clear, "Clear search (Esc)");
        global::Avalonia.Automation.AutomationProperties.SetName(clear, "Clear search");
        clear.Click += (_, _) => Text = string.Empty;
        InnerRightContent = clear;
        global::Avalonia.Automation.AutomationProperties.SetName(this, "Search");
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && InnerRightContent is Control clear)
        {
            clear.IsVisible = !string.IsNullOrEmpty(Text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text))
        {
            Text = string.Empty;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>A labelled pair of numeric bounds: AGE 19-40, OVR 40-99.</summary>
public sealed class RangeFilter : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<RangeFilter, string?>(nameof(Label));

    public static readonly StyledProperty<decimal> MinimumProperty =
        AvaloniaProperty.Register<RangeFilter, decimal>(nameof(Minimum));

    public static readonly StyledProperty<decimal> MaximumProperty =
        AvaloniaProperty.Register<RangeFilter, decimal>(nameof(Maximum), 100);

    public static readonly StyledProperty<decimal?> LowProperty =
        AvaloniaProperty.Register<RangeFilter, decimal?>(nameof(Low), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<decimal?> HighProperty =
        AvaloniaProperty.Register<RangeFilter, decimal?>(nameof(High), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public decimal Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public decimal Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public decimal? Low
    {
        get => GetValue(LowProperty);
        set => SetValue(LowProperty, value);
    }

    public decimal? High
    {
        get => GetValue(HighProperty);
        set => SetValue(HighProperty, value);
    }
}

/// <summary>
/// Search, chips and dropdowns in one bar, with the active filter count and a Reset that restores the
/// defaults (spec 03 section 6). The filters themselves are the content.
/// </summary>
public sealed class FilterBar : ContentControl
{
    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<FilterBar, string?>(nameof(SearchText), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SearchPlaceholderProperty =
        AvaloniaProperty.Register<FilterBar, string?>(nameof(SearchPlaceholder), "Search");

    public static readonly StyledProperty<int> ActiveCountProperty =
        AvaloniaProperty.Register<FilterBar, int>(nameof(ActiveCount));

    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<FilterBar, ICommand?>(nameof(ResetCommand));

    public string? SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string? SearchPlaceholder
    {
        get => GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    public int ActiveCount
    {
        get => GetValue(ActiveCountProperty);
        set => SetValue(ActiveCountProperty, value);
    }

    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    public string ActiveCountText => ActiveCount switch
    {
        0 => "No filters",
        1 => "1 filter",
        var count => $"{count} filters",
    };

    public static readonly DirectProperty<FilterBar, string> ActiveCountTextProperty =
        AvaloniaProperty.RegisterDirect<FilterBar, string>(nameof(ActiveCountText), bar => bar.ActiveCountText);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveCountProperty)
        {
            RaisePropertyChanged(ActiveCountTextProperty, string.Empty, ActiveCountText);
            PseudoClasses.Set(":active", ActiveCount > 0);
        }
    }
}

/// <summary>
/// Labels under a slider's ticks (spec 03 section 6): child i is centred on tick i, the ticks spaced
/// evenly between <see cref="Inset"/> from each end (half the thumb, where the track's ticks start).
/// </summary>
public sealed class TickLabelPanel : Panel
{
    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<TickLabelPanel, double>(nameof(Inset), 10);

    static TickLabelPanel()
    {
        AffectsArrange<TickLabelPanel>(InsetProperty);
    }

    public double Inset
    {
        get => GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        var span = Math.Max(0, finalSize.Width - (2 * Inset));
        for (var index = 0; index < count; index++)
        {
            var child = Children[index];
            var centre = Inset + (count == 1 ? span / 2 : span * index / (count - 1));
            var width = child.DesiredSize.Width;
            var left = Math.Clamp(centre - (width / 2), 0, Math.Max(0, finalSize.Width - width));
            child.Arrange(new Rect(left, 0, width, finalSize.Height));
        }

        return finalSize;
    }
}
