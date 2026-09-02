# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

BallGM: a fictional, moddable, cross-platform basketball front-office and league simulation game, aimed at a future Steam release. No 3D match engine — the game is management depth, rules accuracy, long-term simulation quality, and explainable AI decisions. Think Football Manager, not NBA 2K.

Full context lives in `docs/`:
- `docs/vision.md` — pillars and non-goals
- `docs/product-scope.md` — MVP system list, vertical-slice target
- `docs/architecture.md` — solution structure and dependency rules
- `docs/domain-language.md` — canonical terminology and aggregate boundaries
- `docs/roadmap.md` — milestone sequence (M0–M13); a thin Avalonia UI slice now lands at Milestone 2, not at the end
- `docs/negotiation-mechanisms.md` — the signing-mechanism inventory, with each one marked built / deferred to a named milestone / declined. Read before adding anything to contract or free-agency rules; "we decided not to" is recorded there.
- `docs/competitive-feature-review.md` — the same treatment applied to a competitor's published franchise-mode feature set: relationships/morale/trust, in-save rule changes, expansion and relocation, cash in trades, draft-class playlists, and what we declined and why. It also states the content-neutrality position (engine neutral, our shipped content fictional, format must be expressive enough for community packs) and defines Milestone 13. Read before proposing a "what if the game also had…" feature; the answer is probably already recorded.

Read the relevant doc before touching a system you haven't worked in yet. Don't re-derive decisions already recorded there.

## Current state (check before assuming)

As of this writing: Milestones 0 (repo/architecture proof), 1 (league and roster foundation), 2 (thin playable UI slice), 3 (contracts and cap ledger), 4 (draft assets), 5 (trade engine), 6a (contract offers and signing routes), 6b (the free-agency market), 7a (the season calendar, schedule, standings, depth charts and postseason) and 7b (the match engine) are done. Milestone 8 — player lifecycle — is in progress: its first slice (draft classes, scouting uncertainty, and the draft lottery) is done; development, ageing, retirement, records/history, and awards are not yet built. The one piece of 7 still outstanding is **UI box scores**: `LeagueSession.BoxScore`/`BoxScoresOn` and `BoxScoreSummary` exist and are tested, but no client view reads them yet.

What Milestone 8's first slice added: `Prospect`/`DraftClass`/`ScoutingRange` in `BallGM.Domain.Draft`; `DraftClassRules`/`ScoutingRules`/`DraftLotteryRules` in `BallGM.Rules.Configuration`; `ProspectGenerator`/`ScoutingModel`/`DraftLottery`/`ProspectNameBank` in `BallGM.Rules.Draft`. Ruleset schema version 7. `DraftLottery.Run` is the `DraftOrderSnapshot` producer that section of `docs/domain-language.md` named as owed since Milestone 4. None of it is wired into `LeagueSession` or the client yet — see `docs/architecture.md` → "Draft classes, scouting, and the lottery" for what shipped, what's deliberately deferred (a tracked scouting-investment economy, a draft-day flow that turns a `Prospect` into a `Player`), and the decisions worth not re-deriving.

What 7b added: `PossessionMatchEngine` and `MatchModelBounds` in `BallGM.Simulation.Seasons`, plus `MatchTeam`/`MatchOutcome`/`MatchInjury` on the `IMatchEngine` contract. `RulesSeasonEngine` now defaults to the real engine; `UnplayedMatchEngine` is still there and still the right thing to hand in when a test wants to inject its own results.

Four decisions from 7b worth not re-deriving: **the game is simulated possession by possession rather than drawn as a score**, because a drawn total has nothing underneath it to attribute, so the box score would have to be invented afterwards and reconciled to a number decided without it; **every term is bounded and named in `MatchModelBounds`**, which is the answer to `docs/competitive-feature-review.md` §7, and `MatchModelBoundsTests` asserts the relationships rather than the values — including that the strength cap is *reachable*, since a bound nothing can hit is dead code; **all arithmetic is integer** (efficiencies in points per ten thousand possessions, probabilities in basis points) so no result depends on floating-point rounding; and **strength enters only as the difference between the two sides**, so a data pack on its own rating scale still produces basketball rather than 0-0 or 300-300. All four are in `docs/architecture.md` → "The match engine plays possessions, not scores".

