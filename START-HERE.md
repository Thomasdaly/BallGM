# Start here in the Codex app

## First thread

Paste this into a new **Local** thread for this repository:

> Read `AGENTS.md`, `README.md`, and every file under `docs/`. Then execute the instructions in `prompts/00-project-audit.md`. Do not edit files until you have presented the audit and Milestone 0 plan.

Review the audit. Correct any major misunderstanding in the same thread.

## Second thread

After accepting the audit, create a new thread or continue the existing one and paste:

> Read the accepted audit, then execute `prompts/01-scaffold-milestone-zero.md`. Keep the change restricted to Milestone 0. Run the actual build and tests and report exact results.

## Review thread

After the scaffold completes, open a separate review thread, ideally in the same project, and paste:

> Review the current changes using `prompts/90-review-current-diff.md`. Do not edit during the first pass.

Address blocking findings before committing.

## Commit checkpoint

After review and successful tests:

```bash
git add .
git commit -m "chore: scaffold basketball simulation architecture"
```

## Continue milestone by milestone

Use one bounded prompt file per task. Do not ask Codex to implement the entire roadmap in one run.
