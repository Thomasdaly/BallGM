# UI Release Overhaul — Start Here

This pack is the source of truth for lifting the game's Avalonia UI to release quality. It is written for Claude Code running under `/goal`, and for the people reviewing that work.

**The target in one line:** Football Manager's depth, information architecture and mod model, presented with the energy of NBA 2K's MyNBA and EA's NBA Live franchise menus.

---

## 1. Reading order

Read all seven spec files before changing code. Re-read the relevant one at the start of each phase.

| # | File | What it settles |
|---|---|---|
| 00 | `docs/ui/spec/00_START_HERE.md` | Scope, hard rules, phases, turn protocol (this file) |
| 01 | `docs/ui/spec/01_DESIGN_LANGUAGE.md` | Look and feel: references, tokens, team theming, type, shape, motion, imagery, data viz, anti-patterns |
| 02 | `docs/ui/spec/02_AVALONIA_GUIDE.md` | How to build it in Avalonia: theme structure, control themes, bindings, fonts, transitions, virtualization, images, headless tests |
| 03 | `docs/ui/spec/03_COMPONENTS.md` | Component library: every component, variant and state, plus the Component Gallery |
| 04 | `docs/ui/spec/04_SHELL_AND_SCREENS.md` | App shell, navigation, keyboard map, screen templates, per-screen specs, wireframes |
| 05 | `docs/ui/spec/05_MOD_GRAPHICS.md` | Asset resolver, fallbacks, decoding and cache, Mods & Graphics screen, modder docs |
| 06 | `docs/ui/spec/06_QUALITY_GATES.md` | Fixture league, UiRelease test suite, budgets, visual QA rubric, acceptance checklist, report formats |

## 2. Ledger files (you maintain these)

| File | Purpose | Update |
|---|---|---|
| `docs/ui/PROGRESS.md` | Run and turn counter, phase, done / next / remaining, turn log | Start and end of every turn |
| `docs/ui/AUDIT.md` | Baseline facts, exact verification commands, **Screen Inventory**, decisions log | Phase 0, then whenever a decision is made |
| `docs/ui/FOLLOWUPS.md` | Everything deferred, blocked or out of scope, with the reason | The moment something is deferred |
| `docs/ui/VISUAL_QA.md` | Screenshot reviews, defects, fixes, verification | From Phase 2 onward |

Outputs you create along the way: `docs/ui/DESIGN_SYSTEM.md`, `docs/ui/ASSET_LICENSES.md`, `docs/modding/GRAPHICS.md`, a `CHANGELOG` entry, and screenshots under `artifacts/ui-screens/before/` and `artifacts/ui-screens/after/` (gitignored).

The spec files are read-only for you. If one is wrong or silent, follow §8 instead of editing it.

## 3. Product context

- An NBA franchise management sim in the Football Manager mould: deep data, an inbox-driven loop, time advanced with a Continue button, the user as general manager.
- Mods work like FM: users drop roster/database files, player photos and team logos into user folders, and files map to entity IDs. **The base game must look finished with zero mods installed, and better with them.**
- The UI is Avalonia. Find out which version, MVVM library, base theme, grid control and test framework the project uses during Phase 0 and record them in `AUDIT.md`. Adapt the mechanisms in `02` to what you find; keep the outcomes.

## 4. Scope

**In scope**
- A token-based visual design system (01) implemented as Avalonia themes (02).
- A component library and dev-only Component Gallery (03).
- The app shell and navigation (04 §1–4).
- Restyling **every existing screen** to the templates and specs in 04. No existing screen is optional.
- UI-only additions that need no new game logic (marked **UI** in 04): settings for UI options, Mods & Graphics, Credits & Licenses, global search over existing data, loading / empty / error states, crash dialog, hover cards, back/forward history.
- Asset presentation and the mod-graphics experience (05).
- Accessibility, UI performance, UI robustness, the headless UI test suite, and documentation (06).

**Out of scope** (log to `FOLLOWUPS.md` if you hit them)
- Simulation, AI, game rules, balance, save format, mod on-disk format, entity IDs.
- New game features, or controls that would need game logic that doesn't exist yet.
- Localization of strings, gamepad input, Avalonia major-version migration, installers and store packaging.

## 5. Hard rules

1. **Game logic is frozen.** No changes to simulation, AI, rules, save format, mod on-disk format or entity IDs. View models may gain presentation-only members; services may gain UI-facing helpers (asset resolver, formatting, theming).
2. **No Avalonia major-version upgrade** (e.g. 11 → 12). A patch or minor bump only if a defect requires it — record it in `AUDIT.md`.
3. **No paid or commercially licensed dependencies.** New dependencies must be MIT, BSD, Apache-2.0, ISC or SIL OFL, and are recorded in `docs/ui/ASSET_LICENSES.md`.
4. **IP: evoke the genre, copy nothing.** No NBA, WNBA, team or league logos, real player names or likenesses, and no 2K or EA fonts, layouts, icons or art in the base build or the test fixtures. Real-world assets arrive only through user mods. Fixture data is fictional (06 §1).
5. **No faked features.** If a screen needs logic that doesn't exist, build nothing for that part and log it in `FOLLOWUPS.md`.
6. **Never weaken a check.** Don't delete, skip, loosen or special-case a test, lint rule or threshold to get green. If a check itself is wrong, fix it in its own commit and explain why in `AUDIT.md` → Decisions.
7. **Branch and commits.** Work on `ui/release-overhaul` cut from the base branch recorded in `AUDIT.md`. Commit after each finished step with a message starting `ui:`. The build is green at the end of every turn; if it isn't, fix it or revert the turn's last commit before ending.
8. **Keep the game playable** at every commit.
9. **Tests are isolated.** UI tests run against a temporary data and mod root; never read or write the real user folders.
10. **`artifacts/` is gitignored.** Never commit screenshots.

