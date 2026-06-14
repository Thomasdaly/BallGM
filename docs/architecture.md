# Architecture direction

This is a starting architecture decision, not permission to overbuild every layer immediately.

## Recommended solution structure

```text
BasketballFrontOfficeSim.sln
src/
  BasketballSim.Domain/
  BasketballSim.Application/
  BasketballSim.Rules/
  BasketballSim.Simulation/
  BasketballSim.Infrastructure/
  BasketballSim.Mods/
  BasketballSim.Client.Godot/
tests/
  BasketballSim.Domain.Tests/
  BasketballSim.Rules.Tests/
  BasketballSim.Simulation.Tests/
  BasketballSim.Integration.Tests/
tools/
  BasketballSim.DataValidator/
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

### Godot client

Presentation, input, navigation, view models/presenters, localisation, accessibility, and desktop-platform integration.

## Dependency direction

```text
Godot Client -> Application -> Domain
Infrastructure -> Application/Domain interfaces
Rules -> Domain
Simulation -> Domain and Rules abstractions
Mods -> schema/validation and approved application interfaces
```

The Domain project must not reference the other production projects.

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

## First architectural proof

Before implementing broad gameplay, prove that:

1. the pure .NET projects compile and test independently;
2. the Godot client can call one application query without domain leakage;
3. a tiny fictional league can serialize and deserialize;
4. a seeded simulation produces the same result twice;
5. an invalid transaction returns structured explanations.
