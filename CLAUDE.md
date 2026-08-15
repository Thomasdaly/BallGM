# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

BallGM: a fictional, moddable, cross-platform basketball front-office and league simulation game, aimed at a future Steam release. No 3D match engine — the game is management depth, rules accuracy, long-term simulation quality, and explainable AI decisions. Think Football Manager, not NBA 2K.

Full context lives in `docs/`:
- `docs/vision.md` — pillars and non-goals
- `docs/product-scope.md` — MVP system list, vertical-slice target
- `docs/architecture.md` — solution structure and dependency rules
- `docs/domain-language.md` — canonical terminology and aggregate boundaries
- `docs/roadmap.md` — milestone sequence (M0–M12); a thin Avalonia UI slice now lands at Milestone 2, not at the end

Read the relevant doc before touching a system you haven't worked in yet. Don't re-derive decisions already recorded there.

## Current state (check before assuming)

As of this writing: Milestones 0 (repo/architecture proof), 1 (league and roster foundation), 2 (thin playable UI slice), 3 (contracts and cap ledger), and 4 (draft assets) are done. The cap sheet is real, backed by `Contract`, `CapCharge`/`CapChargeProjection`, `TransactionLedger` (Domain), `CapLedger` (Rules), and the `ICapLedger` port/`RulesCapLedger` adapter pair. The pick board is real too, backed by `DraftPick`/`PickOwnership`/`DraftAssetBook`, `PickProtection`, `DraftOrderSnapshot` (Domain), `PickConveyanceEvaluator`/`PickOwnershipRules`/`DraftAssetLedger` (Rules), and the `IDraftAssetLedger` port/`RulesDraftAssetLedger` adapter — same shape as the cap ledger, deliberately. The league ruleset file is at schema version 2 and version-checked on load. Milestone 5 (trade engine) is next, and now has both halves it needs: cap ledger from M3, pick-ownership validation from M4. Earlier state, still true — `BallGM.Domain` has `League` and `Team` aggregates (created via non-throwing `Create(...)` factories returning `DomainOperationResult<T>`, not public constructors), `FranchiseId`, `PlayerId`, `LeagueId`, `TeamId`, `RosterSizeLimits`, a shared `DomainOperationResult`/`DomainOperationResult<T>`/`DomainError` result kernel (also used by `BallGM.Rules`, which has no result type of its own anymore), and `SortableId` for minting ULID-shaped identifiers. This list goes stale fast — `git log` and the actual `src/` tree are the source of truth, not this file.

## Stack

| | |
|---|---|
| Language | C# 14, `net10.0`, nullable enabled |
| Desktop UI | Avalonia (client project only) |
| Tests | xUnit |
| Serialization | `System.Text.Json` |
| Persistence | filesystem now; SQLite only if/when justified |
| Mods/data | JSON, schema-versioned data packs |
| CI | GitHub Actions — restore/format/build/test on Windows, macOS, Linux |
| SDK pin | `global.json` → 10.0.301, `rollForward: latestFeature` |

## Solution layout and dependency direction

```
BallGM.Domain                                  (no project references — ever)
BallGM.Application    -> Domain
BallGM.Rules          -> Domain
BallGM.Simulation     -> Domain, Rules
BallGM.Infrastructure -> Application, Domain
BallGM.Mods           -> Application, Domain
BallGM.Client.Avalonia -> Application
BallGM.DataValidator  -> Mods
```

Hard rules, enforced by `tests/BallGM.Integration.Tests/ArchitectureBoundaryTests.cs` — treat a violation as a build-breaking bug, not a style nit:
- `BallGM.Domain` has zero project references.
- Only `BallGM.Client.Avalonia` may reference Avalonia packages. The simulation core must never see Avalonia.
- UI views/view models must not contain league or CBA-style rules — that logic lives in `BallGM.Rules`.
- Domain logic must be fully testable without launching the client.
- External services, persistence, Steam, and engine APIs sit behind interfaces in `BallGM.Infrastructure`.
- Runtime models are separate types from save/mod DTOs — don't serialize domain entities directly.

## Commands

```bash
./tools/verify-dotnet.sh          # full pipeline: restore, format check, build, test
```

Equivalent steps individually:

