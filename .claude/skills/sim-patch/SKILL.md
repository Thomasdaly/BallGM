---
name: sim-patch
description: Convert a sim-audit report into an ordered patch plan with falsification tests. Reads the report file, fixes root causes rather than symptoms, and emits a frozen regression suite. Use after /sim-audit, or when asked how to fix simulation calibration findings.
argument-hint: [path/to/reports/sim-audit-*.md]
disable-model-invocation: true
effort: high
allowed-tools: Read Grep Glob
---

# GOAL

Turn the audit report at `$ARGUMENTS` into a patch plan I can execute.
Targets: `${CLAUDE_SKILL_DIR}/../sim-audit/targets.json`

If `$ARGUMENTS` is empty, use the most recent file matching `reports/sim-audit-*.md`.

# CONSTRAINTS

- **Fix section C, not section B.** Patch the root-cause clusters. A plan with one
  entry per finding means you skipped the collapse.
- **Classify every fix before prescribing it.** For each root cause, state which
  of these it is, because the three take different treatment:
  - `(a) wrong parameter value` — retune
  - `(b) wrong functional form` — replace the form, then retune
  - `(c) missing mechanism` — implement, then retune
  No amount of parameter tuning fixes (b) or (c). Say so when it applies.
- **Order by (expected metric improvement) / (blast radius).** State both numbers.
- **Declare coupling.** If a change moves more than one metric, give the joint
  adjustment, not the marginal one. Pace, ORtg and TOV% are never independent.
- **Prefer config over code.** Propose an engine rewrite only with an explicit
  argument for why a parameter or form change cannot reach the target.
- **No speculative patches.** If the report's section E says a thing was
  untestable, it does not get a patch. It gets a measurement request.
- **Plan only.** Do not edit files in this turn.

# OUTPUT — one document

## Per patch

1. Root-cause ID and the mechanism in one sentence
2. Classification: (a), (b) or (c)
3. Exact file / function / parameter, current value → proposed value
4. If (b) or (c): the replacement form with a short derivation. Reach for these
   where they fit, and say why the chosen one fits:
   - Gaussian copula or latent-factor model for correlated rating generation
   - a concave aggregation kernel for team-level rate stats, tuned so the
     observed stacking slope in Battery 4.4 lands inside its band
   - a convex shot-creation cost curve linking USG to TS
   - a lognormal or Pareto-spliced generator for the top decile of talent
   - a two-parameter asymmetric aging curve (fast rise, slow decline) with
     skill-specific peaks
5. Predicted post-fix value for **every** metric the change touches
6. **Falsification test** — a runnable assertion that proves the fix worked,
   written so it fails loudly if the fix regresses
7. Regression risk: which currently-passing metric this could break

## Then

- **Patch sequence.** Order matters: recalibrate after each structural change,
  since every parameter fit is conditional on the form it was fit under. State
  the recalibration point between steps.
- **Frozen regression suite.** The 12 assertions worth running on every future
  build, with tolerance bands, ready to paste into
  `.claude/skills/sim-regress/suite.md`.
- **What NOT to fix.** Findings that are technically wrong but load-bearing for
  game feel. Be specific about the feel they carry.

Keep code blocks under 30 lines each.
