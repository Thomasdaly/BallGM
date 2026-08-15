# Domain language

Use these terms consistently unless an architecture decision changes them.

- **League**: Competition containing franchises, rules, schedule, seasons, and history.
- **Franchise**: Persistent organisation identity across seasons.
- **Team**: Competitive squad for a specific league context.
- **Season**: One year of league operation, identified by year.
- **Player**: Basketball participant with identity, skills, contract status, health, and career history.
- **Contract**: Agreement containing terms, compensation, options, guarantees, and clauses. Modelled as the `Contract` aggregate (`BallGM.Domain.Contracts`): parties, a contiguous season range, and one `ContractSeasonTerm` per season carrying compensation, the guaranteed part of it, and any team or player option. A contract states what is *owed*; it does not state what counts against a threshold.
- **Cap charge**: Amount applied to a team calculation for a season — `CapCharge` (`BallGM.Domain.Cap`), a value projected from contracts by `CapChargeProjection`, never stored and mutated. Two kinds exist: an active-contract charge, and dead money. **Dead money** is a charge whose contract has been terminated — guaranteed money owed to a player who is no longer on the roster. It is modelled that way from the start rather than as a later special case, which is why cap charge is a concept of its own and not a field on `Contract`.
- **Option**: A season one party may decline. An undecided (pending) option season produces no cap charge; exercising it makes the season a normal charge, and declining it ends the contract there without creating dead money.
- **Threshold**: Configurable financial boundary such as cap, tax, first apron, or second apron. Modelled as `CapThresholds` (`SoftCap`, `LuxuryTax`, `FirstApron`, `SecondApron`, `HardCap`) — named generically rather than after any one real-world league's current agreement, so the values are configuration, not a fact baked into the code.
- **Draft pick**: Asset identified by draft, round, and original franchise — `DraftPick` (`BallGM.Domain.DraftAssets`), and immutable in all four. Identity never changes hands; ownership does.
- **Pick ownership**: Current legal control of a draft pick asset, plus whatever is riding on it — `PickOwnership`, deliberately a separate type from `DraftPick`. One record per pick, held in a `DraftAssetBook`, so two franchises cannot both hold the same asset.
- **Encumbrance**: Something unresolved attached to a pick's ownership: a **pick obligation** (a promise to convey it, subject to a protection) or a **swap right**. A pick carries at most one of each; an obligation and a swap right may coexist, which is what makes their resolution order a decision.
- **Protection**: Condition determining whether a pick conveys — `PickProtection`, an explicit vocabulary rather than a string. Either unprotected, or protected through the top N selections with a rollover schedule (one level per successive draft) terminating in a stated **fallback**: conveys unprotected, converts to a later round, or extinguishes.
- **Conveyance**: What an encumbrance did when its draft came around — conveyed, rolled over, converted, extinguished, or (for a swap) exercised or declined. Decided against a supplied **draft order snapshot**, never a generated one, and returned as a rule code plus a human sentence.
- **Swap right**: Conditional right to exchange draft positions. Held by one franchise over another's pick, naming the selection it would give up in exchange. Resolves *before* protections — see `docs/architecture.md` for why that ordering is a rule.
- **Draft order snapshot**: Where every franchise's selection lands in one draft, supplied from outside the rules layer. The lottery (Milestone 8) will produce one; until then a fixture or a test writes one directly, so conveyance is testable without a simulation.
- **Transaction**: Auditable state change involving players, contracts, picks, money, or roster status. Recorded as a `TransactionEntry` in the append-only `TransactionLedger` (`BallGM.Domain.Transactions`), stamped from an injected `IClock` and ordered by an explicit sequence rather than by timestamp or identifier. Payroll never changes silently, and neither does pick ownership: every cap-affecting and every draft-asset event leaves a line with a reason on it, in the same ledger.
- **Trade proposal**: Proposed multi-team exchange before validation and execution.
- **Rule violation**: Structured reason a proposed operation is illegal.
- **Ruleset**: Versioned configuration plus trusted algorithms defining league operation. Concretely: `LeagueRuleset` (`BallGM.Rules.Configuration`), loaded from a file via `LeagueRulesetSerializer` (`BallGM.Infrastructure.Rulesets`) — see `docs/architecture.md` → "Moddable rules, by design" for why this is a file, not a build-time constant.
- **Simulation seed**: Input used to reproduce stochastic outcomes.
- **Data pack**: Validated declarative content used to create or modify a league.

## Aggregate boundaries

- **League aggregate**: Owns league identity and league-level team membership. It references teams by stable identifiers rather than embedding team roster state.
- **Team aggregate**: Owns roster membership for one competitive squad. It enforces duplicate-membership and configured roster-size invariants, and references players by stable identifiers.
- **Franchise aggregate**: Owns persistent organisation identity across seasons. Referenced by `Team` via `FranchiseId` rather than embedded.
- **Player aggregate**: Owns one participant's identity, position, rating, and current injury status. Referenced by `Team` via `PlayerId` rather than embedded, and by `Contract` the same way — a player does not own their contract, and a released player keeps existing after the roster stops referencing them. Career-history data stays out of scope until Milestone 8; `Player` is modelled directly rather than through a separate `Person` base type, since no other `Person` subtype (coach, front-office staff) exists yet.
- **Draft-pick aggregate**: Split in two on purpose. `DraftPick` owns identity and cannot change; `PickOwnership` owns current control and encumbrances and is the only thing that can. Both are reached through `DraftAssetBook`, which owns registration and lookup by draft coordinates — the lookup a rollover uses to find the pick an obligation moves to. Ownership changes and conveyance outcomes are recorded in the shared `TransactionLedger`, never applied silently.
- **Contract aggregate**: Owns the terms agreed between one team and one player: the season range, per-season compensation and guarantees, option kind and status, and whether the deal has been terminated. Options are decided and releases are recorded through its own methods (`ExerciseOption`, `DeclineOption`, `Terminate`), never by setting a field — including when loading a save, so a file claiming an impossible release fails the same way a live release would.

## Aggregate creation

Aggregates expose a static `Create(...)` factory returning `DomainOperationResult<T>` rather than a public throwing constructor. Structural argument problems (a null required reference) still throw `ArgumentException`/`ArgumentNullException` — those indicate a programming error at the call site. Business-rule violations (roster size out of range, duplicate membership) return a structured failure instead, because data-pack and mod content is untrusted input and must be able to fail validation without crashing the loader. New aggregates should follow the same split.

## Identifier format

Entity identifiers (`TeamId`, `PlayerId`, `FranchiseId`, `LeagueId`, and future ones) are plain validated string wrappers with no fixed shape enforced by the type itself, but new identifiers should be minted with `BallGM.Domain.Common.SortableId.NewId()`. It produces a 26-character, Crockford base32, ULID-shaped identifier — a 48-bit millisecond timestamp followed by 80 bits of randomness. This was decided at Milestone 1, before any save format locks in a shape: identifiers sort in creation order without a coordinating sequence, which matters for the auditable transaction ledger and for data-pack/mod tooling that mints identifiers offline. Retrofitting an identifier format after saves exist in the wild is a migration problem, not a refactor — pin it now.
