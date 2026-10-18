using Avalonia;
using Avalonia.Controls;

namespace BallGM.Client.Avalonia.Controls;

/// <summary>
/// Pins pseudo-classes (pointerover, pressed, focus-visible, checked, selected, disabled-look) on a
/// control so the Component Gallery can show every state at once (spec 03 section 2). Gallery only.
/// </summary>
public static class ForceState
{
    public static readonly AttachedProperty<string?> PseudoClassesProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("PseudoClasses", typeof(ForceState));

    static ForceState()
    {
        PseudoClassesProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            Apply(control, change.GetNewValue<string?>());
            if (control is global::Avalonia.Controls.Primitives.TemplatedControl templated)
            {
                templated.TemplateApplied += (_, _) => Apply(control, GetPseudoClasses(control));
            }
        });
    }

    public static string? GetPseudoClasses(Control control) => control.GetValue(PseudoClassesProperty);

    public static void SetPseudoClasses(Control control, string? value) => control.SetValue(PseudoClassesProperty, value);

    private static void Apply(Control control, string? value)
    {
        if (value is null)
        {
            return;
        }

        var pseudo = (global::Avalonia.Controls.IPseudoClasses)control.Classes;
        foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            pseudo.Set(":" + name, true);
        }
    }
}
