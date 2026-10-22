# UI Audit — Baseline

> Filled by Claude Code in Phase 0, before any visual change. Spec 00 §6 and 02 §1.

## 1. Environment
| Item | Value |
|---|---|
| Avalonia version(s) | **12.0.4** (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`), central versions in `Directory.Packages.props`. SkiaSharp 3.119.4 (transitive via `Avalonia.Skia`). Stays on 12.x (00 §5 rule 2). |
| .NET target framework(s) | `net10.0`, C# 14, nullable on (`Directory.Build.props`); SDK pinned by `global.json` 10.0.301 |
| Solution / UI project / test projects | `BallGM.slnx` · UI: `src/BallGM.Client.Avalonia` · tests: `tests/BallGM.{Domain,Application,Rules,Simulation,Integration}.Tests` (xunit v2), `tests/BallGM.Client.Avalonia.Tests` (xunit v3 + Avalonia.Headless.XUnit, headless drawing), **new** `tests/BallGM.Client.Avalonia.UiTests` (xunit v3 + Avalonia.Headless.XUnit, Skia drawing — the UiRelease suite and screenshot harness) |
| Base theme (Fluent, Simple, other) | `FluentTheme` with a custom dark `ColorPaletteResources` (`App.axaml`), plus `Themes/Tokens.axaml` (resources) and `Themes/Controls.axaml` (styles). No DataGrid package. `RequestedThemeVariant="Dark"`. No light variant. |
| MVVM library | None — hand-rolled `ViewModelBase` (INotifyPropertyChanged), `RelayCommand`, `ParameterCommand<T>` |
| DI container | None — `LeagueClientComposition` is the composition root (builds one `LeagueSession`) |
| Navigation mechanism | View-model-first: `MainWindowViewModel.SelectedSection` (string) → `CurrentScreen` (object) → `ContentControl` with `DataTemplate`s in `MainWindow.axaml`. Sidebar = `NavGroup`s (Squad / Market / League) of `ListBox`es. Sub-views (player profile, game sheet) are view-model state inside a screen (`RosterViewModel.Profile`, `LeadersViewModel` profile, `SeasonViewModel.SelectedFixture`). No back/forward history. |
| Grid / list controls in use | `ItemsControl` (non-virtualizing) for almost every table; `ListBox` for nav; hand-built table rows (Grid with shared column widths); `SpanPanel` (custom panel); LiveCharts2 referenced (`LiveChartsCore.SkiaSharpView.Avalonia` 2.1.0-dev) but no chart control in views; custom-drawn `Sparkline` and `ShotChart` controls |
| Image-loading code paths | `Converters/ImagePathConverter.cs` (`new Bitmap(path)` synchronously on the UI thread, cached forever in a static dictionary), `Theming/TeamPaint.cs` (`new Bitmap(logoPath)` + `CreateScaledBitmap` to sample the logo's dominant colour). Paths come from `TeamSummary.LogoPath`, `RosterSpot.PortraitPath`, profile/leader read models. |
| Mod loader entry points | `LeagueClientComposition.CreateMainWindowViewModel()` reads env var `BALLGM_LEAGUE_PACK`; unset → `FixtureLeagueDataSource` (6-team shipped league, `data/rulesets/default-league.json`), set → `LeaguePackDataSource(path)` (`src/BallGM.Infrastructure/LeaguePacks`). |
| Mod on-disk format (folders, mapping files, ID scheme) | One mod kind: the **league pack** — a single JSON file (`LeaguePackEnvelope`, schema 1–3) holding league name, season, `rulesetFile` (path relative to the pack), conferences → divisions → team keys, teams (`key`, `name`, `franchiseName`, optional `logo` path, `colours` {primary, secondary}), players (`name`, `position`, `birthDate`, `seasonsOfService`, `team` key, ratings, `injury` text, `contract`, optional `portrait` path, career…). Images are png/jpg files referenced by **paths relative to the pack file**; the loader refuses rooted paths, paths escaping the pack folder (string check, not symlink-aware) and other extensions as pack errors (whole pack fails to load), and maps a missing file to "no image". Keys are pack-local; the loaded league **mints fresh `SortableId`s on every load**, so entity IDs are not stable across loads. There are no separate graphics packs and no user mod folder. |
| Rating scale used by the game (e.g. 25–99, 1–20) and tier mapping | `PlayerRating.Overall` 0–100 (attributes 0–100). Spec 25–99 tier mapping applies directly: Elite 90+, All-Star 80–89, Starter 70–79, Rotation 60–69, Bench 50–59, Fringe < 50. Potential shown as stars (`RatingView`). |
| Test framework(s) | xunit v2 (domain/rules/sim/integration), xunit v3 + `Avalonia.Headless.XUnit` 12.0.4 (client) |
| Dev OS used for this run | macOS (Darwin 25.4.0, Apple Silicon), .NET SDK 10.0.301 |

## 2. Baseline numbers
| Metric | Value | Command |
|---|---|---|
| Base branch and commit | `main` @ `2c42a3c` (feat(client): the scouting sheet on screen - attributes A2). Uncommitted attributes-A3a work found in the tree was parked on branch `wip/attributes-a3a` (Decision 1). | `git log -1 --oneline main` |
| Release build warnings | **1** (CS9113 in `tests/BallGM.Application.Tests/LeagueSessionTests.cs(243,57)`), 0 errors, 0 XAML/binding build warnings | `dotnet build BallGM.slnx -c Release --no-incremental -p:UseSharedCompilation=false` |
| Tests: total / passed / failed / skipped | **1021 / 1021 / 0 / 0** (Application 41, Domain 246, Rules 368, Simulation 60, Client 93, Integration 213) | `dotnet test BallGM.slnx -c Release --no-build` |
| Runtime binding warnings at baseline | 9 distinct binding warnings (null-source `$parent[UserControl].DataContext.*` commands, `Selected.FullName`, `Opponent.LogoPath`), logged per capture in `artifacts/ui-screens/before/binding-log.txt` | baseline capture test |

## 3. Verification commands (exact)
| Purpose | Command |
|---|---|
| Build | `dotnet build BallGM.slnx -c Release -p:UseSharedCompilation=false` |
| All tests | `dotnet test BallGM.slnx -c Release --no-build` |
| Existing tests only (checkpoint) | `dotnet test BallGM.slnx -c Release --no-build --filter "Category!=UiRelease"` |
| UiRelease tests | `dotnet test tests/BallGM.Client.Avalonia.UiTests -c Release --no-build --filter "Category=UiRelease"` |
| Capture `before/` | `BALLGM_UI_CAPTURE_BEFORE=1 dotnet test tests/BallGM.Client.Avalonia.UiTests -c Release --no-build --filter "FullyQualifiedName~BaselineCaptureTests"` (run once, on the untouched UI — done Run 1 Turn 1) |
| Capture `after/` | `dotnet test tests/BallGM.Client.Avalonia.UiTests -c Release --no-build --filter "FullyQualifiedName~Screens_RenderMatrix"` (writes `artifacts/ui-screens/after/`) |
| Full local pipeline | `./tools/verify-dotnet.sh` (restore, format check, build, test) |

## 4. Screen Inventory
Every screen that exists at baseline, plus the UI-only additions from spec 04 (marked "added"). Template and priority per spec 04 §5–6.

| ID | Screen | Template | View file(s) | View model | Exists at baseline | Priority | Status |
|---|---|---|---|---|---|---|---|
| S01 | Splash and loading | T0 | — | — | added (UI) | P0 | todo |
| S02 | Title / main menu | T0 | — | — | added (UI) | P0 | todo |
| S03 | New career (league pack + team choice, summary) | T5 | — | — | added (UI; GM profile step needs game logic → F-list) | P0 | todo |
| S04 | Load / save | T2 | — | — | added (UI over existing `LeagueSession.Save`/`LoadSave`) | P0 | todo |
| S05 | Home hub | T1 | — | — | added (UI over existing read models) | P0 | todo |
| S07 | Roster | T2 | `Views/RosterView.axaml` | `RosterViewModel` | yes | P0 | before ✓ |
| S08 | Rotation / depth chart (read-only) | T9 | — | — | added (UI over `LeagueSession.DepthChart`; editing needs game logic) | P0 | todo |
| S10 | Player profile | T3 | `Views/PlayerProfileView.axaml` | `PlayerProfileViewModel` | yes | P0 | before ✓ |
| S11 | Team profile | T3 | — | — | added (UI over `TeamSummary`, schedule, standings, stats) | P0 | todo |
| S12 | Schedule / calendar | T8 | `Views/SeasonView.axaml` | `SeasonViewModel` | yes | P0 | before ✓ |
| S13 | Standings | T2 | part of `Views/SeasonView.axaml` | `SeasonViewModel.Standings` | yes (inside S12) | P0 | todo |
| S14 | League leaders and stats | T2 | `Views/LeadersView.axaml` | `LeadersViewModel` | yes | P1 | before ✓ |
| S15 | Pre-game | T1 | — | — | added (UI over schedule, standings, injuries, depth chart) | P0 | todo |
| S17 | Box score / post-game | T7 | game sheet in `Views/SeasonView.axaml` | `GameSheet` | yes | P0 | before ✓ |
| S18 | Trades | T6 | `Views/TradeProposalView.axaml` | `TradeProposalViewModel` | yes | P0 | before ✓ |
| S19 | Free agency | T2 | `Views/FreeAgencyView.axaml` | `FreeAgencyViewModel` | yes | P0 | before ✓ |
| S20 | Contract negotiation (free-agency board) | T6 | `Views/FreeAgencyBoardView.axaml` | `FreeAgencyBoardViewModel` | yes | P1 | before ✓ |
| S23 | Finances (cap sheet) | T1/T2 | `Views/CapSheetView.axaml` | `CapSheetViewModel` | yes | P1 | before ✓ |
| S26 | Playoff bracket (from postseason fixtures) | T9 | — | — | added (if data) | P1 | todo |
| S28 | Global search | UI | — | — | added (UI) | P1 | todo |
| S29 | Settings | T10 | — | — | added (UI) | P0 | todo |
| S30 | Mods & Graphics | T4 | — | — | added (UI) | P0 | todo |
| S31 | Credits and licenses | T10 | — | — | added (UI) | P0 | todo |
| S32 | Dialogs, toasts and crash dialog | — | — | — | added (UI) | P0 | todo |
| S33 | Component Gallery (dev-only) | — | — | — | added (UI) | P0 | todo |
| S34 | Contracts (payroll outlook, upcoming free agents, extensions) | T2/T6 | `Views/ContractsView.axaml` | `ContractsViewModel` | yes | P1 | before ✓ |
| S35 | Pick board (draft assets) | T2 | `Views/PickBoardView.axaml` | `PickBoardViewModel` | yes | P1 | before ✓ |
| S36 | Front office (AI diagnostics and turn) | T4 | `Views/FrontOfficeView.axaml` | `FrontOfficeViewModel` | yes | P1 | before ✓ |
| S37 | League failed to load | T0 | `MainWindow.axaml` (load-error branch) | `MainWindowViewModel(loadErrors)` | yes | P0 | before ✓ |

Spec screens with no inventory entry (no data or behaviour in the game; logged in `FOLLOWUPS.md`): S06 Inbox and news, S09 Tactics, S16 Live game, S21 Draft (big board / draft night — draft classes are generated at season end and never exposed to the client), S22 Scouting, S24 Staff, S25 Training and development, S27 Awards, history and records.

Baseline screens: **13** (S07, S10, S12, S14, S17, S18, S19, S20, S23, S34, S35, S36, S37). `before/` holds 39 PNGs = 13 × 3 sizes. S13 Standings existed only as a panel of S12 and appears in the S12 captures.

Baseline screens that could not render headless (with the error):
- _(none — all 13 rendered at all three sizes)_

## 5. Token name mapping (if the project's names differ from spec 01)
| Spec token | Project resource key |
|---|---|
| `Canvas`, `CanvasRaised`, `Surface1–3`, `Stroke`, `TextPrimary/Secondary/Muted/Disabled`, `AccentFallback`, `Positive`, `Negative`, `Warning`, `Info`, `PositiveCvd`, `NegativeCvd`, `FocusRing`, `Scrim` | same names + `Color` / `Brush` suffix (`CanvasColor`, `CanvasBrush`, …). `PositiveBrush`/`NegativeBrush` follow the CVD setting; `PositiveTextBrush`/`NegativeTextBrush` are the fixed defaults. |
| Team accent (01 §4): Accent, OnAccent, AccentAlt, TeamPrimaryRaw, TeamSecondaryRaw, OnPrimaryRaw | `AccentColor`/`AccentBrush` (+ `AccentHoverBrush`, `AccentPressedBrush`, `AccentSoftBrush`, `AccentFaintBrush`), `OnAccentBrush`, `AccentAltBrush`, `TeamPrimaryRawBrush`, `TeamSecondaryRawBrush`, `OnPrimaryRawBrush`, `TeamGradientBrush` — all DynamicResource |
| Rating tiers Elite / All-Star / Starter / Rotation / Bench / Fringe | `TierEliteBrush`, `TierStarBrush`, `TierStarterBrush`, `TierRotationBrush`, `TierBenchBrush`, `TierFringeBrush` (+ `…OnBrush` chip text), palettes `Tier*DefaultColor` / `Tier*CvdColor` |
| Legacy (pre-overhaul) keys | `Bg0–4`, `Line`, `Text`, `TextDim`, `TextFaint`, `Good/Warn/Bad`, `Maple`, `BannerBrush`, `Radius*`, `Space1–6` — aliases of the spec values, removed once no view uses them |

## 6. Decisions
Adaptations, spec gaps, and anything decided where the spec was silent (spec 00 §8).

| # | Run / turn | Decision | Why | Spec ref |
|---|---|---|---|---|
| 1 | R1 / T1 | Uncommitted attributes-A3a work (17 tracked files, 2 new, touching simulation, save and pack code) was committed unchanged onto a new branch `wip/attributes-a3a` before `ui/release-overhaul` was cut from clean `main`. Resume it with `git switch wip/attributes-a3a && git reset HEAD~1`. | The UI branch must not carry simulation or save-format changes (00 §5 rule 1), and the work was not mine to commit to `main` or discard. | 00 §5 r1, r7 |
| 2 | R1 / T1 | The spec pack (`docs/ui/`) and the `CLAUDE.md` snippet are committed as the first commit of `ui/release-overhaul` rather than on `main`. | Keeps `main` untouched; base for diffs stays `main@2c42a3c`. | GOAL.md setup |
| 3 | R1 / T1 | The fixture league is a league pack (`tests/Fixtures/UiLeague/ui-league.json`, schema 2) generated by a committed, deterministic script (seed 42) and loaded through the real `LeaguePackDataSource` via the real composition root (`BALLGM_LEAGUE_PACK`), under `data/rulesets/calibration-league.json` (30 teams, 82 games). Teams, conferences, divisions and colours are exactly spec 06 §1.1; the name-stress players are in it. 570 players (420 rostered at 14 per team, 150 free agents). | The loader reads from a folder, so the real pipeline is exercised (06 §1). | 06 §1 |
| 4 | R1 / T1 | Mid-season state is produced by the calendar screen's own commands: start the season (the session's default seed), advance to day 49 (~35 games). HCM's record is whatever the real engine produces (22–13 at capture), not forced to 24–11. Draft prospects (60) are not in the fixture: the game generates a draft class only at season conclusion and exposes none to the client. | Forcing a record or injecting prospects would need game logic. | 06 §1.2, 00 §5 r5 |
| 5 | R1 / T1 | Entity IDs are minted per load (`SortableId.NewId()`), so two loads of the same pack are different leagues (different schedules). The harness loads the league **once** and resizes the window for each size, so every size shows the same league; values can still differ between test runs. Pixel-exact screenshots across runs are therefore not asserted. | Entity IDs are frozen (00 §5 r1). | 06 §1, 02 §15 |
| 6 | R1 / T1 | Mod fixtures (06 §1.3) are league packs written at test time by `Harness/ModFixtures.cs` (images drawn with SkiaSharp shapes): **good** (portraits for every player — 1:1 and 1.37:1 PNG cutouts, opaque JPEGs — and logos for all 30 teams), **broken** (missing, zero-byte, corrupt, truncated, JPEG-named-.png, 8000×8000, 5:1 headshot, 5:1 logo, escaping symlink), **stress** (≥ 5,000 portraits). The pack loader refuses `..` and absolute image paths as a pack error (whole pack fails), and has one pack at a time, so "duplicate ID across packs" and in-pack unsafe paths are covered against the asset resolver directly (Assets_PathSafety) and against the loader's refusal (the shell shows the load error, no crash). | Mod on-disk format and loader behaviour are frozen. | 05 §5, 06 §1.3 |
| 7 | R1 / T1 | The UiRelease suite lives in a new test project `tests/BallGM.Client.Avalonia.UiTests` (Skia drawing, `UseHeadlessDrawing = false`); the existing client test project keeps its headless-drawing app builder untouched. `src/BallGM.Client.Avalonia.csproj` gained `InternalsVisibleTo BallGM.Client.Avalonia.UiTests` (the composition root is internal). | Real-pixel rendering is per assembly; existing tests must not change. | 02 §15 |
| 8 | R1 / T1 | Baseline `before/` captured with the good mod pack (portraits and logos), the user's team HCM selected through the shell's own team picker, default motion (the baseline UI has no reduced-motion switch). | 06 §2 | 06 §2 |
| 9 | R1 / T2 | Body font is **Barlow** (spec 01 §5 lists it as an OFL alternate to Inter); display Barlow Condensed with ExtraBold and ExtraBold Italic added. One family pair, already embedded. | Avoid a second body family and a new package. | 01 §5 |
| 10 | R1 / T2 | Icons: **Phosphor regular** (MIT), 70 icons converted to `StreamGeometry` (`Themes/Icons.axaml`); the old nav keys (`Icon.Roster`, `Icon.CapSheet`, …) now hold Phosphor paths, so existing code and tests keep working. | One family, fill paths render as PathIcon. | 01 §9, 02 §8 |
| 11 | R1 / T2 | Legacy class styles (`Themes/Controls.axaml`) stay loaded until every screen is rebuilt; legacy token keys alias spec values. Literal `FontSize` in views was replaced by size-only helper classes (`size-caption` …) so the token lint passes now; those helpers go when the screens are rebuilt. | Keep every commit playable (00 §5 r8) while the lint is enforced from Phase 1. | 00 §6 Phase 1 exit |
| 12 | R1 / T2 | `OverallTierConverter` tiers moved to the spec thresholds 90/80/70/60/50 (was 90/80/72/64/56); every existing test case still holds. | 01 §12 | 01 §12 |
| 13 | R1 / T2 | Image decoding uses SkiaSharp's codec (transitive via Avalonia.Skia, MIT) rather than `Bitmap.DecodeToWidth`: Avalonia's loader draws a truncated PNG half-decoded instead of failing, which would break the "corrupt or truncated → fallback" rule. Pixels are resampled to the bucket and copied into an Avalonia `Bitmap`. | 05 §5 | 02 §11, 05 §4–5 |
| 14 | R1 / T2 | `LeagueSession` gained a read-only `Artwork` property (the pack's logo/portrait/colour mapping) so free agents' portraits resolve too; `FreeAgentLine` carries no portrait. Presentation-only, no behaviour change. | 05 §3; 00 §5 r1 allows UI-facing helpers | 05 §3 |
| 15 | R1 / T2 | Headless tests stay synchronous (background work is awaited with a dispatcher-pumping helper), and no AvaloniaObject is created in a plain `[Fact]`: one `DrawingImage` built off the UI thread before the headless platform started bound the dispatcher to the wrong thread and hung the whole suite. | Test determinism | 02 §15 |
| 16 | R1 / T2 | Missing image files cannot be reported by the resolver: the existing pack loader maps a path to a missing file to "no image" before the UI sees it. The resolver reports "missing file" for any catalog path that no longer exists (e.g. deleted after load); the loader's case shows the fallback silently. Logged as F14. | Loader behaviour is frozen. | 05 §5 |
| 17 | R1 / T3 | Fonts split into two embedded collections, `fonts:BallGMDisplay` (Barlow Condensed) and `fonts:BallGMBody` (Barlow), in `Assets/Fonts/Display` and `Assets/Fonts/Body`. With one collection Avalonia folded "Barlow Condensed" into "Barlow" and matched on weight and style only, so every display heading at baseline rendered in regular-width Barlow. | Correct type roles (01 §5) | 01 §5, 02 §6 |
| 18 | R1 / T3 | Legacy generic styles for `Window`, `TextBlock`, `Button`, `ListBox` and inputs were removed from `Themes/Controls.axaml`: styles outrank control themes, so they would have overridden the new Button/ToggleButton control themes. Unrebuilt screens now take the new base look. | 02 §4 | 02 §4 |
| 19 | R1 / T3 | CheckBox, RadioButton, ToggleSwitch, TextBox, ComboBox, Slider, ScrollBar, MenuItem and SplitButton keep Fluent templates, retuned through the token palette and Fluent resource keys (`Themes/Controls/FluentOverrides.axaml`), including the focus visual in the FocusRing colour. Library components and Button/ToggleButton have their own control themes with an outside focus ring. | Re-templating stock controls gains little over palette retuning; the look rules (radius, colours, focus) still hold. | 02 §4 |
| 20 | R1 / T3 | Template text that sets its own colour uses colour-free type classes (`caps`, `caption-plain`): a class style is a style trigger and outranks any value set inside a control template, so `label-caps` (secondary colour) silently overrode e.g. `OnAccent` on the score bug. | Legibility on accents (01 §4) | 02 §4 |
| 21 | R1 / T3 | `SkewBorder` never arranges its child narrower than the child's desired width: when a parent stretches it taller than it measured, the slant grows, and squeezing the child clipped labels ("FINA", "SCORE"). | 06 §6 R6 | 02 §7 |
| 22 | R1 / T3 | Headless UI tests force `UiServices.ReducedMotion` and the `reduced-motion` window class; infinite animations (spinner, skeleton shimmer, indeterminate progress) run only without reduced motion. | Deterministic screenshots (01 §10, 02 §15) | 02 §15 |
| 23 | R1 / T3 | The gallery is also captured full-height (`S33_gallery-<section>_full.png`, 1920×2400) so components below the fold are reviewable; the 1920×1080 captures remain the matrix images. | 03 §2 | 03 §2 |
| 24 | R1 / T4 | Potential is shown as the scouts' stars (half-star steps) on the roster, the profile hero and player cards, never as a numbered POT badge: the game keeps Potential Ability hidden (`docs/attributes-plan.md`), so a figure would leak it. Where 04 asks for "OVR and POT badges", POT is the star rating with a "POT" caption and a tooltip saying why. | Rules accuracy over the wireframe | 04 S07, S10 |
| 25 | R1 / T4 | The shot chart stays the zone heat map the game already had (five zones, league comparison, a table of the same figures, and a hot/level/cold legend) rather than 01 §12's make/miss dots: the game records each attempt's zone, not its location, and 01 §12 makes the dot chart conditional on shot locations. Fills are the `Chart*` tokens. | 01 §12 "only if the game records shot locations" | 01 §12 |
| 26 | R1 / T4 | `RosterSpot` gained `Age` and `BirthDate` init properties and `LeagueSession` an `AgeDate` property (the date the profile already measured age on). Additive read-model members only: no rule, simulation, save or pack change. Roster and profile now state the same age. | 04 S07 row (age) | 04 S07 |
| 27 | R1 / T4 | Box score (S17) and pre-game (S15) are one page of the calendar, opened from any game, with Back/Escape and a breadcrumb; the header's Continue reads GO TO GAME on a game day and opens today's pre-game, whose SIM GAME plays the day and turns the page into the box score. There is no live game (F03). | 04 §4, S15, S17 | 04 §4 |
| 28 | R2 / T1 | The user's run-2 goal widens scope past the UI overhaul: a multi-season playthrough with realistic stat distributions, awards with real eligibility, guarded buyouts and deeper contracts, trades and franchise direction, with the current real-world agreement as context. Spec 00 §5 rule 1 (game logic frozen) is superseded for this run by that instruction. Game-logic work keeps the project's own gates instead: seeded calibration tests, the frozen sim-regress rows (all re-measured in band), versioned schemas, and `docs/` updated with each slice. UI work resumes on the same branch. | user goal, run 2 | 00 §5 |

