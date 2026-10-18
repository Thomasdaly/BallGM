# UI Release — Progress Ledger

> Maintained by Claude Code. Read at the start of every turn, update at the end (spec 00 §9).
> A turn started by a `/goal` command begins a new run: increment **Run**, set **Turn** to 1.

**Run:** 1 · **Turn:** 3/40 · **Phase:** 2 — Components
**Base branch:** `main` @ `2c42a3c` · **Work branch:** `ui/release-overhaul`
**Last updated:** 2026-10-18 (Run 1 · Turn 3, start)

## Next up
1. Phase 2 — geometry and action controls: `SkewBorder`, `CutCornerBorder`, `Divider`, Button ControlTheme (primary/secondary/ghost/danger/icon, S/M/L, loading, focus ring outside), `ContinueButton`, `SplitButton`, `EntityLink`, toggles; scrollbar/tooltip/menu/flyout/combo restyle
2. Phase 2 — navigation, inputs and data display: `NavRail`, `PageHeader`, `SkewTabStrip`, `SegmentedControl`, `Breadcrumb`, `Stepper`, search `TextBox`, `FilterBar`, `ChipToggle`, `RangeFilter`, table styling + `ColumnChooser`, `Card`/`Tile`, `StatTile` + `CountUpText`, `RatingBadge`, `AttributeBar`, `PositionChip`, `StatusChip`, `PlayerHeadshot`/`TeamLogo` (resolver-backed), `PlayerCard`, `TeamCard`, `HoverCard`, `Sparkline`/W-L strip, `ComparisonBar`, `CapBar`, `Linescore`, `ScoreBug`, `Ticker`, `BracketView`, `CalendarMonth`, `Timeline`, `KeyValueList`, `NewsCard`/`InboxItem`
3. Phase 2 — feedback: `Toast`, `Dialog`, `ProgressOverlay`, `Skeleton`, `EmptyState`, `ErrorState`, `Banner`, `Badge`, `CrashDialog`; Component Gallery (S33, `--gallery`) with team switcher (HCM, NGG, SMS, NCV, IRV); gallery screenshots + fresh-subagent QA; `Gallery_Complete`, `FocusVisible_Themes` tests

## In progress
- _(none)_

## Done
- R1T2 · Phase 1 complete: spec 01 tokens (legacy keys aliased), typography (Barlow Condensed ExtraBold + Italic added), geometry, motion, Phosphor icons (MIT), `Styles.axaml` text roles; `TeamTheme` accent algorithm + `ThemeResources` (app/page scope, Fluent accent, CVD palette); asset resolver (fallbacks, path guard, off-thread Skia decode, LRU cache, issues/stats, catalog from pack); `Fmt`; `UiSettings`/`UiPaths`/store; `ASSET_LICENSES.md`; UiRelease: Tokens_NoHardcodedValues, Bindings_Compiled, Contrast_Tokens, Contrast_TeamColors, Assets_PathSafety, Assets_NoUiThreadDecode (+ resolver unit tests).
- R1T1 · Phase 0 complete: WIP parked on `wip/attributes-a3a`; branch cut; spec pack + CLAUDE.md rules committed; `AUDIT.md` filled (environment, baseline 1 warning / 1021 tests, commands, inventory of 29 screens, 8 decisions); `FOLLOWUPS.md` F01–F13; fixture league `tests/Fixtures/UiLeague` (30 teams, 570 players, seed 42); `tests/BallGM.Client.Avalonia.UiTests` (Skia headless, shell driver, binding sink, mod fixtures good/broken/stress); `before/` = 39 PNGs (13 baseline screens × 3 sizes).

## Remaining by phase
- **0 · Baseline:** done
- **1 · Foundation:** done (settings are stored but not yet applied at runtime — wired with the shell in Phase 3)
- **2 · Components:** library per spec 03 · Component Gallery · first QA pass
- **3 · Shell:** header band · nav rail · page header and tabs · Continue states · back/forward · search · ticker · dialogs · toasts · hover cards · context menus · crash dialog
- **4 · Screens:** every inventory screen in priority order · UI-only screens
- **5 · Mods UX:** Mods & Graphics screen · entity IDs · robustness and stress · cache tuning · `docs/modding/GRAPHICS.md`
- **6 · Release:** accessibility · settings persistence · performance · error handling · P2 or deferral · docs · full QA pass · final report

## Turn log
| Run | Turn | Phase | What changed | Commits | Build | Tests |
|---|---|---|---|---|---|---|
| 1 | 1 | 0 | Baseline audit, fixture league, mod fixtures, screenshot harness, before/ capture | a9d15f6, e2b21fa | green (0 err) | 1022/1022 |
| 1 | 2 | 1 | Tokens, type, geometry, motion, icons, team theming, resolver, formatting, settings; lint/contrast/resolver tests | 33a82a8, 52e872b | green (0 err, 0 new warn) | 1051/1051 |
