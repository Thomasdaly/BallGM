# BallGM Design System

The token-based design system behind the client (spec: `docs/ui/spec/01`–`03`). Everything here lives in
`src/BallGM.Client.Avalonia`: tokens and styles in `Themes/`, controls in `Controls/`, theming code in
`Theming/`, images in `Imaging/`. The Component Gallery (S33, launch with `--gallery`) shows every
component in every state; its screenshots are `artifacts/ui-screens/after/S33_gallery-*.png`.

## 1. Files and merge order

`App.axaml` merges resources first, then styles:

| File | Holds |
|---|---|
| `Themes/Tokens.axaml` | Colour tokens and brushes; team accent and tier brushes (replaced at runtime) |
| `Themes/Typography.axaml` | Font families, sizes, line heights, tracking, `TabularFigures` |
| `Themes/Geometry.axaml` | Spacing scale, paddings, radii, skew angle, cut size, density row heights, shell sizes, image slots, court and silhouette paths |
| `Themes/Icons.axaml` | Phosphor (MIT) icon geometries, `Icon.*` |
| `Themes/Motion.axaml` | Durations and distances |
| `Themes/Controls/*.axaml` | Control themes for built-in controls (Button, ToggleButton) and Fluent resource retuning |
| `Themes/Components/*.axaml` | Control themes for the library components |
| `Themes/Controls.axaml` | Legacy class styles of screens not yet rebuilt (removed when the last one is) |
| `Themes/Styles.axaml` | Text roles, icon sizes, component helper styles, motion and reduced-motion overrides (last) |

## 2. Colour tokens

| Token | Value | Use |
|---|---|---|
| `Canvas` | `#0A0E14` | App background |
| `CanvasRaised` | `#0F1520` | Page background |
| `Surface1` / `Surface2` / `Surface3` | `#141B27` / `#1A2331` / `#222D3E` | Cards; headers and hover; pressed, selected, inputs |
| `Stroke` | `#2A3547` | Decorative hairlines |
| `TextPrimary` / `TextSecondary` / `TextMuted` | `#F2F5F9` / `#A9B4C4` / `#8C99AD` | Text (every one ≥ 4.5:1 on every surface — `Contrast_Tokens`) |
| `TextDisabled` | `#5B6779` | Disabled controls only |
| `AccentFallback` | `#3D8BFF` | Accent with team colours off |
| `Positive` / `Negative` / `Warning` / `Info` | `#2BD67B` / `#FF6B78` / `#FFA53D` / `#4CC2FF` | Text, icons, chips — never large fills |
| `PositiveCvd` / `NegativeCvd` | `#5AAEFF` / `#FF9F43` | The pair in colour-vision-safe mode |
| `FocusRing` | `#F2F5F9` | 2px ring outside the focused control |
| `Scrim` | `#0A0E14` @ 72% | Behind text on imagery and under dialogs |

Brushes are `<Token>Brush`. `PositiveBrush`/`NegativeBrush` follow the CVD setting; static references to a
fixed semantic colour use `PositiveTextBrush`/`NegativeTextBrush`.

## 3. Team theming

