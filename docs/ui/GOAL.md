# Running the UI release overhaul with `/goal`

`/goal` keeps Claude Code working turn after turn until a separate evaluator model judges the condition met. The evaluator reads only the conversation — it can't open files or run commands — so the condition makes Claude **print fresh proof** in its last turn, and the detail lives in the spec pack it points to. Conditions are capped at 4,000 characters; the main goal below is 3,108 characters and the Phase 0 goal is 1,188.

## Setup (once)

1. Unzip the pack at the repo root (it adds `docs/ui/` and `CLAUDE_MD_SNIPPET.md`) and commit `docs/ui/` on your base branch.
2. Paste `CLAUDE_MD_SNIPPET.md` into the project's `CLAUDE.md` (create one if needed), delete the snippet file, and commit. `CLAUDE.md` is loaded into every session and isn't summarized away by compaction, so the hard rules survive long runs.
3. Start Claude Code in the repo, on the base branch. Use **auto mode** so goal turns don't stall on permission prompts.
4. Check progress at any time with `/goal` (no argument): it shows the condition, turns, tokens spent and the evaluator's latest reason.

## Recommended first: Phase 0 only

Builds the audit, fixtures, test harness and `before/` screenshots without touching the UI, so you can check the Screen Inventory and decisions before the long run.

```text
/goal Complete only Phase 0 (Baseline) of docs/ui/spec/00_START_HERE.md, then stop. Read every spec file it lists. On branch ui/release-overhaul: fill every section of docs/ui/AUDIT.md (environment, baseline numbers, exact verification commands, Screen Inventory); gitignore artifacts/; build the fixture league, the mod fixtures and the headless screenshot harness described in docs/ui/spec/06_QUALITY_GATES.md; capture artifacts/ui-screens/before for every baseline screen at 1280x720, 1920x1080 and 2560x1440. Follow the turn protocol in 00_START_HERE.md section 9 with docs/ui/PROGRESS.md. Change no views, styles or game code except what the test harness strictly needs, and list every such change in AUDIT.md Decisions.

The goal is met when your latest turn freshly runs and prints: the Screen Inventory table; the count of before PNGs against baseline screens x 3, with any render failures noted in AUDIT.md; dotnet build -c Release exiting 0; and dotnet test exiting 0, with git diff --stat against the base branch showing no pre-existing test changed. Also met if the current run in PROGRESS.md reaches turn 15 and the latest turn prints a green build plus the remaining Phase 0 work.
```

Then review `docs/ui/AUDIT.md` (Screen Inventory and Decisions) and `artifacts/ui-screens/before/`, and adjust the spec if anything is off.

## The main goal

```text
/goal Bring this Avalonia NBA management game's UI to release quality: Football Manager depth and mod model (user-added rosters, photos, logos) presented like NBA 2K MyNBA and NBA Live. First read docs/ui/spec/00_START_HERE.md and every spec file it lists; together they are the source of truth for design, scope, phases, tests and process. Work on branch ui/release-overhaul, commit after each finished step, keep the build green at the end of every turn, and begin and end every turn with docs/ui/PROGRESS.md (run and turn counter, done, next, remaining), assuming your context may have been compacted.

The goal is met only when your latest turn freshly runs the checks and prints the final report defined in docs/ui/spec/06_QUALITY_GATES.md section 8.1, with each command and its real output, showing all of:
1. dotnet build -c Release exits 0 with zero XAML or binding warnings, and compiler warnings at or below the baseline in docs/ui/AUDIT.md.
2. dotnet test on the whole solution exits 0 with 0 failed and 0 skipped, and git diff --stat against the base branch shows no pre-existing test deleted, skipped or weakened.
3. Every UiRelease test in 06_QUALITY_GATES.md passes and is listed by name, covering at least: every screen in the AUDIT.md inventory rendered headless at 1280x720, 1920x1080 and 2560x1440 plus 150% UI scale; zero hard-coded colors, font families or font sizes in views; compiled bindings everywhere; WCAG contrast for every text token and all 30 fixture team colors; missing, corrupt, oversized and 5,000-image mod packs loading with fallbacks, zero crashes and no image decoding on the UI thread; a 1,000-row table realizing under 100 rows; Tab traversal with visible focus and no traps.
4. artifacts/ui-screens/before holds a PNG for every screen that existed at baseline (or an AUDIT.md note with the error that prevented it), artifacts/ui-screens/after holds one for every inventory screen at every required size, and docs/ui/VISUAL_QA.md shows every after screenshot reviewed by a fresh subagent that did not write the code, with zero open S1 or S2 defects.
5. Every acceptance item in 06_QUALITY_GATES.md section 7 is PASS with an evidence pointer, or DEFERRED only where that table allows it and logged in docs/ui/FOLLOWUPS.md.

Constraints: do not change simulation, game rules, save format, mod on-disk format or entity IDs; no Avalonia major-version upgrade; no paid dependencies; no NBA, team, league, 2K or EA logos, photos, player likenesses, fonts or art in the base build or fixtures; no features that need new game logic (log them in FOLLOWUPS.md); never weaken, skip or delete a test, check or threshold to get green. A blocked item is logged in FOLLOWUPS.md with its reason and does not make this goal impossible.

Checkpoint: the goal is also met when the current run in PROGRESS.md reaches turn 40 (each new /goal start begins a new run at turn 1), the latest turn prints the checkpoint report from 06_QUALITY_GATES.md section 8.2 with a green build and green pre-existing tests, and PROGRESS.md lists all remaining work, so the same goal can be re-run to continue.
```

## Continuing after a checkpoint

After 40 turns in a run, Claude prints a checkpoint report and the goal clears. Start a fresh session (or `/clear`) and paste the same main goal. `PROGRESS.md` carries the state; the new run starts at turn 1.

## Tuning

- **Turn budget:** change `40` in the goal, in `PROGRESS.md` and in spec 00 §9 together.
- **Scope cuts:** mark an item Deferrable in spec 06 §7 rather than deleting it, so the decision stays visible.
- **Design changes:** edit spec 01 (tokens, fonts, palettes) before the main run; the contrast tests enforce whatever values you choose.
- **Different fixture colors or teams:** edit spec 06 §1.1; keep the hard cases.
