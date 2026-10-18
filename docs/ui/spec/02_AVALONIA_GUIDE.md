# 02 · Avalonia Implementation Guide

How to build 01 and 03 in Avalonia. Code here is illustrative: **check every API against the Avalonia version the project uses** (11.x and 12.x differ in places) and adapt names to the project's conventions. Record adaptations in `AUDIT.md` → Decisions.

---

## 1. Detect before you build (Phase 0)

Record in `AUDIT.md`:

- Avalonia version and target frameworks. **Do not migrate between major versions** (00 §5 rule 2). 12.x requires SkiaSharp 3 and removed long-obsolete APIs; if the project is on 11.x, stay there and log a migration follow-up.
- MVVM library (CommunityToolkit.Mvvm, ReactiveUI, Prism, custom) — follow its idioms for any new view models.
- Base theme (FluentTheme, Simple, a third-party theme) and whether the DataGrid theme is included.
- Navigation mechanism (view-model-first with `DataTemplate`s, a navigation service, view-first `ContentControl` swaps).
- Grid and list controls in use; image-loading code paths; the mod loader's entry points.
- Test framework and any existing UI tests.

## 2. Theme structure

```
Themes/
  Tokens.axaml          colors and brushes (static), team accent brushes (dynamic)
  Typography.axaml      font families, sizes, text style classes
  Geometry.axaml        spacing scale, radii, skew angle, cut size, icon geometries
  Motion.axaml          durations and easings as resources
  Controls/*.axaml      ControlThemes for built-in controls (ResourceDictionaries)
  Components/*.axaml    ControlThemes / templates for custom components
  Styles.axaml          class-based styles, density, reduced-motion overrides (last)
```

Merge order in `App.axaml` — resources first, then styles, with the overrides last:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://YourApp/Themes/Tokens.axaml" />
      <ResourceInclude Source="avares://YourApp/Themes/Typography.axaml" />
      <ResourceInclude Source="avares://YourApp/Themes/Geometry.axaml" />
      <ResourceInclude Source="avares://YourApp/Themes/Motion.axaml" />
      <ResourceInclude Source="avares://YourApp/Themes/Controls/Index.axaml" />
      <ResourceInclude Source="avares://YourApp/Themes/Components/Index.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
<Application.Styles>
  <FluentTheme />  <!-- or the project's current base theme -->
  <StyleInclude Source="avares://YourApp/Themes/Styles.axaml" />
