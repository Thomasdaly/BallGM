# UI Release — Progress Ledger

> Maintained by Claude Code. Read at the start of every turn, update at the end (spec 00 §9).
> A turn started by a `/goal` command begins a new run: increment **Run**, set **Turn** to 1.

**Run:** 1 · **Turn:** 4/40 · **Phase:** 2 — Components
**Base branch:** `main` @ `2c42a3c` · **Work branch:** `ui/release-overhaul`
**Last updated:** 2026-10-18 (Run 1 · Turn 4, start)

## Next up
1. Phase 2 — fresh-subagent visual QA of the 18 gallery screenshots; record in `VISUAL_QA.md`; fix S1/S2; re-verify with another fresh subagent
2. Phase 3 — shell: header band, nav rail with collapse, page header + tabs, Continue states, back/forward, search (Ctrl+K), ticker, dialogs, toasts, hover cards, context menus, crash dialog, UI settings applied at runtime
3. Phase 4 — restyle every inventory screen (P0 first) on the new shell

## In progress
- _(none)_

## Done
- R1T3 · Phase 2 library built: SkewBorder, CutCornerBorder, Button/ToggleButton themes, ContinueButton, EntityLink, NavRail, PageHeader, Breadcrumb, Stepper, SkewTabStrip, SegmentedControl, Badge, SearchBox, RangeFilter, FilterBar, Tile, StatTile, CountUpText, RatingBadge, AttributeBar, StatusChip, PositionChip, KeyValueList, AssetImage/PlayerHeadshot/TeamLogo, PlayerCard, TeamCard, HoverCard, FormSparkline, WinLossStrip, ComparisonBar, CapBar, Linescore, ScoreBug, Ticker, BracketView, CalendarMonth, Timeline, NewsCard, DataTable (+ column chooser), EmptyState, ErrorState, Banner, Skeleton, ProgressOverlay, ToastHost, DialogHost, CrashDialog; Component Gallery (9 sections, `--gallery` launch still to wire); display font fix (separate collections); `DESIGN_SYSTEM.md`; UiRelease: Table_Virtualization, Gallery_Complete, FocusVisible_Themes (+ gallery render test).
- R1T2 · Phase 1 complete: spec 01 tokens (legacy keys aliased), typography (Barlow Condensed ExtraBold + Italic added), geometry, motion, Phosphor icons (MIT), `Styles.axaml` text roles; `TeamTheme` accent algorithm + `ThemeResources` (app/page scope, Fluent accent, CVD palette); asset resolver (fallbacks, path guard, off-thread Skia decode, LRU cache, issues/stats, catalog from pack); `Fmt`; `UiSettings`/`UiPaths`/store; `ASSET_LICENSES.md`; UiRelease: Tokens_NoHardcodedValues, Bindings_Compiled, Contrast_Tokens, Contrast_TeamColors, Assets_PathSafety, Assets_NoUiThreadDecode (+ resolver unit tests).
- R1T1 · Phase 0 complete: WIP parked on `wip/attributes-a3a`; branch cut; spec pack + CLAUDE.md rules committed; `AUDIT.md` filled (environment, baseline 1 warning / 1021 tests, commands, inventory of 29 screens, 8 decisions); `FOLLOWUPS.md` F01–F13; fixture league `tests/Fixtures/UiLeague` (30 teams, 570 players, seed 42); `tests/BallGM.Client.Avalonia.UiTests` (Skia headless, shell driver, binding sink, mod fixtures good/broken/stress); `before/` = 39 PNGs (13 baseline screens × 3 sizes).

## Remaining by phase
- **0 · Baseline:** done
- **1 · Foundation:** done (settings are stored but not yet applied at runtime — wired with the shell in Phase 3)
- **2 · Components:** first fresh-subagent QA pass and fixes · `--gallery` launch argument
- **3 · Shell:** header band · nav rail · page header and tabs · Continue states · back/forward · search · ticker · dialogs · toasts · hover cards · context menus · crash dialog
- **4 · Screens:** every inventory screen in priority order · UI-only screens
- **5 · Mods UX:** Mods & Graphics screen · entity IDs · robustness and stress · cache tuning · `docs/modding/GRAPHICS.md`
- **6 · Release:** accessibility · settings persistence · performance · error handling · P2 or deferral · docs · full QA pass · final report

## Turn log
| Run | Turn | Phase | What changed | Commits | Build | Tests |
|---|---|---|---|---|---|---|
| 1 | 1 | 0 | Baseline audit, fixture league, mod fixtures, screenshot harness, before/ capture | a9d15f6, e2b21fa | green (0 err) | 1022/1022 |
| 1 | 2 | 1 | Tokens, type, geometry, motion, icons, team theming, resolver, formatting, settings; lint/contrast/resolver tests | 33a82a8, 52e872b | green (0 err, 0 new warn) | 1051/1051 |
| 1 | 3 | 2 | Component library, gallery, data table, library lints; display-font fix | 0330073, 513b984, aa29945, f2320b1 | green (0 err, 0 new warn) | 1057/1057 |