`TeamTheme.Derive(primary, secondary)` implements spec 01 §4: keep the primary if it reaches 3.1:1 on
`Surface2`; otherwise lighten it in 0.02 HSL steps up to +0.25; otherwise take the secondary; otherwise
lighten without a cap. `OnAccent` and `OnPrimaryRaw` are pure black or white. `ThemeResources` writes the
result as new brush instances: `ApplyUserTeam` into the application (also Fluent's `SystemAccentColor*`),
`ScopeTeam(control, theme)` into a page root so only that subtree re-tints. Every team key is therefore
referenced with `DynamicResource`. "Team colours in interface" off forces the fallback theme.
All 30 fixture teams are checked by `Contrast_TeamColors`.

## 4. Type

Display: Barlow Condensed (Medium, SemiBold, Bold, ExtraBold, ExtraBold Italic) from the
`fonts:BallGMDisplay` collection. Body: Barlow (Regular, Medium, SemiBold, Bold) from `fonts:BallGMBody`.
Two collections because one folded Barlow Condensed into Barlow. All SIL OFL, licence in `Assets/Fonts/OFL.txt`.

| Class | Role |
|---|---|
| `hero` (`hero small`) | Display ExtraBold 56 (44) — player names, final scores; `italic` allowed here only |
| `h1` / `h2` / `h3` | Display Bold 32 / Bold 24 / SemiBold 20, caps with tracking |
| `stat-xl` / `stat-l` / `stat-m` | Display ExtraBold 56 / 40, Bold 28, tabular figures |
| `label-caps` (`large`) | Body SemiBold 11 (12), +8% tracking, secondary colour |
| `body` / `body-strong` / `table` / `caption` | 14 / 14 SemiBold / 13 Medium tabular / 12 muted |
| modifiers | `num`, `secondary`, `muted`, `accent`, `positive`, `negative`, `warning`, `on-accent`, `on-primary`, `wrap`, `trim` |

Uppercase: write caps text literally, or bind through `UpperCaseConverter`; components such as
`PageHeader` do it for you.

## 5. Shape, spacing, density, motion

- 4px grid: `Space4`…`Space64`, paddings `Pad*`, gaps `Gap*`. Radii: `RadiusNone` 0, `RadiusChip` 2, `RadiusCard` 4.
- Signature angle `SkewAngle` 12°: `SkewBorder` leans only its background; the child is inset by height × tan(angle).
- Cut corners: `CutCornerBorder` (`CutSize` 16, top-right by default, optional child clipping).
- Density: `RowHeight` and `ListItemHeight` (40/56 comfortable, 28/44 compact).
- Motion durations in `Motion.axaml`. Transitions and animations are declared in styles; the
  `reduced-motion` class on the top level removes them (tab flag slide, spinner, skeleton shimmer,
  progress sweep), and `UiServices.ReducedMotion` stops code-driven motion (count-ups, ticker scroll,
  image fades).

## 6. Components

All in `Controls/`, themed in `Themes/Components/`. Interactive themes define `:pointerover`, `:pressed`,
`:disabled`, `:focus-visible` (and `:checked`/`:selected` where relevant).

| Component | Variants and states | Usage |
|---|---|---|
| `Button` | `primary` (skewed accent, one per view), `secondary`, `ghost`, `danger`, `icon`; `small`/`large`; busy via `ctl:Busy.IsBusy` | `<Button Classes="primary" Content="PROPOSE TRADE" />` |
| `ToggleButton` | default, `chip`; checked | position and status filters |
| `ContinueButton` | `State` Ready / Blocked / GameDay / Simulating; `Options` dropdown | the shell's main call to action |
| `EntityLink` | default, hover underline, focus, disabled, trimmed with tooltip | any player or team name; `ToolTip.Tip` = `HoverCard`, `ContextMenu` = actions |
| `NavRail` | expanded, `IsCollapsed`; selected stripe; badges | `ItemsSource` of `NavEntry` |
| `PageHeader`, `Breadcrumb`, `Stepper`, `SkewTabStrip`, `SegmentedControl`, `Badge`, `Divider` | see gallery "Navigation" | |
| `SearchBox`, `RangeFilter`, `FilterBar` | Esc clears; active filter count; Reset | |
| `Tile`, `StatTile`, `CountUpText` | standard, featured (team gradient + cut corner); deltas with ▲/▼ | dashboards |
| `RatingBadge` | S / M / L / XL; tier colour live with the CVD setting; tier name tooltip | |
| `AttributeBar`, `PositionChip`, `StatusChip` | compare tick, delta; status kinds | |
| `PlayerHeadshot`, `TeamLogo` (over `AssetImage`) | XS…Hero; injury dot; number tag; monogram fallback | always through the asset resolver |
| `PlayerCard`, `TeamCard`, `HoverCard` | full / compact; entity ID when "Show entity IDs" is on | |
| `FormSparkline`, `WinLossStrip`, `ComparisonBar`, `CapBar` | | |
| `Linescore`, `ScoreBug`, `Ticker`, `BracketView`, `CalendarMonth`, `Timeline`, `NewsCard` | | |
| `EmptyState`, `ErrorState`, `Banner`, `Skeleton`, `ProgressOverlay` | | every data screen's empty, error and loading states |
| `ToastHost` + `ToastService`, `DialogHost` + `DialogRequest`, `CrashDialog` | toasts auto-dismiss in 5s (errors stay); dialogs trap Tab, Esc cancels, Enter confirms | |

## 7. Images

Views never load images. `IAssetResolver` (`Imaging/AssetResolver.cs`) maps an `AssetKey` (entity kind and
ID) to the loaded pack's file, checks the path stays inside the pack (no `..`, absolute paths or escaping
links) before opening it, probes and decodes on worker threads through SkiaSharp's codec at a bucketed
size (32…1024 px), and caches in a byte-budgeted LRU (256 MB default). Bad files become the vector
fallback plus one issue per file per session.

**Disposal (02 §11):** evicted bitmaps are not disposed. An image evicted from the cache may still be drawn
by a visible control, so it is released to the garbage collector when the last control lets go of it.

## 8. Adding a component

1. Class in `Controls/` (a `TemplatedControl`, or a `Control` that renders), properties as `StyledProperty`.
2. Control theme in `Themes/Components/<Family>.axaml`, keyed by type, tokens only, `:focus-visible` if
   it takes focus; descendant selectors go in `Themes/Styles.axaml` (control themes cannot hold them).
3. Add it to the gallery in every state and, if it is required by spec 03, to the gallery registry.
4. Run the UiRelease suite: the token lint, the compiled-bindings lint and the gallery test fail on
   literals, untyped templates or binding errors.