</Application.Styles>
```

Set `RequestedThemeVariant="Dark"` on the application. If a light variant exists, put variant-specific values in `ResourceDictionary.ThemeDictionaries` keyed `Dark` and `Light`.

## 3. Resources and team theming

- **`StaticResource`** for fixed tokens (spacing, radii, type sizes, surface colors).
- **`DynamicResource`** for anything that changes at runtime: team accent brushes, density sizes, UI-scale-sensitive values.
- **App-level team accent:** a `TeamThemeService` computes the accent (01 §4) and writes the resources:

```csharp
var res = Application.Current!.Resources;
res["AccentColor"] = accent;
res["AccentBrush"] = new SolidColorBrush(accent);
res["OnAccentBrush"] = new SolidColorBrush(onAccent);
// Keep the base theme's accent in step (Fluent):
res["SystemAccentColor"] = accent;   // plus SystemAccentColorLight1..3 / Dark1..3
```

- **Page-scoped team colors:** a page about another team sets the same keys on its root control's `Resources`. `DynamicResource` lookups resolve to the nearest scope, so that subtree re-tints and the shell does not.
- Never hard-code a color, font family or font size in `Views/**`. The token lint (06 §3) enforces this.

## 4. Control themes and style classes

Restyle built-in controls with `ControlTheme`s keyed by type; express variants as style classes.

```xml
<ControlTheme x:Key="{x:Type Button}" TargetType="Button">
  <Setter Property="Background" Value="{DynamicResource Surface2Brush}" />
  <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
  <Setter Property="Padding" Value="16,8" />
  <Setter Property="Template">
    <ControlTemplate>
      <ui:SkewBorder Background="{TemplateBinding Background}"
                     Angle="{DynamicResource SkewAngle}"
                     Padding="{TemplateBinding Padding}">
        <ContentPresenter Content="{TemplateBinding Content}"
                          ContentTemplate="{TemplateBinding ContentTemplate}" />
      </ui:SkewBorder>
    </ControlTemplate>
  </Setter>
  <Style Selector="^:pointerover"> ... </Style>
  <Style Selector="^:pressed"> ... </Style>
  <Style Selector="^:disabled"> ... </Style>
  <Style Selector="^:focus-visible"> ... </Style>   <!-- required on every interactive theme -->
  <Style Selector="^.primary">
    <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource OnAccentBrush}" />
  </Style>
</ControlTheme>
```

- Every interactive control theme defines `:pointerover`, `:pressed`, `:disabled`, `:focus-visible`, and `:selected` / `:checked` where the control has them. The `FocusVisible_Themes` test checks this.
- Variants are classes (`primary`, `secondary`, `ghost`, `danger`, `icon`, `compact`), never separate one-off templates per view.
- Restyle scrollbars, tooltips, context menus, flyouts and combo-box popups too — default ones break the look instantly.

## 5. Compiled bindings everywhere

```xml
<PropertyGroup>
  <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
</PropertyGroup>
```

- Every view and every `DataTemplate` / item template declares `x:DataType`. Binding mistakes become build errors instead of silent blanks.
- A reflection binding is allowed only with `x:CompileBindings="False"` on the smallest possible element, a comment giving the reason, and an entry in the lint allowlist.
- Setting the property is harmless if your version already defaults to it.

## 6. Fonts

```xml
<ItemGroup>
  <AvaloniaResource Include="Assets\Fonts\**" />
</ItemGroup>
```

```xml
<FontFamily x:Key="DisplayFont">avares://YourApp/Assets/Fonts#Barlow Condensed</FontFamily>
<FontFamily x:Key="BodyFont">avares://YourApp/Assets/Fonts#Inter</FontFamily>
```

- Ship static weight files (Regular, Medium, SemiBold, Bold, ExtraBold) rather than variable fonts; check every weight renders distinctly in the gallery. The `Avalonia.Fonts.Inter` package (MIT) is an acceptable way to get Inter.
- Put each font's `OFL.txt` beside the files and list it in `ASSET_LICENSES.md`.
- Tabular figures: if your version exposes OpenType font features on text elements, enable `tnum` for stat styles; otherwise pick a numeral style whose default digits are tabular. Verify with the `111.1` / `888.8` column test (01 §5).
- `LetterSpacing` on `TextBlock` implements the tracking values in 01 §5.

## 7. Geometry helpers

Build these once in `Controls/` and reuse them everywhere:

- **`SkewBorder`** (a `Decorator`): draws a parallelogram background and optional stroke leaning `Angle` degrees. The child stays upright. In `MeasureOverride`, add a horizontal inset of `height × tan(angle)` on each side so content never touches the slanted edges. Implement custom hit testing (`ICustomHitTest` or equivalent) so the transparent corners don't swallow clicks.
- **`CutCornerBorder`**: a polygon with one or more 45° cut corners; can clip its child (images in featured tiles).
- Keep both cheap: build a `StreamGeometry` once per size change and cache it.

Do **not** apply a `SkewTransform` to whole controls — text and hit areas skew with it.

## 8. Icons

```xml
<StreamGeometry x:Key="Icon.Roster">M...Z</StreamGeometry>
<PathIcon Data="{StaticResource Icon.Roster}" Width="20" Height="20" />
```

Convert the chosen icon set's SVG paths into `StreamGeometry` resources in `Themes/Geometry.axaml` (or an `Icons.axaml`). `PathIcon` fills with `Foreground`, so it follows theme and state automatically.

## 9. Transitions and animation

- Declare `Transitions` in **styles**, not inline in views, so the reduced-motion style can remove them:

```xml
<Style Selector="Border.tile">
  <Setter Property="Transitions">
    <Transitions>
      <BrushTransition Property="Background" Duration="0:0:0.12" />
      <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.12" />
    </Transitions>
  </Setter>
</Style>

<!-- Styles.axaml, declared last -->
<Style Selector="Window.reduced-motion Border.tile">
  <Setter Property="Transitions" Value="{x:Null}" />
</Style>
```

- Reduced motion toggles a `reduced-motion` class on the window (or top-level host). Screenshot tests force it on.
- **Page transitions:** host pages in a `TransitioningContentControl`. Built-in `PageSlide` moves the full width — too much. Write a small custom page transition: fade plus a 24px offset in the navigation direction (back = reverse). Set it to `null` under reduced motion.
- **Count-up numbers:** a `CountUpText` control animating a display value from 0 (or the previous value) to the target over 400ms; shows the final value immediately under reduced motion and in tests.
- **Ticker:** animate a `TranslateTransform` on a duplicated strip for a seamless loop; pause on pointer-over; page every 6s under reduced motion.
- Animate opacity and transforms only. No `BoxShadow`, `OpacityMask` or blur on items inside virtualized lists.

## 10. Lists and tables

- `ListBox` virtualizes by default (`VirtualizingStackPanel`). **Never** put a virtualizing list inside an outer `ScrollViewer` or a `StackPanel` — it measures with infinite height and realizes every item.
- `ItemsControl` uses a non-virtualizing `StackPanel` by default. For long lists use `ListBox`, or set `ItemsPanel` to a `VirtualizingStackPanel`.
- **DataGrid** (if used): make sure its theme is included; restyle via `DataGridColumnHeader`, `DataGridRow`, `DataGridCell` and the sort glyphs; right-align numeric columns; freeze the name column; fixed row heights per density (40 / 28) help virtualization.
- **Column chooser:** a flyout of check items toggling column `IsVisible`; persist the choice per screen in UI settings.
- Row content is cheap: XS/S headshots decoded at size, no shadows or masks, no nested virtualizing lists.
- Use whichever grid the project already uses. Don't add a commercial grid.

## 11. Images

- Views and view models never construct bitmaps from paths. Every image goes through the asset resolver (05 §3). The lint in 06 §3 greps for violations.
- Decode at the size you'll draw: `Bitmap.DecodeToWidth(stream, px, BitmapInterpolationMode.HighQuality)` (or `DecodeToHeight`), where `px` = slot size in DIPs × render scaling × UI scale, rounded up to a bucket (05 §4).
- Decode on a background thread; publish the result to the UI thread.
- Don't dispose a bitmap that may still be on screen. Let evicted cache entries be collected, or reference-count them. Document the choice in `DESIGN_SYSTEM.md`.

## 12. UI scale, window and chrome

- **UI scale (90–150%)**: wrap the shell in a `LayoutTransformControl` with a `ScaleTransform` bound to the setting. The 150% screenshot runs at 1920×1080, an effective 1280×720 layout.
- **Window:** minimum 1280×720; remember size, position and state; F11 toggles full screen; restore safely if the saved position is off-screen.
- **Custom chrome (P2):** extending content into the title bar so the header band is the title bar (native caption buttons kept) makes it feel like a game. The APIs differ between 11.x and 12.x — use the installed version's documented approach, check Windows, macOS and Linux, and keep native chrome on any platform where it misbehaves.

## 13. Threading

- Change bound state on the UI thread (`Dispatcher.UIThread.Post` / `InvokeAsync`).
- Long operations (multi-day sim, load/save, roster import) run in the background with `IProgress<T>` marshalled to the UI, behind a progress overlay with a Stop button where the operation supports cancellation.
- If the simulation currently runs on the UI thread and cannot move without changing simulation code, don't change it: show the overlay, yield to the dispatcher between existing per-day steps if the loop already has them, and log the rest in `FOLLOWUPS.md`.

## 14. Error handling

- Hook `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and the UI dispatcher's unhandled-exception event (name varies by version).
- Show a branded crash dialog: what happened in plain words, the log path, **Copy details**, **Open logs folder**, **Quit**.
- Log to a rotating file in the user data folder. A failing image load logs once per file per session, never per frame.

## 15. Headless UI tests

Packages, version-matched to the app's Avalonia: `Avalonia.Headless.XUnit` (or `Avalonia.Headless.NUnit` — follow the project's framework) and `Avalonia.Skia`.

```csharp
[assembly: AvaloniaTestApplication(typeof(UiTestAppBuilder))]

public class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
```

- `UseHeadlessDrawing = false` with Skia is what makes `window.CaptureRenderedFrame()` return real pixels. Save the returned bitmap as PNG.
- `TestApp` derives from the real `App` (or shares its resource setup) but skips desktop-lifetime startup, points data and mod roots at a temp directory, forces culture `en-US`, a fixed in-game date, a fixed seed, and reduced motion.
- Navigate through the **real shell and navigation service**, not by constructing a view in isolation, so the screenshots show what players see.
- Input: the headless helpers simulate keys and pointer (`KeyPress` / `KeyRelease` / `KeyPressQwerty`, `MouseDown` / `MouseMove` / `MouseUp`); read focus from the top level's `FocusManager`.
- **Binding errors:** install a log sink that records binding-area warnings and errors during each test; fail the test if any appear.
- Tests must be deterministic: embedded fonts only, no animations, no wall-clock time, no network.

## 16. Performance hygiene checklist

- [ ] No synchronous file or image I/O on the UI thread.
- [ ] Virtualized lists for anything that can exceed 50 rows; fixed row heights.
- [ ] No `BoxShadow`, `OpacityMask`, blur or full-size images inside list rows.
- [ ] Geometry cached; no per-frame allocations in custom `Render` overrides.
- [ ] Brushes for team colors are shared resources, not created per item.
- [ ] Expensive view models load data asynchronously and show a skeleton first.
- [ ] Event subscriptions from views to long-lived services are released when the view detaches (no leaks while navigating).
