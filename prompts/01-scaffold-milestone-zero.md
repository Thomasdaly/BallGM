Read `AGENTS.md`, `docs/architecture.md`, and the accepted project-audit findings.

Implement only Milestone 0.

Required outcomes:

- Create the C# solution and minimum project structure.
- Enable nullable reference types and consistent build settings.
- Create a pure .NET domain library.
- Create a minimal application library.
- Create a rules library with no Godot dependency.
- Create test projects.
- Create a placeholder Godot C# client only if the required Godot tooling is installed; otherwise document the exact missing prerequisite and create no fake generated project.
- Add a deterministic random abstraction and one reproducibility test.
- Add a minimal versioned league-save envelope and one round-trip test.
- Add a structured validation result and one invalid-operation test.
- Add a suitable `.gitignore`.
- Add a CI workflow that restores, builds, and tests the pure .NET solution.
- Update architecture documentation to match what was actually built.

Before editing:
1. Inspect the repository.
2. State the plan and commands you intend to run.
3. Identify any assumption that would materially alter the scaffold.

After editing:
1. Run formatting where configured.
2. Run restore, build, and tests.
3. Report exact command results.
4. Review the diff for accidental coupling and overengineering.
5. Summarise changed files and remaining prerequisites.

Do not implement full teams, players, trades, contracts, or UI screens.
