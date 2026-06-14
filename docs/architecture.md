# Architecture direction

This is the current Milestone 0 architecture proof. It is intentionally small and is not permission to overbuild every layer immediately.

## Current solution structure

```text
BallGM.slnx
src/
  BallGM.Domain/
  BallGM.Application/
  BallGM.Rules/
  BallGM.Simulation/
  BallGM.Infrastructure/
  BallGM.Mods/
  BallGM.Client.Avalonia/
tests/
  BallGM.Domain.Tests/
  BallGM.Rules.Tests/
  BallGM.Simulation.Tests/
  BallGM.Integration.Tests/
tools/
  BallGM.DataValidator/
docs/
prompts/
```

## Responsibilities

### Domain

Entities, value objects, domain events, invariants, and domain-level calculations without engine, storage, network, or UI dependencies.

### Application

Use cases and orchestration such as submitting a trade, advancing the calendar, offering a contract, completing the draft, and loading a league.

### Rules

Configurable rulesets for roster limits, contracts, cap thresholds, transaction restrictions, pick trading, draft operation, and postseason qualification.

### Simulation

Games, statistics, development, ageing, injuries, market behaviour, and seeded random processes.

### Infrastructure

Persistence, filesystem access, SQLite if adopted, logging adapters, platform integrations, and repositories.

### Mods

Versioned external schemas, loading, validation, compatibility checks, content manifests, and safe asset discovery.

### Avalonia client

Presentation, input, navigation, view models, localisation, accessibility, and desktop-platform integration.

## Current project references

```text
BallGM.Domain
BallGM.Application -> BallGM.Domain
BallGM.Rules -> BallGM.Domain
BallGM.Simulation -> BallGM.Domain, BallGM.Rules
BallGM.Infrastructure -> BallGM.Application, BallGM.Domain
BallGM.Mods -> BallGM.Application, BallGM.Domain
BallGM.Client.Avalonia -> BallGM.Application
BallGM.DataValidator -> BallGM.Mods
```

The Domain project must not reference the other production projects.
Only `BallGM.Client.Avalonia` may reference Avalonia packages.

## Cross-cutting design decisions

- Use identifiers rather than object graph persistence across every boundary.
- Separate runtime models from serialization DTOs.
- Use explicit result types for rule validation.
- Return machine-readable rule codes plus human-readable explanations.
- Store money in integer smallest units or a dedicated value object.
- Inject clocks and random sources.
- Record transactions as an auditable ledger.
- Make league rules data-driven, while keeping complex rule algorithms in trusted C#.
- Version both saves and external content schemas from the beginning.

## Milestone 0 proofs

Implemented before broad gameplay:

1. the pure .NET projects compile and test independently;
2. the Avalonia client shell calls one application query without referencing Domain directly;
3. a minimal versioned fictional league save envelope serializes and deserializes;
4. a seeded simulation smoke path produces a stable signature;
5. an invalid league-start operation returns structured rule explanations;
6. integration tests check that non-client projects do not reference Avalonia and Domain has no project references;
7. GitHub Actions restores, checks formatting, builds, and tests the solution on Windows, macOS, and Linux.