Calibration is locked by seeded regression tests in `MatchModelCalibrationTests`, so a tuning change that breaks the sport fails a test rather than quietly shipping. Two known limitations are recorded there and traceable to the same cause — `PlayerRating` carries a single `Overall`, so leading scorers and rotation minutes are both flatter than a real league's. Both want the multi-attribute rating `PlayerRating` already anticipates; do not try to fix them by pushing harder on `Overall`, which would make a great defensive centre his team's leading scorer.

One thing to know before writing a cross-load determinism test: **`FixtureLeagueDataSource` mints its identifiers with `SortableId.NewId()` on every load**, and the schedule generator orders teams by identifier before it shuffles, so two loads are two different leagues that merely share a name. Exact replay is provable with a fixed league (the simulation suite does it), not across two `Load()` calls.

What 7a added: `LeagueCalendar`/`CalendarPhase`/`SeasonPhase`, `SeasonSchedule`/`Fixture`/`GameId`, `SeasonRun` (a season in progress, with `Capture`/`RestoreTo`), `SeasonSeed`, `Standings`/`StandingsRow`/`TeamRecord`/`StandingsTieBreak`/`TieBreakSequence`, `DepthChart`, `BoxScore`, `InjurySpell`, `LeagueAlignment` (conferences and divisions, on the `League` aggregate because alignment is league *content*, not a rule) and `SeedMixer` in Domain; `ScheduleRules`/`StandingsRules`/`PostseasonRules`/`HomeCourtPattern`, `SeasonCalendarBuilder`, `ScheduleGenerator`, `StandingsCalculator`/`StandingsComparer`, `DepthChartBuilder`/`MinutesAllocationBounds` and `PostseasonBracketBuilder` in `BallGM.Rules.Seasons`; `SeasonEngine`/`SeasonContext`/`IMatchEngine` in `BallGM.Simulation.Seasons`; the `ISeasonEngine` port and `RulesSeasonEngine` adapter; `SeasonEnvelope`/`SeasonSerializer` at its own schema version 1; the season half of `LeagueSession` in `LeagueSession.Seasons.cs`; and the season view in the client. The ruleset file moved to **schema version 6**.

Five decisions from 7a worth not re-deriving: **`SeasonEngine` lives in `BallGM.Simulation`, not `BallGM.Rules`**, because it drives the match engine and Rules sits below Simulation — so it owns sequencing and nothing else, and every rule it applies is a Rules type testable without it; **every game's seed is derived through `SeedMixer.Mix(seasonSeed, gameId)` rather than drawn from one running stream**, so game 400 is the same game whether it is the four-hundredth of an uninterrupted run or the first thing simulated after a load — which is also why `GameId` is derived from the fixture's coordinates instead of minted with `SortableId.NewId()`; **`Advance` re-assesses, captures a restore point, and puts the whole season back through `RestoreTo` if any day fails**, because a half-advanced season has games played on days the league had not reached, the exact state `RecordResult` exists to refuse; **`LeagueCalendar` maps onto the `SeasonDay` index rather than replacing it** — day 0 is the day the season and the free-agency market both opened, and nothing in the rules or the simulation ever reads the date side; and **the postseason bracket is drawn a round at a time and a game at a time**, because round two's participants are unknown until round one is decided and a best-of-seven that ends in five never played its last two games. All five are recorded in `docs/architecture.md` → "The season: a calendar, a schedule, a table, and a bracket".

Also from 7a, worth knowing before touching signings: **`PostseasonRules.PlayoffEligibilityCutoffDay` is applied in `SigningValidator`**, with the day threaded in from `LeagueSession`'s season through `ISigningEngine` onto `SigningContext`. It is a warning rather than a violation — a cutoff decides who may appear in the postseason, not who may be signed — and the three ways it cannot fire (no postseason, no stated cutoff, no season under way) are each their own note. It does not yet keep an ineligible player out of a postseason line-up; that needs the signing day to survive a save.

What 6b added: the `Negotiation` aggregate (Domain) with its ordered history of offers, counteroffers, withdrawals and expiries, and four states (`Open`/`Resolved`/`Signed`/`Closed`); `SeasonDay`, the index offer expiry is measured in until a calendar exists; `OfferPreference`/`PreferenceContribution`/`PreferenceRanking`, four factors that are **never summed**; `PreferenceModel`, `MarketContext`, `FreeAgencyMarketResolver` and `FreeAgencyMarketExecutor` in `BallGM.Rules.Negotiations`; the `IFreeAgencyMarket` port and `RulesFreeAgencyMarket` adapter; `NegotiationEnvelope`/`NegotiationSerializer` at its own schema version 1; the market half of `LeagueSession` in `LeagueSession.Negotiations.cs`; and the positional free-agency board in the client.