## 6. Phases

Work the phases in order. A phase is done when its exit check holds and is recorded in `PROGRESS.md`.

| Phase | Work | Exit check |
|---|---|---|
| **0 · Baseline** | Fill `AUDIT.md` (environment, baseline numbers, exact commands, Screen Inventory). Gitignore `artifacts/`. Build the fixture league and the headless screenshot harness (06 §1–3). Capture `before/` screenshots for every baseline screen at the three 100% sizes. **No visual changes yet.** | `AUDIT.md` complete; `before/` count = baseline screens × 3, apart from screens noted in `AUDIT.md` as unable to render |
| **1 · Foundation** | Tokens, typography and embedded fonts, geometry, motion, icon set, team theming service, formatting service, UI settings plumbing, asset resolver core with vector fallbacks (05 §3–6). | Token lint, contrast and resolver unit tests pass |
| **2 · Components** | Build the component library and the Component Gallery (03). | Gallery screenshots captured and reviewed in `VISUAL_QA.md` |
| **3 · Shell** | Header band, nav rail, page header and tab strip, Continue states, back/forward, search, ticker, dialogs, toasts, hover cards, context menus, crash dialog (04 §1–4). | Shell screenshots at all four sizes; keyboard test passes on the shell |
| **4 · Screens** | Restyle every inventory screen in priority order using its template (04 §5–6). Add the UI-only screens. | Per screen: `after/` screenshots + a `VISUAL_QA.md` entry |
| **5 · Mods UX** | Mods & Graphics screen, entity-ID display, stress and robustness work, cache tuning, `docs/modding/GRAPHICS.md` (05). | Asset tests pass |
| **6 · Release** | Accessibility, settings persistence, performance, error handling, remaining P2 work or deferral, docs, full visual QA, final report (06). | Every acceptance item PASS, or DEFERRED where 06 allows |

## 7. Priorities

- Every screen in the `AUDIT.md` inventory is restyled — none are optional. Priority (P0 → P1 → P2) only sets the **order**.
- UI-only additions and polish features carry a priority. **P0 and P1 are required.** **P2 may be DEFERRED** (logged in `FOLLOWUPS.md` with the reason) if the turn budget runs short.
- P2 items: custom window chrome, skin token overrides, animated draft-lottery reveal, ticker paging animation polish, shortcut overlay (`?`), saved table views.

## 8. When the spec is wrong or silent

- **Silent:** pick the option most consistent with the pillars in 01 §2. Record the decision in `AUDIT.md` → Decisions.
- **Conflicts with the codebase** (different MVVM library, grid control, navigation model): adapt the mechanism, keep the outcome. Record it.
- **Spec bug** (a check impossible as written): don't edit the spec. Record the problem and your adaptation in Decisions, keep the intent, and never loosen a threshold. If no faithful adaptation exists, log it as BLOCKED in `FOLLOWUPS.md` and continue with everything else.

## 9. Turn protocol

You are working under `/goal`: after each turn a separate evaluator reads the conversation (it cannot open files or run commands) and decides whether to continue. Your context may be compacted between turns. So:

**Start of every turn**
1. Read `docs/ui/PROGRESS.md`.
2. If this turn was started by a `/goal` command rather than an evaluator continuation, begin a **new run**: increment `Run`, set `Turn` to 1. Otherwise increment `Turn`. Save the file.
3. Make your first line: `Run R · Turn T/40 · Phase P · <what this turn will do>`.
4. Re-read the spec section for the work item.

**End of every turn**
1. Build green (rule 7). Commit.
2. Update `PROGRESS.md`: done, next three items, remaining by phase, a turn-log row.
3. Make your last lines a short status: what changed, build/test state, next item.
4. If `Turn` has reached 40, print the **checkpoint report** (06 §8.2) instead of starting new work.
5. If you believe every acceptance item is met, run everything fresh and print the **final report** (06 §8.1). Never print it from memory.

## 10. Subagents

- Subagents are fine for independent, well-bounded work (a single screen, a component family, a test). The main session integrates, builds and runs the full verification.
- **Visual QA must be done by a fresh subagent that has not seen the implementation** (06 §6). The work does not grade itself.
- A turn that ends while a subagent or background command is still running is not evaluated; finish or stop background work before ending a turn.