```bash
dotnet restore BallGM.slnx
dotnet format BallGM.slnx --verify-no-changes --no-restore
dotnet build BallGM.slnx --configuration Release --no-restore -p:UseSharedCompilation=false
dotnet test BallGM.slnx --configuration Release --no-build
```

Never report a command as passing unless you actually ran it and saw it pass.

## Product priorities (in order — use this to break ties)

1. Correct rules and invariants
2. Deterministic, reproducible simulation
3. High-quality tests
4. Moddability
5. Explainable AI decisions
6. Save compatibility and migration
7. Performance
8. UI polish

## Coding standards

- Nullable reference types on; no suppressing warnings to move faster.
- Use domain terminology from `docs/domain-language.md` (League, Franchise, Team, Contract, Cap charge, Threshold, Draft pick, Pick ownership, Protection, Swap right, Transaction, Ruleset, Simulation seed, Data pack) — don't invent synonyms.
- Value objects for money, season/year, and every identifier type (team, player, pick, franchise, league). Money as integer smallest-units or a dedicated type — never a raw `decimal`/`double` for cap math. Mint new identifiers with `BallGM.Domain.Common.SortableId.NewId()`, not ad hoc GUIDs or counters — see `docs/domain-language.md` for why.
- Inject time, randomness, and identifier generation wherever they affect test determinism. Simulation code must accept a supplied seed and be reproducible from it.
- Explicit result types for rule validation — return machine-readable rule codes plus a human-readable explanation, not just a bool or a thrown exception, so failures are explainable to the player. Use the shared `DomainOperationResult`/`DomainOperationResult<T>`/`DomainError` kernel in `BallGM.Domain.Common` for this everywhere — don't add a second, layer-local result type.
- Aggregates expose a static `Create(...)` factory returning `DomainOperationResult<T>`, not a public throwing constructor. Only genuine programming errors (null required references) throw; business-rule violations that untrusted data-pack content can trigger (roster size, duplicate membership, and their equivalents on future aggregates) return a structured failure instead. See `docs/domain-language.md` → "Aggregate creation".
- Record transactions as an auditable ledger, not as silent mutation.
- Version save schemas and mod/data-pack schemas from the start — don't defer this to "later."
- Prefer explicit domain services and rule objects over large manager/god classes.
- Keep methods focused; avoid hidden global mutable state; avoid speculative abstraction and reflection-heavy magic.
- XML doc comments only where they clarify a non-obvious public API — not as decoration.

## Testing standards

- Unit-test domain invariants and individual rules in isolation.
- Integration-test multi-team transactions and save/load round-trips.
- Every confirmed bug gets a regression test.
- Simulation tests use seeded randomness — no flaky non-deterministic assertions.
- Cover unhappy paths (illegal trades, invalid league setup, roster overflow) as deliberately as the happy path.
- Test names describe business behavior (`Trade_RejectsWhenSalaryDoesNotMatchWithinTolerance`), not implementation.

## Safety and legal boundaries (non-negotiable)

- Fictional leagues, teams, players, logos, branding only.
- No NBA trademarks, no real team branding, no real player likenesses, no scraped proprietary datasets.
- Model rules generically and make them configurable rather than hardcoding one real-world league's current CBA.
- Never execute arbitrary mod code. Treat all imported mod/data-pack content as untrusted input — validate against the versioned schema before it touches domain logic.

## Change control — ask before

- Adding a production dependency.
- Changing a public API consumed across projects.
- Changing save or mod schema compatibility.
- A large refactor unrelated to the current task.
- Deleting data or generated assets.
- Changing the selected engine or a core architectural boundary listed above.

## Working on a task

1. Check `docs/` and the actual current code — don't assume a milestone doc's aspirational list is already implemented.
2. State assumptions and the smallest viable plan before writing code for anything non-trivial.
3. Keep changes bounded to the milestone/task at hand; don't drag in unrelated cleanup.
4. Add or update tests for any changed behavior.
5. Run `./tools/verify-dotnet.sh` (or the equivalent steps) before calling something done.
6. Update the relevant `docs/*.md` file when behavior or architecture actually changes — these docs are load-bearing, not historical.
7. Summarize changed files, trade-offs, risks, and follow-up work when reporting back.

A task is done only when: acceptance criteria are met, the solution builds, relevant tests pass, no engine dependency has leaked into the simulation core, and docs are updated if architecture or behavior changed.
