# Sim audit kit

Drop `.claude/` into your repo root. Three commands:

- `/sim-audit all` — full diagnostic, writes `reports/sim-audit-<session>.md`
- `/sim-patch <report>` — turns that report into an ordered patch plan
- `/sim-regress` — fast pass/fail gate for CI and pre-commit

Calibration targets live in `.claude/skills/sim-audit/targets.json`. Edit them to
match your game's era before the first run. They are approximate modern-NBA
anchors; verify against basketball-reference.com.

`/sim-audit` records how to run your engine headlessly into
`.claude/skills/sim-audit/harness.md` on its first run. Commit that file —
later runs and `/sim-regress` both read it instead of rediscovering the entrypoint.
