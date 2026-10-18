# UI Release — Progress Ledger

> Maintained by Claude Code. Read at the start of every turn, update at the end (spec 00 §9).
> A turn started by a `/goal` command begins a new run: increment **Run**, set **Turn** to 1.

**Run:** 1 · **Turn:** 2/40 · **Phase:** 1 — Foundation
**Base branch:** `main` @ `2c42a3c` · **Work branch:** `ui/release-overhaul`
**Last updated:** 2026-10-18 (Run 1 · Turn 2, start)

## Next up
1. Phase 1 — tokens (spec 01 §3) as `Themes/Tokens.axaml` + typography (Barlow Condensed incl. ExtraBold, body Barlow) + geometry + motion resources; keep existing views compiling against renamed keys
2. Phase 1 — `TeamThemeService` (01 §4 accent algorithm) + Contrast_Tokens / Contrast_TeamColors / Tokens_NoHardcodedValues lint tests
3. Phase 1 — asset resolver core (05 §3–6) with vector fallbacks, off-UI-thread decode, LRU cache; formatting service; UI settings plumbing; icon set

## In progress
- _(none)_

## Done
- R1T1 · Phase 0 complete: WIP parked on `wip/attributes-a3a`; branch cut; spec pack + CLAUDE.md rules committed; `AUDIT.md` filled (environment, baseline 1 warning / 1021 tests, commands, inventory of 29 screens, 8 decisions); `FOLLOWUPS.md` F01–F13; fixture league `tests/Fixtures/UiLeague` (30 teams, 570 players, seed 42); `tests/BallGM.Client.Avalonia.UiTests` (Skia headless, shell driver, binding sink, mod fixtures good/broken/stress); `before/` = 39 PNGs (13 baseline screens × 3 sizes).

## Remaining by phase
- **0 · Baseline:** done
- **1 · Foundation:** tokens · typography and fonts · geometry · motion · icons · team theming · formatting · UI settings plumbing · resolver core and fallbacks
- **2 · Components:** library per spec 03 · Component Gallery · first QA pass
- **3 · Shell:** header band · nav rail · page header and tabs · Continue states · back/forward · search · ticker · dialogs · toasts · hover cards · context menus · crash dialog
- **4 · Screens:** every inventory screen in priority order · UI-only screens
- **5 · Mods UX:** Mods & Graphics screen · entity IDs · robustness and stress · cache tuning · `docs/modding/GRAPHICS.md`
- **6 · Release:** accessibility · settings persistence · performance · error handling · P2 or deferral · docs · full QA pass · final report

## Turn log
| Run | Turn | Phase | What changed | Commits | Build | Tests |
|---|---|---|---|---|---|---|
| 1 | 1 | 0 | Baseline audit, fixture league, mod fixtures, screenshot harness, before/ capture | a9d15f6, e2b21fa | green (0 err) | 1022/1022 |
