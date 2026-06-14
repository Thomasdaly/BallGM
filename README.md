# Basketball Front Office Sim — Codex Handoff

This repository handoff turns the original project discussion into durable project context for Codex.

## Recommended workflow

Use the Codex desktop app as the main command centre, an IDE for manual review and focused edits, and the CLI for terminal-native work.

1. Create or open a local Git repository.
2. Copy the contents of this handoff folder into the repository root.
3. Open that repository as a project in the Codex app.
4. Start with `prompts/00-project-audit.md`.
5. Review the proposed architecture before permitting broad implementation.
6. Run one bounded milestone at a time.
7. Create a Git checkpoint after each accepted milestone.

## Important principle

The simulation engine must be a pure .NET library. Godot is the presentation layer, not the owner of the league, contract, salary-cap, draft-pick, trade, or simulation rules.

## Initial intended stack

- Godot 4 with C# for the desktop client
- Modern supported .NET SDK for pure C# libraries
- xUnit for tests
- System.Text.Json for configuration and save DTOs
- SQLite only where structured persistence becomes useful
- JSON-based, schema-versioned data packs and mods
- GitHub Actions for CI
- Steam integration later, behind an adapter

## Before implementing features

Codex should first:
- inspect these documents;
- check installed tooling;
- propose a solution structure;
- identify assumptions and risks;
- scaffold a minimal buildable solution;
- run tests and report exact results.