Three decisions from 6b worth not re-deriving: **a counteroffer is a new `Offer` in the history authored by the player**, not a state transition, so the market stays open and a team answers by offering again; **the deterministic ordering key is `(TeamId, OfferId)` ordinal ascending**, and the preference comparison is deliberately not a sort comparator because a materiality band cannot be transitive; and **an in-flight negotiation is session state rather than league state**, so it is not on `LeagueSnapshot` and it carries a save schema version independent of the ruleset's. All three are recorded in `docs/architecture.md` → "The free-agency market" and `docs/negotiation-mechanisms.md`.

What 6a added: `Offer` (Domain, immutable, carrying the same `ContractSeasonTerm`s a contract carries — both validated by the shared `ContractTerms.Normalize`), `OfferLegality`/`SigningRouteTable`/`SigningValidator`/`SigningExecutor` in `BallGM.Rules.Signings`, the `ISigningEngine` port and `RulesSigningEngine` adapter, `NegotiationRules` in the ruleset, and `RosterSlotHoldProjection` in `BallGM.Rules.Cap` — which is what finally *creates* the `CapCharge.RosterSlotHold` shape that has existed unused since schema v4. A signing is legal only if some route permits it; four routes exist (unrestricted, minimum salary, cap room, standard over-cap allowance) and each reports permitted / refused-with-the-figure / **not a rule in this league**, that third state travelling in the assessment's `Notes` list, the same shape `TradeAssessment` already uses. `RuleFinding` moved to `BallGM.Domain.Common` and is now shared by both engines rather than duplicated.

Three decisions from 6a worth not re-deriving: the ruleset file is at **schema version 5** (the negotiation section is optional-by-absence like everything else, but the version still moved, because a v4 reader handed a v5 file would silently drop rules the file states — the serializer now also refuses unknown fields); **`marketResolution` is a mode, not a limit**, so its absence is a documented default rather than "no such rule"; and the **roster minimum is an obligation, not an invariant** — `Team.Create` accepts a short roster, because a league whose teams cannot be short of the minimum is a league where roster-slot holds are unreachable code. All three are recorded in `docs/negotiation-mechanisms.md`.

Earlier state, still true — the cap sheet is real, backed by `Contract`, `CapCharge`/`CapChargeProjection`, `TransactionLedger` (Domain), `CapLedger` (Rules), and the `ICapLedger` port/`RulesCapLedger` adapter pair (whose input now carries a roster count, so holds are projected behind the port rather than in a read model). The pick board is real too, backed by `DraftPick`/`PickOwnership`/`DraftAssetBook`, `PickProtection`, `DraftOrderSnapshot` (Domain), `PickConveyanceEvaluator`/`PickOwnershipRules`/`DraftAssetLedger` (Rules), and the `IDraftAssetLedger` port/`RulesDraftAssetLedger` adapter — same shape as the cap ledger, deliberately. The trade engine is `TradeProposal`/`TradeAssetMovement`/`LeagueStateToken` (Domain), `TradeValidator`/`TradeExecutor`/`TradeRules` (Rules), the `ITradeEngine` port/`RulesTradeEngine` adapter, and `LeagueSession` — the first thing in the codebase that holds a league in memory across commands. Assessment never mutates; execution re-validates and rolls back on any failure. `BallGM.Domain` has `League` and `Team` aggregates (created via non-throwing `Create(...)` factories returning `DomainOperationResult<T>`, not public constructors), `FranchiseId`, `PlayerId`, `LeagueId`, `TeamId`, `RosterSizeLimits`, the shared `DomainOperationResult`/`DomainOperationResult<T>`/`DomainError`/`RuleFinding` kernel, `BandedScale` (the tier-table primitive the draft-slot scale and tax brackets will reuse), `IRandomSource`/`SeededRandomSource`, and `SortableId`. This list goes stale fast — `git log` and the actual `src/` tree are the source of truth, not this file.

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
