# UI Release — Progress Ledger

> Maintained by Claude Code. Read at the start of every turn, update at the end (spec 00 §9).
> A turn started by a `/goal` command begins a new run: increment **Run**, set **Turn** to 1.

**Run:** 1 · **Turn:** 4/40 · **Phase:** 4 — Screens
**Base branch:** `main` @ `2c42a3c` · **Work branch:** `ui/release-overhaul`
**Last updated:** 2026-10-21 (Run 1 · Turn 4, end)

## Next up
1. Phase 4 — S13 Standings (new nav section, T2, conference/division/league toggle, play-in and elimination lines), then the remaining legacy views: S18 Trades, S19 Free agency, S20 Negotiations, S14 Leaders, S23 Finances, S34 Contracts, S35 Draft picks, S36 Front office (their binding warnings keep `Screens_RenderMatrix` red, so `Release/ScreenTests.cs` stays uncommitted until they are rebuilt)
2. Phase 4 — UI-only screens: S01 Splash, S02 Title, S03 New career, S04 Load/save, S05 Home, S08 Rotation, S11 Team profile, S26 Bracket, S29 Settings, S30 Mods & Graphics, S31 Credits, S32 Dialogs; S16 Compare (F16)
3. Visual QA pass 4 on S07, S10, S12, S15, S17 and the gallery (verify the pass-3 fixes; triage the pass-3 S3s)

## In progress
- _(none)_

## Done
- R1T4 · Gallery QA passes 1–3 recorded and every S1/S2 fixed (focus-ring gaps and outside adorners, hero bands in every section, menu shadows and icons, table selection persistence, user-row stripe, column-shaped skeletons, placeholder/selection contrast with a test). Phase 3 shell (b679ab1): header band, collapsible nav rail, page header and breadcrumb, Continue states, back/forward, search, ticker, toasts, dialogs. Phase 4: S07 Roster (T2, AGE, filters, cards), S10 Player profile (T3, team-scoped hero, actions, tabs), S12 Calendar (T8), S15 Pre-game and S17 Box score (T7) with GO TO GAME / SIM GAME. Decisions 24–27; F15–F16.
- R1T3 · Phase 2 library built: SkewBorder, CutCornerBorder, Button/ToggleButton themes, ContinueButton, EntityLink, NavRail, PageHeader, Breadcrumb, Stepper, SkewTabStrip, SegmentedControl, Badge, SearchBox, RangeFilter, FilterBar, Tile, StatTile, CountUpText, RatingBadge, AttributeBar, StatusChip, PositionChip, KeyValueList, AssetImage/PlayerHeadshot/TeamLogo, PlayerCard, TeamCard, HoverCard, FormSparkline, WinLossStrip, ComparisonBar, CapBar, Linescore, ScoreBug, Ticker, BracketView, CalendarMonth, Timeline, NewsCard, DataTable (+ column chooser), EmptyState, ErrorState, Banner, Skeleton, ProgressOverlay, ToastHost, DialogHost, CrashDialog; Component Gallery (9 sections, `--gallery` launch still to wire); display font fix (separate collections); `DESIGN_SYSTEM.md`; UiRelease: Table_Virtualization, Gallery_Complete, FocusVisible_Themes (+ gallery render test).
- R1T2 · Phase 1 complete: spec 01 tokens (legacy keys aliased), typography (Barlow Condensed ExtraBold + Italic added), geometry, motion, Phosphor icons (MIT), `Styles.axaml` text roles; `TeamTheme` accent algorithm + `ThemeResources` (app/page scope, Fluent accent, CVD palette); asset resolver (fallbacks, path guard, off-thread Skia decode, LRU cache, issues/stats, catalog from pack); `Fmt`; `UiSettings`/`UiPaths`/store; `ASSET_LICENSES.md`; UiRelease: Tokens_NoHardcodedValues, Bindings_Compiled, Contrast_Tokens, Contrast_TeamColors, Assets_PathSafety, Assets_NoUiThreadDecode (+ resolver unit tests).
- R1T1 · Phase 0 complete: WIP parked on `wip/attributes-a3a`; branch cut; spec pack + CLAUDE.md rules committed; `AUDIT.md` filled (environment, baseline 1 warning / 1021 tests, commands, inventory of 29 screens, 8 decisions); `FOLLOWUPS.md` F01–F13; fixture league `tests/Fixtures/UiLeague` (30 teams, 570 players, seed 42); `tests/BallGM.Client.Avalonia.UiTests` (Skia headless, shell driver, binding sink, mod fixtures good/broken/stress); `before/` = 39 PNGs (13 baseline screens × 3 sizes).

## Remaining by phase
- **0 · Baseline:** done
- **1 · Foundation:** done (settings are stored but not yet applied at runtime — wired with the shell in Phase 3)
- **2 · Components:** re-verify pass-3 fixes in a fresh pass; triage S3s
- **3 · Shell:** done (hover cards on links and context menus still to wire per screen)
- **4 · Screens:** done S07, S10, S12, S15, S17 · to do S13, S14, S18, S19, S20, S23, S34, S35, S36, S37 and the UI-only screens; commit `Screens_RenderMatrix`
- **5 · Mods UX:** Mods & Graphics screen · entity IDs · robustness and stress · cache tuning · `docs/modding/GRAPHICS.md`
- **6 · Release:** accessibility · settings persistence · performance · error handling · P2 or deferral · docs · full QA pass · final report

## Turn log
| Run | Turn | Phase | What changed | Commits | Build | Tests |
|---|---|---|---|---|---|---|
| 1 | 1 | 0 | Baseline audit, fixture league, mod fixtures, screenshot harness, before/ capture | a9d15f6, e2b21fa | green (0 err) | 1022/1022 |
| 1 | 2 | 1 | Tokens, type, geometry, motion, icons, team theming, resolver, formatting, settings; lint/contrast/resolver tests | 33a82a8, 52e872b | green (0 err, 0 new warn) | 1051/1051 |
| 1 | 3 | 2 | Component library, gallery, data table, library lints; display-font fix | 0330073, 513b984, aa29945, f2320b1 | green (0 err, 0 new warn) | 1057/1057 |
| 1 | 4 | 2–4 | Gallery QA passes 1–3 and fixes; shell; roster, profile, calendar, pre-game, box score | b679ab1, d368efa, 666800f, 49904d4, 34b84f3, 0501005 | green (0 err, baseline warning only) | 1058/1058 (matrix uncommitted) |
