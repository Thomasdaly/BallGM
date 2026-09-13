---
name: sim-audit
description: Adversarial statistical validation of the basketball sim engine. Runs conservation, distribution, correlation, temporal, calibration and economy batteries against committed targets, then writes one diagnostic report. Use when asked to audit, validate, stress-test or sanity-check sim output or realism.
argument-hint: [all | battery ids e.g. "1 3 4"] [--seasons N]
disable-model-invocation: true
context: fork
background: false
effort: high
allowed-tools: Read Grep Glob Write Bash(python3 *) Bash(node *) Bash(npm run *)
---

# GOAL

Produce one diagnostic report at `reports/sim-audit-${CLAUDE_SESSION_ID}.md`
telling me where this engine is statistically wrong and why. Nothing else.

Batteries requested: $ARGUMENTS (default `all`).
Full spec: `${CLAUDE_SKILL_DIR}/batteries.md`
Numeric targets: `${CLAUDE_SKILL_DIR}/targets.json`
Harness, if already recorded: `${CLAUDE_SKILL_DIR}/harness.md`

Read all three before touching the codebase. Targets are data — read the values
from the file, never from memory.

# CONSTRAINTS

- **Adversarial posture.** You are trying to break this engine, not confirm it
  works. A finding you cannot support is worth less than an admitted gap.
- **Assume nothing not in the code.** If a mechanism isn't implemented, that is a
  finding, not something to infer from how the sport works.
- **No fixes.** Diagnosis only. Root-cause hypotheses are in scope; patches,
  parameter values and code edits are not. Those belong to `/sim-patch`.
- **Measure, don't eyeball.** Every row in the findings table needs a computed
  number. "Looks about right" is not a value. If you cannot compute it, it goes
  in section E.
- **Fixed seeds.** Every Monte Carlo run must be reproducible. Record the seeds
  in the report.
- **Distinguish sampling noise from bias.** With N seasons, report whether a
  deviation survives its own standard error before calling it a finding.
- **Do not edit game code.** Analysis scripts go in `scratch/audit/`. Nothing
  under the engine's source tree is writable during an audit.
- **One file out.** Do not narrate progress into chat beyond a one-line
  completion notice and the verdict.

# PROCEDURE

1. **Find the harness.** If `harness.md` exists, follow it. If not: locate the
   sim entrypoint (look for a season-sim CLI, a test fixture, or an exported
   `simSeason`/`simGame` function), work out how to run N headless seasons with a
   seed and dump per-game and per-player output to disk, then write what worked
   to `${CLAUDE_SKILL_DIR}/harness.md` as a numbered recipe. Do this once. Later
   runs must not rediscover it.
2. **Run the batteries** named in `$ARGUMENTS`, in order. Battery 1 first: if any
   check in it has a nonzero violation rate, record it, finish Battery 1, then
   stop and write the report. Downstream distributional results are meaningless
   on top of a conservation bug.
3. **Write the report** in the exact format at the end of `batteries.md`.
4. **Reply in chat with the verdict line and the report path.** Nothing more.

# ANTI-PATTERNS TO CHECK FOR EXPLICITLY

These are the two failure modes that account for most broken basketball sims.
Name them directly in the report if present, even if the batteries pass:

- **Linear talent aggregation** (Battery 4.4). Five on-court players' rates summing
  linearly into a team rate makes "acquire max total rating" optimal and kills
  every roster decision in the game.
- **Gaussian talent generation** (Battery 3.3). Normal draws produce a league with
  no franchise-bending outlier. Statistically fine, emotionally flat.
