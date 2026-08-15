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

Read models returned from here own their own ordering. Aggregates hold membership as sets and identifiers are minted per load, so any list handed to the UI must be sorted on a stable domain value — a name, a rating, a season — never on identifier order, or the client reshuffles between launches.

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
BallGM.Infrastructure -> BallGM.Application, BallGM.Domain, BallGM.Rules
BallGM.Mods -> BallGM.Application, BallGM.Domain
BallGM.Client.Avalonia -> BallGM.Application, BallGM.Infrastructure
BallGM.DataValidator -> BallGM.Mods
```

The Domain project must not reference the other production projects.
Only `BallGM.Client.Avalonia` may reference Avalonia packages.

`BallGM.Client.Avalonia` references `BallGM.Infrastructure` because it is the composition root: something has to choose the concrete `ILeagueDataSource` the Application query reads through, and the client process is the only place that decision can be made. The reference is fenced rather than open-ended — `LeagueClientComposition` is the single file allowed to name an Infrastructure type, and `ArchitectureBoundaryTests.AvaloniaViewsAndViewModelsDoNotReachIntoInfrastructure` fails the build if anything under `Views/` or `ViewModels/` mentions `BallGM.Infrastructure`. No cycle is created and no Avalonia dependency reaches the simulation core; the rule that matters — the UI depends on Application read models, never on aggregate internals or on a file format — is unchanged.

## The cap ledger crosses a boundary, on purpose

Cap rules read configuration that lives in `BallGM.Rules` (`CapThresholds`), but the screen that shows a cap sheet talks to `BallGM.Application`, which does not reference Rules. That is resolved with a port and an adapter rather than by relaxing the dependency rule:

- `BallGM.Domain.Contracts.Contract` states what is owed; `BallGM.Domain.Cap.CapCharge` states what counts against a threshold in a season, and `CapChargeProjection` is the only thing that turns one into the other. Dead money is a charge whose contract is terminated, not a special field.
- `BallGM.Rules.Cap.CapLedger` totals those charges and compares the total to the configured `CapThresholds`, returning a `TeamCapSheet` with a rule code *and* a human explanation per threshold.
- `BallGM.Application.Cap.ICapLedger` is the port the Application query calls. Thresholds travel in per call from the already-loaded `LeagueConfiguration`, so there is one ruleset load path and no second copy of the amounts.
- `BallGM.Infrastructure.Cap.RulesCapLedger` is the adapter: it maps `LeagueConfiguration` back onto `CapThresholds` and delegates. Infrastructure already referenced Rules for ruleset persistence, so no new dependency appears and no cycle is created.

Explicitly deferred rather than half-built (see `CapLedger`'s own remarks): signing exceptions of any kind, minimum-salary and rookie-scale rules, cap holds for unsigned players, the size of the tax bill above the tax line, and the transaction restrictions each apron implies. The ledger reports where a team stands; enforcing what a threshold forbids is the trade engine's job at Milestone 5.

`BallGM.Domain.Transactions.TransactionLedger` is append-only — `Entries` is exposed as a read-only view and there is no update or delete — because a payroll figure that changed without a ledger line behind it is a bug, not a shortcut. Draft-asset events (Milestone 4) are recorded in that same ledger through `RecordPickEvent`, as new `TransactionKind` members rather than a second ledger: an asset trail kept apart from the money trail is two accounts of the same trade that can disagree. A ledger entry names a team (cap events, whose subject is a season's squad) or a franchise (draft assets, which outlive one), and naming neither throws.

## Draft assets: identity and ownership are separate types

A draft pick has two halves, and Milestone 4 keeps them apart from the first commit:

- `BallGM.Domain.DraftAssets.DraftPick` is identity — league, draft season, round, original franchise — and is immutable. None of those can change: a pick traded twice still originally belonged to the same franchise.
- `PickOwnership` is the mutable half: the current owner plus the encumbrances riding on the asset. `DraftAssetBook` holds exactly one ownership record per registered pick, so duplicate current ownership is impossible by construction rather than by convention — there is no collection of owners for two entries to appear in.

Merging the two is how a pick system ends up unable to answer "whose pick was this originally", and that is the question every protection is written against: a top-4 protection means the top 4 of the *original* franchise's selection, no matter who holds the asset today.

**Protection is a vocabulary, not a string.** `PickProtection` carries a schedule of top-N levels — one per successive draft — terminating in a stated `PickProtectionFallback`: conveys unprotected, converts to a later round, or extinguishes. A schedule with no terminal outcome would roll forever, which is why the fallback is required rather than optional. Deliberately deferred and named rather than half-built: range protections, record- or outcome-conditional protections, cash considerations, lottery odds, and multi-team pick routing. Each changes what a protection is evaluated *against*, not merely its numbers.

**Draft order is injected, never generated here.** `DraftOrderSnapshot` is supplied to the evaluator by a fixture, a test, or (from Milestone 8) the lottery. If the only way to obtain an order were to run a lottery, every protection test would become a seeded-simulation test.

**Resolution order is a rule, so it is a decision.** `BallGM.Rules.DraftAssets.PickConveyanceEvaluator` settles **swap rights before protections**. A swap changes *which selection a pick is*; a protection asks *where this pick landed*. Testing the protection first would judge it against a selection number the asset no longer occupies, letting a franchise sell a top-4-protected pick, swap into a better selection, and keep the pick on a protection that no longer describes reality. Real sims differ on this ordering, which is exactly why it is written down here and asserted in `PickConveyanceEvaluatorTests`.

**Ownership validation is the surface the trade engine will call.** `PickOwnershipRules` answers whether a transfer or an encumbrance is legal — control, the configured tradable horizon, conflicting obligations, and the consecutive-future-draft retention restriction — as rule codes plus sentences. A pick carrying a pending obligation does not count as retained: a rule satisfied by an asset the franchise may lose is not a retention rule. Milestone 5 calls this the way it calls `ICapLedger`; this milestone builds the surface, not the execution.

The board reaches the rules through the same port/adapter pair the cap sheet uses: `BallGM.Application.DraftAssets.IDraftAssetLedger` is the port, `BallGM.Rules.DraftAssets.DraftAssetLedger` builds the `DraftAssetBoard`, and `BallGM.Infrastructure.DraftAssets.RulesDraftAssetLedger` maps the loaded `LeagueConfiguration` back onto `DraftRules`. The protection wording lives in the rules layer, not the client, for the same reason the threshold explanations do: two screens inventing their own wording is two chances to word it wrongly.

The retention restriction itself is configuration, generically named, in `DraftRules` alongside the rest of the draft structure — `RetainedRoundNumber`, `RetainedRoundInterval`, and `TradableFutureDraftHorizon`. No real-world rule name, no compiled-in horizon. Adding them made the ruleset file schema version 2, and `LeagueRulesetSerializer` now rejects a version it cannot read rather than defaulting the missing fields: a league quietly running restrictions its ruleset never stated is worse than one that refuses to load.

`BallGM.Infrastructure` references `BallGM.Rules` because loading and saving the league ruleset file (`BallGM.Infrastructure.Rulesets`) is persistence — Infrastructure's job — but the type being persisted (`LeagueRuleset`) is defined in Rules, matching this project's stated responsibility for "configurable rulesets for roster limits, contracts, cap thresholds ... draft operation." No cycle is created: Rules still has no knowledge of Infrastructure.

## The trade engine: assessment and execution are different operations

Milestone 5 splits a trade in two, because the two halves have opposite requirements.

- `BallGM.Rules.Trades.TradeValidator` never mutates anything. A trade machine is nothing but speculative runs — a GM reworks a proposal a dozen times before submitting it — so assessment projects the result instead of applying it: charges are rebuilt against the team each contract *would* belong to and handed to the same `CapLedger` the cap sheet uses. It returns blocking violations, non-blocking warnings, and the resulting payroll, roster, and pick count for every team, so a rejection can be negotiated against rather than merely read.
- `BallGM.Rules.Trades.TradeExecutor` re-validates against the league as it stands — never against an assessment handed in from outside — and then applies the trade with an undo stack. If any step fails, the stack unwinds and the league is exactly where it started. A half-applied trade would leave a player on two rosters or a pick owned by nobody, and no ledger line could explain it.

It owns no rule that already exists elsewhere: pick movement goes through `PickOwnershipRules` and threshold standing through `CapLedger`, so a trade cannot legalise something the pick board or the cap sheet would call illegal.

**Two aggregate operations exist purely because a trade cannot be expressed without them.** `Team.ApplyTrade(outgoing, incoming)` judges where a roster ends up rather than each step along the way — a legal one-for-one by a team on the roster minimum fails halfway through a remove-then-add, and a team on the maximum fails the other ordering; the transient state is an artefact of the steps, not a rule anybody wrote. `Contract.TransferTo(teamId)` moves the salary with the player, because a traded player whose contract stayed behind leaves both cap sheets wrong.

**Staleness is detected with the ledger, not a hash of the world.** A `TradeProposal` records `LeagueStateToken` — the ledger's length when it was assembled. Every state change worth knowing about leaves a ledger entry, so a token that no longer matches means the proposal was built against a league that has since moved. This is also what stops a double submission from executing a trade twice: the trade's own ledger lines invalidate its proposal.

**`BallGM.Application.Leagues.LeagueSession` holds the loaded league for the length of a run.** Before this milestone every screen could reload from its data source on demand, because nothing changed. A trade changes it, and reloading after an execution would discard the very change the screen exists to show. The session owns loading, re-projection, and trade submission, and it is where advancing the calendar will go. Saving is still out of scope — closing the client discards the run.

`BallGM.Application.Trades.ITradeEngine` is the port; `BallGM.Infrastructure.Trades.RulesTradeEngine` is the adapter that maps the loaded `LeagueConfiguration` back onto `TradeRules`, `CapThresholds`, and `DraftRules`. Identical in shape to the cap and draft-asset pairs, for the same reason.

Deliberately deferred rather than half-built, and named in `TradeRules`: trade and traded-player exceptions, sign-and-trade, cash considerations, aggregation windows and waiting periods after a signing, and no-trade clauses. Each needs state this build does not keep yet. What *is* configured, generically named, in the ruleset file: `SalaryMatchPercent`, `SalaryMatchAllowance`, `InjuredPlayerEligibility` (allowed, allowed-with-warning, or blocked), and `SecondApronBlocksSalaryIncrease`. Those additions took the ruleset file to schema version 3.

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

## Moddable rules, by design

The concrete reason "make league rules data-driven" is a top-level design decision rather than an aspiration: a licensed sports game's balance rules are baked into the shipped code, so a rule the community considers broken stays broken until the next annual release. `LeagueRuleset` (`BallGM.Rules.Configuration`) plus `LeagueRulesetSerializer` (`BallGM.Infrastructure.Rulesets`) exist so a cap or draft rule change is a new ruleset file, not a code change or a new build.

The financial thresholds (`CapThresholds`) are named generically — `SoftCap`, `LuxuryTax`, `FirstApron`, `SecondApron`, `HardCap` — rather than after any one real-world league's current agreement, matching the `Threshold` term already defined in `docs/domain-language.md`. What each threshold actually restricts during a trade or free-agent signing is trade-engine logic (Milestone 5); today these types only carry the configured amounts and guarantee `SoftCap ≤ LuxuryTax ≤ FirstApron ≤ SecondApron ≤ HardCap`. Because a ruleset file is untrusted input the moment it's editable outside the build, `LeagueRulesetSerializer.Deserialize` never throws on a malformed or self-contradictory file — it returns a structured `DomainOperationResult<LeagueRuleset>` failure, the same explainable-failure mechanism the rest of the domain uses.

## Mod and data-pack trust model

`AGENTS.md`/`CLAUDE.md` both commit to treating imported mod/data-pack content as untrusted input. This is the concrete plan that commitment resolves to, so the mod format doesn't need a breaking change once the mod platform (Milestone 10) is built out:

1. **Never executed.** Data packs are declarative JSON only. No mod ever contains or references executable code, and the loader never evaluates mod content as code.
2. **Schema-validated on load, in effect now.** `DataPackManifest.SchemaVersion` is checked against `CurrentSchemaVersion` before content is trusted structurally. `BallGM.DataValidator` exists specifically to run this check outside the game process. This is sufficient while data packs are developer-authored fixtures (Milestone 0–8).
3. **Content-integrity verification, required before Milestone 10 ships.** Once mods are loaded from outside the repository (community-authored packs), schema validity alone doesn't establish the pack is untampered. Before the mod platform milestone ships, `DataPackManifest` gains a content hash (or signature) field, and the loader rejects a pack whose declared hash doesn't match its contents. This is a breaking manifest-schema change, which is exactly why it's called out here rather than left implicit — bump `DataPackManifest.CurrentSchemaVersion` when it lands, and do it deliberately rather than as an afterthought.
4. **Aggregate factories already fail closed on bad content.** Because `Team.Create`/`League.Create` return `DomainOperationResult<T>` instead of throwing for business-rule violations (see `docs/domain-language.md`), a malformed data pack produces a structured, explainable load error rather than crashing the loader — this is the mechanism integrity and schema checks report through.

## Milestone 0 proofs

Implemented before broad gameplay:

1. the pure .NET projects compile and test independently;
2. the Avalonia client shell calls one application query without referencing Domain directly;
3. a minimal versioned fictional league save envelope serializes and deserializes;
4. a seeded simulation smoke path produces a stable signature;
5. an invalid league-start operation returns structured rule explanations;
6. integration tests check that non-client projects do not reference Avalonia and Domain has no project references;
7. GitHub Actions restores, checks formatting, builds, and tests the solution on Windows, macOS, and Linux.
