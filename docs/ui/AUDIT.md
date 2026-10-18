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
| _(filled in Phase 1, when spec 01 tokens replace the baseline `Bg0–4` / `Text*` / `Accent*` set)_ | |

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
