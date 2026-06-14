# Project instructions for Codex

## Product

Build a fictional, moddable, cross-platform basketball front-office and league simulation game for a future Steam release.

The product should prioritise management depth, rules accuracy, long-term simulation quality, and explainable AI decisions. It does not require a 3D match engine.

## Language and platform

- Use C# for production code.
- Use Avalonia with C# for the desktop presentation layer.
- Use pure .NET class libraries for simulation and domain logic.
- Target Windows, macOS, and Linux desktop platforms.
- Do not introduce JavaScript or TypeScript without an explicit approved architectural reason.

## Architecture boundaries

- The simulation core must not reference Avalonia.
- UI views and view models must not contain league or collective-bargaining rules.
- Domain logic must be testable without launching the game client.
- External services, persistence, Steam, and engine APIs must sit behind interfaces/adapters.
- Prefer explicit domain services and rule objects over large manager classes.
- Keep deterministic simulation paths available through supplied random seeds.
- Separate domain entities from save-file and mod-file DTOs.
- Use versioned schemas for saves and mods.

## Product priorities

1. Correct rules and invariants
2. Deterministic and reproducible simulation
3. High-quality tests
4. Moddability
5. Explainable AI decisions
6. Save compatibility and migration
7. Performance
8. UI polish

## Safety and legal boundaries

- Use fictional teams, leagues, players, logos, and branding.
- Do not add NBA trademarks, official team branding, real player likenesses, or scraped proprietary datasets.
- Describe rules generically and make them configurable.
- Do not execute arbitrary mod code.
- Treat imported mod content as untrusted input.

## Development workflow

For every implementation task:

1. Read the relevant files under `docs/`.
2. Inspect existing code before proposing changes.
3. State assumptions and the smallest viable plan.
4. Keep the change bounded and reviewable.
5. Add or update tests for changed behaviour.
6. Run format, build, and relevant tests.
7. Report exact commands and outcomes.
8. Summarise changed files, trade-offs, risks, and follow-up work.
9. Do not claim a command passed unless it was actually run successfully.

## Change control

Ask before:
- adding a production dependency;
- changing a public API used across projects;
- changing save or mod schema compatibility;
- performing a large refactor unrelated to the current task;
- deleting data or generated assets;
- changing the selected engine or core architectural boundaries.

## Coding standards

- Enable nullable reference types.
- Prefer clear domain terminology over generic names.
- Keep methods focused.
- Avoid hidden global mutable state.
- Make time, randomness, and identifiers injectable where they affect tests.
- Use value objects for money, season/year, team identifiers, player identifiers, and pick identifiers where useful.
- Validate invariants at domain boundaries.
- Prefer immutable data where practical.
- Add XML documentation only where it improves public API understanding; avoid noisy comments.
- Avoid premature optimisation, reflection-heavy magic, and speculative abstractions.

## Testing standards

- Unit-test domain invariants and individual rules.
- Integration-test multi-team transactions and save/load boundaries.
- Add regression tests for every confirmed bug.
- Use seeded randomness in simulation tests.
- Include unhappy paths and edge cases.
- Prefer descriptive test names expressing business behaviour.
- Add property-based tests later for complex ownership and transaction invariants if justified.

## Definition of done

A task is complete only when:
- acceptance criteria are implemented;
- the solution builds;
- relevant tests pass;
- no engine dependency leaks into the simulation core;
- the diff has been reviewed;
- documentation is updated when behaviour or architecture changes.
