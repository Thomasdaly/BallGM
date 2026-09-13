---
name: sim-regress
description: Fast regression gate for the basketball sim. Runs Battery 1 conservation checks plus the frozen assertion suite and reports failures. Use before committing engine changes or in CI.
argument-hint: [--seasons N]
disable-model-invocation: true
allowed-tools: Read Bash(python3 *) Bash(node *) Bash(npm run *)
---

# GOAL

Pass/fail gate, under two minutes. Not an audit.

Suite: `${CLAUDE_SKILL_DIR}/suite.md` (written by `/sim-patch`)
Targets: `${CLAUDE_SKILL_DIR}/../sim-audit/targets.json`
Harness: `${CLAUDE_SKILL_DIR}/../sim-audit/harness.md`

# CONSTRAINTS

- Battery 1 conservation checks always run, regardless of what `suite.md` says.
- Default 50 seasons unless `--seasons` overrides. Enough to catch a broken
  functional form, not enough to resolve a 1% calibration drift — that is what
  `/sim-audit` is for. Say so if a result is borderline.
- Report only failures plus a one-line pass count. No commentary on passing checks.
- Do not fix anything. Report and stop.
- If `suite.md` does not exist, run Battery 1 only and say the suite is unfrozen.

# OUTPUT

```
PASS 11/12  |  seeds: <list>
FAIL 4.4-orb  slope 0.93  band [0.40, 0.70]  <- linear aggregation regressed
```
