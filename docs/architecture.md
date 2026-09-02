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

**A payroll is three buckets and one sum.** `TeamCapSheet` reports committed salary (live contracts), dead money (guaranteed money owed to a released player), and roster holds — and adds all three into `TotalPayroll`, which is what every threshold is measured against. A **roster-slot hold** is a placeholder charge for a spot the team has not filled: one per unfilled spot, priced at the compensation floor for a player with no service, produced by `BallGM.Rules.Cap.RosterSlotHoldProjection`. Without it a team eight players deep with room to spend appears able to put all of it on one player and discovers only afterwards that the roster still has to be filled — a trap the UI would set for a human and the AI front office would walk into.

That projection sits in Rules rather than beside `CapChargeProjection` in Domain, because its size and count come from the ruleset (the compensation floor and the roster minimum) and a projection that needs the ruleset is a rules service. It runs *behind* `ICapLedger`, inside the adapter, which is why the port takes a roster count: the alternative was for the Application query to project holds and pass them in, putting a rule in the layer that is supposed to have none. Two consequences worth stating rather than discovering: holds count towards the payroll floor as well as the ceilings, because a payroll that means something different depending on which line it is compared against is arithmetic nobody can explain; and a league with no compensation floor produces **no holds at all** rather than holds of nought, because there is no honest figure to reserve.

Explicitly deferred rather than half-built (see `CapLedger`'s own remarks): cap holds for a team's own expiring players, the size of the tax bill above the tax line, signing-bonus amortisation, and the transaction restrictions each apron implies. The ledger reports where a team stands; enforcing what a threshold forbids belongs to the trade and signing engines.

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

## The signing engine: routes, and what "no such rule" looks like

Milestone 6a splits a signing the same way Milestone 5 split a trade, and for the same reasons. `BallGM.Rules.Signings.SigningValidator` never mutates anything, so an offer screen can ask on every keystroke; `SigningExecutor` re-validates against the league as it stands, then applies the signing with an undo stack and writes the ledger line last. An entry recorded and then rolled back would describe something that did not happen.

`BallGM.Domain.Negotiations.Offer` is an immutable value object carrying `ContractSeasonTerm`s — the same type the resulting contract carries, so an offer cannot pass a shape check the contract it becomes would fail. Both go through the shared `ContractTerms.Normalize`. An offer is superseded, never amended.

**A signing is legal only if some route permits it, and every route reports.** `SigningRouteTable` evaluates four, in a fixed order that preserves the scarce thing — minimum salary consumes nothing, cap room is simply payroll, and the standard allowance is the only finite pot, so it is tried last:

| Route | Needs | Answers |
|---|---|---|
| Unrestricted signing | no soft cap configured | permits anything; roster space is the only constraint |
| Minimum salary | a compensation floor | always available regardless of payroll, at the player's service tier |
| Cap room | a soft cap | the gap below the line, counting back the hold this signing releases |
| Standard over-cap allowance | a configured allowance | what is left of a fixed sum, withdrawn above a named line |

Every deferred route in `docs/negotiation-mechanisms.md` — incumbent retention, post-room, periodic, injury replacement — is a variation on *eligibility*, so each arrives as another row here rather than a branch inside an existing one.

**Three states per route, not two.** A route can permit, refuse with the figure behind the refusal, or *not apply at all*. `Applicable` is separate from `Permits` because "this league has no such line" is an answer, and a screen that renders it identically to "you cannot afford it" teaches a GM the rules of a league they are not playing in. Inapplicable routes and skipped offer-legality checks travel in the assessment's `Notes` list — the same shape `TradeAssessment` already uses, and deliberately not a second way of saying a rule was skipped. `BallGM.Domain.Common.RuleFinding` is now shared by both engines rather than duplicated per engine, so the third list cannot be forgotten in one of them.

**Offer legality is separate from affordability.** `OfferLegality` checks the shape a league permits — term limit (longer for an incumbent, where a league says so), season-over-season escalation and de-escalation as a share of the *first* season (measuring against the previous season would let a long enough contract compound to any figure at all), a compensation ceiling as a share of the soft cap, and a compensation floor. Each is skippable by configuration and each reports its own skip.

**The hard cap is not a route, it is a ceiling on the result.** No route may leave a team above it, and a league without one has no such ceiling. The payroll floor is the opposite kind of line — being under it after a signing is a *warning*, because a team short of the minimum spend is a team with spending still to do, not a team barred from signing.

**Market resolution is chosen, not stumbled into.** `NegotiationRules.MarketResolution` is a ruleset field with two values. `ResolutionPoint` — the default — accumulates offers during a window and resolves the market at an explicit point, ordering offers within it by a stated deterministic key rather than by arrival. `Immediate` decides the instant an offer lands: defensible, simpler, and dependent on the order the UI happened to submit things in, which is the "the game cheated" complaint. Milestone 6a built no market, so nothing consumed the field; Milestone 6b consumes it, and the section below states the key.

**Absence is a default for a mode and a rule for a limit.** Every negotiation *limit* is optional by absence, meaning the league does not have it — a league configuring none of them is an open market where any team may pay anyone anything, which is exactly the uncapped conformance league, and emphatically not a league where nobody may sign. `MarketResolution` is not a limit: every league resolves offers somehow, so its absence is a documented default, the same way `DraftLotteryEnabled` and `SecondApronBlocksSalaryIncrease` already behave. That distinction is the one to reach for when the draft-slot scale and the tax brackets arrive.

`BallGM.Application.Negotiations.ISigningEngine` is the port and `BallGM.Infrastructure.Negotiations.RulesSigningEngine` the adapter, identical in shape to the cap, draft-asset, and trade pairs. `LeagueSession` gains `AssessOffer` and `SubmitOffer`; a signing is the one transaction so far that *creates* an aggregate rather than moving one, so the session replaces the snapshot it holds with one that includes the new contract.

**The roster minimum became an obligation rather than an invariant.** `Team.Create` no longer refuses a roster below the minimum, and `TradeValidator`/`Team.ApplyTrade` refuse only a trade that takes a team *further* below it. A squad three players short is the ordinary state of a team in the middle of free agency, and it is precisely the state a roster-slot hold prices — a league whose teams cannot be short of the minimum is a league where holds are unreachable code. The maximum stays a hard refusal, because it is a different kind of rule: a team over its limit is not a team with something left to do.

## The free-agency market: how a player chooses, and what may decide it

Milestone 6b is the other half of a signing: 6a answers "may this team sign this player", and this answers "given everyone who wants them, who gets them". Same division of labour as the trade and signing engines, and the same two types: `BallGM.Rules.Negotiations.FreeAgencyMarketResolver` never mutates anything, and `FreeAgencyMarketExecutor` re-validates against the league as it stands, then applies the outcome with a restore point.

**Every competing offer is re-run through `SigningValidator`.** The market owns no affordability rule of its own. An offer that has stopped being a legal signing since it was made — its team crossed an apron, filled its roster, spent its allowance elsewhere — is excluded on a rule code rather than shaded down on taste, and the two ways an offer can lose stay visibly apart on the assessment: one the *league* would not permit, and one the *player* would not accept.

### The preference model is four factors, and never a total

`BallGM.Domain.Negotiations.OfferPreference` carries one `PreferenceContribution` per factor — money, term, team fit, market demand — each with its own 0–100 reading, its own rule code, and its own sentence. There is deliberately no sum, no weight vector, and no overall score anywhere in the type.

The alternative was a weighted total that is always displayed decomposed, which is what most management games do. It was rejected for one reason: a total cannot answer "which factor beat me". A GM who outbid a rival by $2m and lost has to be told it was the three extra guaranteed seasons, and any presentation layered over `69.8 vs 71.2` is reconstructing that answer after the fact rather than reporting it.

**Ranking is therefore a comparison, not a sort key.** `PreferenceRanking.Compare` walks the factors in a fixed order — money, term, team fit, market demand — and stops at the first one where the two offers differ by more than that factor's **materiality band**. A factor inside its band has no opinion and hands over to the next: a player does not move towns over $200k, and a model that lets them is a model where money quietly decides everything. Money leads because money is what a GM is actually bidding with; a market where the biggest cheque routinely loses to a marginal fit reading is a market nobody can play.

Two consequences worth stating, because both shaped the code:

- **The comparison is not transitive**, and cannot be: A can sit inside B's band and B inside C's while A and C are apart. So the resolver *selects* repeatedly from a list already in the stated key order rather than handing the comparison to `Sort`, whose result would otherwise depend on the sort's internals.
- **Indifference is a definable state rather than an exact-tie coincidence.** That is what bounds the seeded draw: it fires only where no factor separates the leaders at all, and never as a tiebreak on a number.

### The ordering key, stated

Within a resolution point, offers are ordered by **team identifier, then offer identifier, both ordinal ascending**. Both are `SortableId`s, so the order is stable across runs, stable across platforms, and independent of the order a UI submitted anything in. `Negotiation.LiveOffersOn` returns them in that order, so nothing downstream has to remember to sort.

`Immediate` mode is the one place arrival order is read, because arrival order is the entire content of that mode: the first acceptable offer is taken and later ones are never weighed against it. It is reported as a note on the assessment, so a GM in such a league knows why their better offer was never considered.

### Where randomness is allowed, and where it is not

Every part of a resolution is arithmetic on the league except one: when the preference comparison reports it cannot separate the leading offers on any factor, one is drawn through `IRandomSource`. The draw runs over a list already in the stated key order, so the same league, the same offers and the same seed produce the same winner on every run. Below the top place the tie falls to the key rather than spending a draw on an ordering nobody acts on.

The assessment carries `TieBreakUsed`, and the board says so in words. "The draw landed that way" is a better answer to a GM than a reason invented after the fact.

### Time, before there is a calendar

Offer expiry is measured in `NegotiationRules.OfferExpiryDays`, so the market needs a notion of elapsed time — and the schedule does not arrive until Milestone 7. `BallGM.Domain.Negotiations.SeasonDay` is an index counted from the day the market opened, not a date: an offer that expired because a wall clock moved would make a save irreproducible, and re-opening a league next week must not quietly expire everything in it. When the calendar lands it maps its own dates onto this index and nothing that reads an expiry changes.

**Expiry is a query, not a stored flag.** `LiveOffersOn` answers what stands on a given day for a given league's rule; recording that an offer expired is a separate, explicit act performed by the executor. An assessment has to be able to ask what has timed out without that question being what times it out.

### An in-flight negotiation is session state, not league state

`Negotiation` is not on `LeagueSnapshot`. A negotiation is market state a session owns for as long as free agency is running, and putting it in the snapshot would give every read model in the game an opinion about it. `LeagueSession` holds them keyed by player, and the port takes the negotiation as an argument.

`BallGM.Application.Negotiations.IFreeAgencyMarket` is the port and `BallGM.Infrastructure.Negotiations.RulesFreeAgencyMarket` the adapter, identical in shape to the cap, draft-asset, trade, and signing pairs. `LeagueSession` gains `OpenNegotiation`, `PlaceOffer`, `Counteroffer`, `WithdrawOffer`, `AssessMarket`, `ResolveMarket`, `FreeAgencyBoard`, and `AdoptNegotiation` — the last being the load half of a save.

**Saves are versioned per concept.** `NegotiationEnvelope` carries its own `CurrentSchemaVersion`, independent of the ruleset's and of `LeagueSaveEnvelope`'s, because a negotiation and a ruleset change for different reasons and one version covering both would force a migration on everyone whenever either moved. Loading **replays the history through the aggregate's own methods** rather than assigning fields, so a save claiming a team withdrew an offer it never made is refused by the same rule that would have refused it live — and the state the file declares is checked against the state the replay reaches, so a file cannot assert an outcome its own history does not support. The serializer also sets `JsonUnmappedMemberHandling.Disallow`, so a file from a later build fails structurally instead of silently dropping half a market.

### Rolling back a market

`FreeAgencyMarketExecutor` mutates the negotiation first — expiries, then the outcome — and signs last. Everything before the signing is reversible through `Negotiation.RestoreTo`, which is a plain state restore rather than a rule-checked method for the same reason `Team.RestoreRoster` is: an undo that can be refused is not an undo. If the signing is refused, the history unwinds and the league was never touched.

### The board is columned by position, on purpose

`FreeAgencyBoardSummary` presents the market as one column per position, each carrying the team's own depth chart at that position alongside the best players available for it, plus whatever this team has on the table and whatever the player has countered with. A league-wide "best available" list answers who the best free agent is and nothing about whether this team needs one; a market a GM cannot read against their own squad is a market they cannot play.


## The season: a calendar, a schedule, a table, and a bracket

Milestone 7 gives the league a notion of *when*. Everything below it — a cap sheet, a trade, an offer — was previously expressed against a season year and, from 6b, a bare `SeasonDay` index. This is where those become a calendar a human can read and a schedule a league can play.

### `SeasonEngine` lives in `BallGM.Simulation`, not in `BallGM.Rules`

Every other rule-shaped thing in this codebase lives in `BallGM.Rules`, and the calendar builder, the schedule generator, the depth chart builder, the standings calculator and the postseason bracket builder all do. `BallGM.Simulation.Seasons.SeasonEngine` does not, for one reason: **it drives the match engine**, and `Rules` sits below `Simulation` in the dependency order. A `Rules` type that could play a game would need `Simulation` above it to reference downwards.

So the engine owns **sequencing and nothing else** — which day it is, which day comes next, what falls inside an advance, and what to hand to which rule. Every rule it applies belongs to `Rules` and is testable without it. That is the same division of labour `TradeExecutor` keeps with `TradeValidator`, applied to a loop instead of a transaction.

`BallGM.Simulation.Seasons.IMatchEngine` is the seam between the season's bookkeeping and the probabilistic model that decides who wins. Splitting them means the calendar can be advanced, tested, and proved deterministic without a single probability being involved. `UnplayedMatchEngine` is the implementation this build ships: it plays nothing and **says so** on the assessment rather than crashing, so a build with a calendar but no game model is a stated condition instead of a missing feature discovered at runtime.

### Assess, advance, restore — the same shape as every other engine

`SeasonEngine.Assess` works out what advancing *n* days would do and touches nothing, so an advance-date control can preview on every keystroke. `Advance` **re-assesses against the season as it stands** rather than trusting an assessment handed in, captures a restore point, walks the days one at a time, and puts the whole season back through `SeasonRun.RestoreTo` if any day fails.

The rollback is not defensive tidiness. A half-advanced season has games played on days the league has not reached, which is precisely the state `SeasonRun.RecordResult` refuses — so a partial advance would leave the season in a shape its own invariants say cannot exist.

`RestoreTo` takes no view on any rule, exactly as `Negotiation.RestoreTo` and `Team.RestoreRoster` do not: the state it restores was legal when it was left, and an undo that can be refused is not an undo. Time itself still only moves forwards — `AdvanceTo` refuses a day already passed, because a season that could rewind would replay games that already have results and double every record in the table.

### `SeedMixer`: every game's seed is derived, never drawn in sequence

`SeasonSeed` is the one number a season's randomness comes from, and **nothing draws from it directly**. Each consumer derives its own through `BallGM.Domain.Randomness.SeedMixer.Mix(seed, name)`: the fixture list from `"schedule"`, and each game from its own `GameId`.

The alternative — one long random stream consumed game by game — was rejected because it makes a result depend on *how much randomness was consumed before it*. Simulating game 400 would then differ depending on whether it was the four-hundredth game of an uninterrupted run or the first thing simulated after a load, so a save resumed mid-season and a season advanced a week at a time would both diverge from the same seed. Deriving each game's seed from the season seed and the game's identifier removes the dependency entirely.

This is also why `GameId` is **derived from the fixture's coordinates rather than minted with `SortableId.NewId()`** — the documented exception to the identifier rule in `docs/domain-language.md`. A minted identifier carries a timestamp and eighty bits of randomness, so two runs of one season would produce two different sets of game identifiers, and therefore two different sets of per-game seeds, and therefore different games. Every determinism guarantee in this milestone rests on that identifier being a function of the season, the day and the slot, and of nothing else.

`SeedMixer` is pure integer arithmetic over the UTF-8 bytes of the name, with a final avalanche so that names differing in one byte do not produce neighbouring seeds — adjacent games would otherwise have visibly correlated random sequences.

### The calendar maps onto the `SeasonDay` index; it does not replace it

`SeasonDay` arrived in 6b, before there was a calendar, as the unit offer expiry is measured in. `LeagueCalendar` maps that index onto dates rather than superseding it, and the direction matters:

- **Day 0 is the day the season opened, which is also the day the free-agency market opened.** That is what makes the mapping compatible with everything 6b already measures. The calendar arrived after the index and fits itself to the index.
- **Nothing in the rules or the simulation reads the date side.** `DateOn` and `DayOn` exist for presentation and for a screen that lets a GM pick a date. A season advanced by dates would reproduce differently depending on which day of the week it started, and an offer that expired because a wall clock moved is exactly the failure `SeasonDay` was introduced to refuse.
- **Phases are half-open ranges laid end to end**, validated at construction: a gap or an overlap is a structured failure at load rather than a crash on the first advance that reaches the hole. A phase configured as zero days long is left out entirely, so `Calendar.Has(SeasonPhase.Postseason)` is a straight answer to "does this league hold a postseason" rather than a length comparison every caller has to remember to make.

The consequence worth stating: the in-season signing window and the playoff eligibility cutoff are both stated in the ruleset as **day indices**, not dates, and are compared against the same index a negotiation's expiry uses. One notion of elapsed time across the whole game.

### A season in progress is session state, not league state

`SeasonRun` is not on `LeagueSnapshot` — the same decision 6b took for `Negotiation`, for the same reason. A schedule in the snapshot would give every read model in the game an opinion about what day it is, and the cap sheet has no business knowing. `LeagueSession` holds the run beside the negotiations, and `ISeasonEngine` takes it as an argument.

`SeasonEnvelope` carries **its own schema version**, independent of the ruleset's and of `LeagueSaveEnvelope`'s, and it already carries results, box scores and injury spells even though the build that introduced it plays no games — precisely so that the half of Milestone 7 which does play them adds no version at all. Loading replays every result through `RecordResult` and every advance through `AdvanceTo`, so a file claiming a game played on a day the league had not reached fails exactly the way it would have failed live.

### The postseason bracket is drawn a round at a time

`BallGM.Rules.Seasons.PostseasonBracketBuilder` seeds the bracket from `Standings` — league-wide in a flat league, per conference otherwise — and reports the fixtures each postseason day is due. It is a pure rule: no seed, no clock, no randomness. `SeasonEngine` asks it on every day it advances through and extends the schedule with whatever comes back.

**Seeding reads the table's order; it never re-decides it.** The league's stated tie-break sequence was already applied once by `StandingsCalculator`, and every tie that fell through to the terminal key is already reported there. A second ordering inside the bracket builder would be a second rulebook. Where the *last qualifying place* was taken on an equal record, the seeding says so in its own warning — that is the one place in a table where the order changes who is in and who is out.

**The bracket is drawn a round at a time and a game at a time, not laid out in full.** It has to be: round two's participants are unknown until round one is decided, and a best-of-seven that ends in five never played its last two games. A schedule holding fixtures that were never going to happen would make "games remaining" a lie and leave the season permanently short of complete.

Three mechanics follow from that:

- **Series in a round run in lockstep.** Game *n* of every live series in a round falls on the same day, so no team is asked to play twice in a day, and a series that ends early simply stops asking for days. The next round starts a full series length after the previous one began.
- **A series is identified by its unordered pair of teams.** Two teams meet at most once in a single-elimination bracket — a rematch would mean both had come through the same series — so the round is not needed to tell one series from another.
- **The higher seed is decided on league-wide standings position, not on conference seed number.** A final is contested between two teams who are both their conference's number one, and a conference seed cannot separate them. Inside a conference the two keys agree by construction, because both are read off the same ordered table.

Home advantage comes from the league's stated `HomeCourtPattern` — `2-2-1-1-1` written the way leagues write it — so a league that plays `2-3-2` is not a league that needs a code change.

A league whose `PostseasonRules` are `None` holds **no** postseason, which is a league rather than a misconfiguration: no postseason phase is built, the season ends when the regular season does, and the absence is a note on every assessment rather than a silence.

### The season boundary

Milestone 7c-a is what happens when a season ends: `BallGM.Rules.Seasons.SeasonConclusion` turns a finished `SeasonRun` into the offseason, and `LeagueSession.ConcludeSeason()` sequences it.

**Validated, then applied — no rollback machinery.** Every other multi-step engine in this codebase (`TradeExecutor`, `SigningExecutor`, `SeasonEngine.Advance`) re-validates immediately before it mutates, because time can pass between an assessment a screen showed and the submission that follows it — a GM previews a trade, thinks about it, and the league may have moved by the time they confirm. Concluding a season has no such gap: it is one call, with nothing in between that could invalidate what was just checked. `SeasonConclusion.Conclude` therefore validates everything read-only first — the season has reached its last day, and the league has no history entry for this year yet — and only after both hold does it touch anything. Every mutation that follows is against state the method itself just proved was safe to mutate a moment earlier, so none of them can fail, and there is nothing to capture a restore point for. This is a narrower reading of "roll back on any failure" than the assess/execute split the trade and signing engines use, and it is narrower on purpose: building generic capture/restore machinery here would be machinery nothing can ever trigger.

**The champion is re-derived, never stored.** Nothing on `SeasonRun` keeps the postseason bracket's winner once the bracket stops needing new fixtures — `SeasonEngine.Advance` reads `PostseasonDraw.ChampionId` only to decide whether to keep drawing, then discards it. `PostseasonBracketBuilder.DrawFor` is pure and entirely re-derivable from the finished season's own table and results, so `SeasonConclusion` asks it once more against the final day rather than plumbing new state through the engine that plays the season. A league with no postseason configured records no champion — `null`, not the regular-season table leader — the same way an unconfigured cap threshold is absent rather than zero.

**A contract's natural expiry is a season-boundary event, not a release.** `Contract.IsTerminated` is set only by `Contract.Terminate`, which is what a voluntary release does; a contract that simply ran out its last season was never terminated and needs no new state of its own. What was actually missing was two things downstream of that: `Team.RemovePlayer` refuses to take a roster below its configured minimum, which is correct for a voluntary release and wrong here — a short roster between seasons is the ordinary state free agency exists to fill, per "Team aggregate, on the roster minimum" above — so `Team.ReleaseExpiredPlayer` shares its missing-player check but skips the floor. And the free-agent predicate in `GetLeagueOverviewQuery`/`LeagueSession.Negotiations.IsFreeAgent` was checking `!contract.IsTerminated` alone, with no season scoping at all, so a contract that had simply run its course held its player out of free agency forever; both now also require `contract.TermFor(currentSeason) is not null`.

**Service time is earned by roster presence, not by contract status.** `Player.CompleteSeasonOfService` is credited to everyone a `Team.PlayerIds` named at the moment a season concludes — including a player whose contract expires that same season, since they were rostered through it. `SeasonHistoryEntry`/`SeasonHistoryTeamRecord` (`BallGM.Domain.Seasons`) are deliberately small: who won and where everyone finished, not a stats table — season and career statistics are Milestone 8's.

### The save game

Milestone 7c-b is what makes a played league survive closing the client. Before it, `Contract`, `DraftAssetBook`, `Negotiation`, `SeasonRun`, and `LeagueRuleset` each already round-tripped on their own; nothing composed them, and `League`, `Franchise`, `Team`, and `Player` had never been serialized at all. `BallGM.Infrastructure.Saves.SaveGameEnvelope` (schema version 1) is the composition, and `SaveGameSerializer` — behind a new `BallGM.Application.Saves.ISaveGameStore` port, the same shape `ICapLedger`/`ITradeEngine`/`Seasons.ISeasonEngine` already use — is what `LeagueSession.Save()`/`LoadSave(...)` reach it through.

**Composition by embedding already-serialized text, not by nesting typed envelopes.** The obvious way to compose five existing envelope types into one save is to give `SaveGameEnvelope` a property of each type — `ContractEnvelope`, `DraftAssetBookEnvelope`, and so on. That was rejected: it would make `SaveGameEnvelope` reference every one of their shapes directly, so a field added to `ContractEnvelope` would be a field this type's C# shape has to know about too, even though nothing here reads it. Instead `SaveGameEnvelope` carries what `ContractSerializer`, `DraftAssetSerializer`, `SeasonSerializer`, and `NegotiationSerializer` already produce and read — plain strings, one already-versioned JSON document each — and never references their DTO types. This is a stricter reading of "each concept keeps its own version" than nesting would give: nesting still couples the outer shape to the inner one, and a string does not.

**The ruleset in effect at save time is embedded in full, not referenced by name or path.** A save is meant to be self-contained and reproducible from the file alone, even if the shipped ruleset file changes after the save was made — the same reasoning that makes `SeasonEnvelope` carry the season's own seed rather than depend on anything computed at load time. Reaching a `LeagueRuleset` to hand to `LeagueRulesetSerializer.Serialize` needed a `LeagueConfiguration → LeagueRuleset` mapping that already existed as `RulesSeasonEngine`'s own private `BuildRuleset`, and the reverse direction already existed inline inside `FixtureLeagueDataSource.BuildLeague`. Both were extracted into `BallGM.Infrastructure.Rulesets.LeagueConfigurationMapper` (`ToRuleset`/`ToConfiguration`) once a second caller needed them, rather than duplicated a third time for the save.

**`League`, `Franchise`, `Team`, and `Player` share the save's own schema version rather than carrying one each.** Unlike the five concepts above, none of them is ever read or written except as part of a whole save — nothing versions them independently today, so a version number that could move independently would be a version number nothing would ever move independently. `TransactionLedger` is the exception among the new types worth naming on its own: everywhere else a ledger is built, every entry is minted fresh through `Record`/`RecordSigning`/`RecordPickEvent`, with a new identifier, sequence number, and timestamp from an injected clock. A save needs the opposite — entries with their original identity intact — so `TransactionLedger.Rehydrate` is the one new aggregate-level entry point this milestone added, and it refuses a file whose sequence is not exactly `0..N-1` in order, the same way a save asserting an impossible history is refused everywhere else in this codebase.

**Loading replays every concept through its own aggregate factory or serializer**, precisely as `SeasonSerializer` and `NegotiationSerializer` already did before this milestone: `League.RecordSeason` for its history, `Team.Create`/`Player.Create` for the roster, `TransactionLedger.Rehydrate` for the ledger. A save claiming a sequence that could not have happened — two history entries for the same season year, a ledger with a gap in its sequence — fails exactly the way it would have failed live.

**Determinism is proved from one `Load()`, not two.** `FixtureLeagueDataSource` mints every identifier fresh with `SortableId.NewId()` on each call, so two separate loads are two different leagues that merely share a name — `PlayedSeasonTests.DifferentSeedsProduceDifferentSeasons` already documents this. `SaveGameDeterminismTests` loads the fixture exactly once, immediately saves that pre-season snapshot to get identifier-stable JSON, and reloads that one save into every session the test plays a season on: one played straight through, one saved halfway and resumed. Both reach the same champion and the same score in every game of the season.

### The match engine plays possessions, not scores

`BallGM.Simulation.Seasons.PossessionMatchEngine` is Milestone 7b: the model that decides who wins. It sits behind `IMatchEngine`, which is the seam 7a was built against, so nothing above it changed shape when it arrived.

**Possessions rather than a score draw.** The cheap alternative is to draw a total for each side from a distribution around their strengths. It produces plausible final scores and is a third of the code — but there is nothing underneath the total to attribute to anybody, so the box score has to be invented afterwards and reconciled back to a number that was decided without it. Simulating possessions means the box score *is* the game: the totals are a sum rather than a target, `BoxScore.Create`'s "the lines must add up to the result" check can never fail by construction, and pace becomes a real property one league can differ from another on.

**Every term is bounded and named in `MatchModelBounds`.** This is the answer to `docs/competitive-feature-review.md` §7, which records a competitor shipping an outcome probability that one input could dominate without a cap. Strength, home advantage, fatigue and usage each have a stated ceiling, and `MatchModelBoundsTests` asserts the relationships rather than just the values: no term may approach the base efficiency, home advantage must be worth less than being the better team, rest must be worth less than talent, and the strength cap must be *reachable* by a real mismatch — a bound nothing can hit is dead code, not a bound.

**Integer arithmetic throughout.** Efficiencies are points per ten thousand possessions and probabilities are basis points, so nothing about a result depends on floating-point rounding and the same seed plays the same game on every platform. This is the same reasoning behind integer money and `TeamRecord`'s cross-multiplication, and it is not decorative here: the whole season's reproducibility rests on it.

**Strength is relative, never absolute.** Only the *difference* between the two sides enters the efficiency, so a league rated 45 against 45 and one rated 90 against 90 are the same contest and produce the same scorelines. A model keyed on absolute rating would send one league to nothing and the other off the top of the scoreboard — and a modder shipping a data pack on their own rating scale would find the sport had stopped working.

**Fatigue arrives as rest days, computed by the sequencing layer.** `MatchTeam.RestDays` is days since that team's previous game, worked out by `SeasonEngine` from the schedule. The model does not know what a calendar is and should not have to.

**Injuries come back beside the result rather than being applied.** `MatchOutcome` carries `MatchInjury`, which counts a knock in *days*; `SeasonEngine` turns each one into an `InjurySpell` against the day the game was played on. Same division of labour as everywhere else — the model decides what happened, the sequencing layer decides when. The spell starts the day *after* the game, because the player finished the one they were hurt in; that is why their minutes are in its box score.

`MatchSetup` carries `MatchTeam` — the rotation, the ratings behind it, and the rest — rather than a bare `DepthChart`, because a depth chart deliberately does not carry a rating and strength cannot be recovered from its minutes: those have already been clamped into the bounds a rotation runs inside, so a team of journeymen and a team of stars both allocate the same 240 minutes.

### The model is calibrated against the sport, and the calibration is a test

`MatchModelCalibrationTests` asserts what the model produces *on average* — team scores averaging 107 with a realistic spread, a mean margin near 14, home teams winning about 55%, an 85-rated side beating a 55-rated one about 87% of the time, a rested side gaining a few points of win probability against one on a back-to-back, and injuries running about 0.13 a game skewed short.

Every run is seeded, so none of it is flaky: a failure means the model moved, never that the dice did. The bands are deliberately wide — this is a fictional league and the point is to stay inside the range a reader of the sport would recognise, not to reproduce a real one to the decimal. A tight band would fail on every legitimate tuning pass and teach whoever hit it to widen the band rather than to think.

**Two known limitations, both traceable to a single-attribute rating.** A team's leading scorer averages about 23 rather than the 26 or so a real league produces, because usage is inferred from overall rating and minutes — there is no attribute that says how much of the offence runs through a player. Modelling that harder off `Overall` would make a great defensive centre his team's leading scorer, which is worse than the error it fixes. The same cause makes minutes flatter than a real rotation's. Both want a multi-attribute `PlayerRating`, which `PlayerRating` itself already anticipates.


### The playoff eligibility cutoff is applied at the signing

`PostseasonRules.PlayoffEligibilityCutoffDay` is the last season day a player may be added and still appear in the postseason. It is enforced in `SigningValidator`, with the day travelling in from `LeagueSession`'s season through `ISigningEngine` onto `SigningContext`.

It is a **warning, not a violation**. A league with a cutoff decides who may appear in the postseason, not who may be signed: a team signing cover for the last fortnight of a regular season is doing something legal and deliberate. What it must never be is silent, because a GM who signs on the wrong side of the cutoff has bought someone who cannot play in the games the signing was probably for.

The three ways the check cannot fire — the league holds no postseason, the league states no cutoff, no season is under way in this session — are each reported as their own note. A check that never ran is otherwise indistinguishable from a check that ran and approved, which is the contract every other assessment in this codebase keeps.

**Known gap.** The cutoff currently bites at the point of signing and is reported there; it does not yet keep an ineligible player out of a postseason *line-up*. Doing that needs the signing day to survive a save, which means either a field on `TransactionEntry` or a `SeasonEnvelope` version — neither of which belongs in the change that drew the bracket.


## Cross-cutting design decisions

- Use identifiers rather than object graph persistence across every boundary.
- Separate runtime models from serialization DTOs.
- Use explicit result types for rule validation.
- Return machine-readable rule codes plus human-readable explanations.
- Store money in integer smallest units or a dedicated value object.
- Inject clocks and random sources. `IRandomSource` and its deterministic `SeededRandomSource` both live in `BallGM.Domain.Randomness`: everything that composes a seeded run has to be able to construct one, including the Infrastructure composition root, which does not reference the simulation project and should not have to.
- Record transactions as an auditable ledger.
- Make league rules data-driven, while keeping complex rule algorithms in trusted C#.
- Version both saves and external content schemas from the beginning.

## Moddable rules, by design

The concrete reason "make league rules data-driven" is a top-level design decision rather than an aspiration: a licensed sports game's balance rules are baked into the shipped code, so a rule the community considers broken stays broken until the next annual release. `LeagueRuleset` (`BallGM.Rules.Configuration`) plus `LeagueRulesetSerializer` (`BallGM.Infrastructure.Rulesets`) exist so a cap or draft rule change is a new ruleset file, not a code change or a new build.

The financial thresholds (`CapThresholds`) are named generically — `PayrollFloor`, `SoftCap`, `LuxuryTax`, `FirstApron`, `SecondApron`, `HardCap` — rather than after any one real-world league's current agreement, matching the `Threshold` term already defined in `docs/domain-language.md`. What each threshold actually restricts during a trade or free-agent signing is trade-engine logic (Milestone 5); these types carry the configured amounts and guarantee `PayrollFloor ≤ SoftCap ≤ LuxuryTax ≤ FirstApron ≤ SecondApron ≤ HardCap`. Because a ruleset file is untrusted input the moment it's editable outside the build, `LeagueRulesetSerializer.Deserialize` never throws on a malformed or self-contradictory file — it returns a structured `DomainOperationResult<LeagueRuleset>` failure, the same explainable-failure mechanism the rest of the domain uses.

### Optional by absence (schema version 4)

Loading changed shape in version 4, and the shape is the point: **a rule the file does not mention is a rule the league does not have.** Every threshold is `Money?`, a draft round count of zero means the league holds no draft, and an absent salary-match percentage means the league does not match salary. No new JSON keys were added — absence itself carries the meaning — and the ordering guarantee above applies to the thresholds that are *present*, in that one fixed sequence.

Three consequences worth stating, because they are what make this different from defaulting:

- **Nullability stops at the boundary in one direction only.** `LeagueRulesetEnvelope` is nullable because JSON is; `LeagueRuleset` does not grow a nullable field to match. The optionality lives in `CapThresholds`, `DraftRules.HasDraft`, and `TradeRules.HasSalaryMatching` — rules concepts — and travels through `LeagueConfiguration` to the read model as `long?`, so the client can render "this league has no cap" rather than "0".
- **A skipped rule is reported, never inferred.** `TradeValidator` returns a third list, `Notes`, naming each money rule it did not run and why (`trade.salary_matching_skipped_no_soft_cap`, `trade.hard_cap_check_skipped_no_hard_cap`, `trade.apron_restriction_skipped_no_apron`). A check that silently passes because a value was null is indistinguishable from a check that ran and approved; that ambiguity is the class of bug the whole scheme exists to remove.
- **Version 3 is refused, not migrated.** The two versions differ only in which fields may be absent, so a valid version 3 file is a valid version 4 file with the number changed, and the error message says exactly that. The version still had to move: a version 3 reader handed a version 4 file would read every absent field as zero and run a cap system the ruleset never stated.

## Draft classes, scouting, and the lottery

Milestone 8 opens with the first three items on its list: procedurally generated draft classes, the scouting model that obscures a prospect's true rating, and the weighted draft lottery that decides round one. Ruleset schema version 7. Development, ageing, retirement, records/history, and awards — the rest of Milestone 8 — are not yet built; this section covers only what shipped.

**`Prospect` and `DraftClass` are new Domain types, not a repurposed `Player`.** A prospect has no roster, no contract, and no seasons of service — modelling one as a `Player` with those fields left empty would make "is this a real player" a null check scattered across every caller instead of a type. `DraftClass` owns its `Prospect`s directly (an entity collection, not identifiers into a sibling aggregate) because a prospect has nothing else to belong to until draft day selects it — unlike `Team`, which references `Player` by `PlayerId` because a released player outlives the roster that stopped naming them.

**`Prospect.TrueRating` is honest; what a scout knows about it is a separate value object, not a fudge on the same field.** `ScoutingRange` (`BallGM.Domain.Draft`) carries a lower bound, an upper bound, and a confidence 0-100, and collapses onto the true value at 100 confidence via `ScoutingRange.Certain`. `BallGM.Rules.Draft.ScoutingModel.Assess` is the only thing that turns a true rating into one, given `ScoutingRules` and a scouting-investment figure — it never receives or returns the true rating alongside the range, so a caller cannot leak it by accident.

**Scouting investment is a function, not a tracked economy.** This milestone builds the mechanism — investment points in, a narrower range out, via `ScoutingRules.InvestmentConfidence` (a `BandedScale`, the same primitive the compensation tables use) — but not a persisted per-franchise scouting budget or ledger. A team's investment figure is supplied by the caller each time `ScoutingModel.Assess` runs. Wiring an actual spendable budget into `LeagueSession` is deferred rather than half-built here.

**The lottery draws the whole configured pool by weight, not just a leading slice of it.** A real league's lottery typically weights only its worst few teams and lets the rest fall in reverse-standings order. `BallGM.Rules.Draft.DraftLottery` and `DraftLotteryRules.Weights` (worst-team-first) instead draw every team the weight list names, one slot at a time without replacement — simpler (one ruleset field instead of two), and it still rewards a worse finish with better average odds. A league that wants a real top-4-style lottery just states four weights; every team outside that count was never in `Weights` and already picks in plain reverse-standings order, because `DraftLottery` appends anything beyond the weighted pool unchanged. Only round one is drawn — every later round runs in the same worst-to-best order the standings state, since a franchise's *original* draft position does not vary round to round before any pick has changed hands, and `PickConveyanceEvaluator` already owns what happens to a pick after that. `DraftLottery.Run` is the producer `DraftOrderSnapshot` was documented as still needing back at Milestone 4 — until now every order fed to pick conveyance came from a fixture or a test.

**`DraftClassRules`, `ScoutingRules`, and `DraftLotteryRules` are three independently optional ruleset sections, not one bundled "draft class" switch.** A league can run a weighted lottery over a draft class a data pack supplies without configuring the procedural generator at all, or generate its own classes with no scouting uncertainty modelled. Each has its own `None`, and each is optional by absence in the schema-version-4 sense — not a default standing in for a rule, but the rule itself. The one cross-check enforced (in `LeagueConfigurationMapper.ToRuleset` and again in `LeagueRulesetSerializer.Deserialize`, matching every other cross-field check in this file) is that `DraftRules.LotteryEnabled` and `DraftLotteryRules.IsConfigured` agree: a league cannot enable the lottery and state no odds, and cannot state odds without enabling it. Both are the same class of contradiction `DraftRules.Create` already refuses for restrictions stated without a draft.

**Ruleset schema version 7.** Adds the draft-class generator (class size, true-rating spread, prospect age), the scouting model (base confidence, zero-confidence range width, the investment-to-confidence table), and the lottery's weighting table — all optional by absence exactly as version 4 established. A version 6 reader handed a version 7 file would ignore a stated rating spread or lottery weighting and either generate nothing or draw uniformly in a league that described something more specific, which is the class of silent gap every version bump here exists to close.

**Names are shipped fictional content, not a rule.** `BallGM.Rules.Draft.ProspectNameBank` is a small built-in pool the generator draws from. The generator's *algorithm* is what this milestone makes configurable; the specific fictional names a build ships are exactly the kind of thing a data pack replaces once the mod platform (Milestone 10) exists.

**Not yet wired to a session.** `ProspectGenerator`, `ScoutingModel`, and `DraftLottery` are callable and tested in isolation but nothing in `LeagueSession` or the client calls them yet — there is no draft-day flow that turns a drafted `Prospect` into a `Player`, and no UI. That is the rest of Milestone 8's remaining scope (development, ageing, retirement, records/history, awards) plus the UI items the roadmap names, not a gap in this slice.

## Development, ageing, and retirement

The second slice of Milestone 8: a player's rating moving with age, and a player's career ending by age. Ruleset schema version 8.

**One curve moves `PlayerRating.Overall`; there is no per-attribute breakdown yet.** `docs/roadmap.md` and this milestone's own goal asked for per-attribute development curves using the multi-attribute rating `PlayerRating` already anticipates. That expansion did not happen here — it is a public-API change consumed by the match engine and its locked calibration tests (`MatchModelCalibrationTests`), squarely inside `CLAUDE.md`'s change-control list, and building it speculatively ahead of a caller that needs it would be exactly the premature abstraction the same file warns against. `BallGM.Rules.Development.PlayerDevelopmentModel` and `DevelopmentRules` are written so that expansion is additive when it comes: the curve is already keyed by age against a `PlayerRating`, not inlined into `Player` or the season engine.

**Growth and decline are two separate `BandedScale` tables either side of a flat peak range, not one signed curve.** `BandedScale` enforces non-negative values (it is shared with the compensation tables, where a negative entry would be nonsensical), so a single table cannot express "grows before, shrinks after" with one sign convention. `DevelopmentRules.GrowthCurve` and `DeclineCurve` are each a magnitude, and `PlayerDevelopmentModel.Develop` decides which one applies and with which sign purely from where the supplied age falls relative to `PeakAgeStart`/`PeakAgeEnd` — inside that range, neither curve applies at all, which is the plateau every development curve needs so growth does not run straight into decline with no rest between them.

**Variance is a seeded draw added on top of the curve, not baked into it.** `DevelopmentRules.VarianceRange` is a symmetric spread (`[-VarianceRange, VarianceRange]`) drawn through the caller's `IRandomSource` each time `Develop` runs, so two players the same age do not move in lockstep — and a variance range of zero draws nothing at all, verified by a test that hands the model a random source which throws if asked for anything, the same technique `DraftLotteryTests` uses to prove no-lottery draws no randomness.

**Retirement reports a `RuleFinding` on every path, including the two that never draw.** `BallGM.Rules.Development.RetirementModel.Assess` always returns one: not configured, below the minimum voluntary age, drawn (with the odds it drew against, stated in the sentence), or a certain mandatory-age retirement — never a bare bool. `RetirementRules.MandatoryRetirementAge`, where stated, is a hard cutoff rather than another entry at the top of the odds table, because a league that means to end every career by a stated age should not have that age occasionally miss on an unlucky-for-the-league roll.

**`Player` gained `Develop`, `Retire`, and a `Biography`, all additive.** `Player.Rating` moved from a get-only property assigned once at construction to a private-set property `Develop(PlayerRating)` is the only way to change — the same "the aggregate applies what the rules layer decided" split `CapChargeProjection` and `PickConveyanceEvaluator` already keep with their callers. `Retire()` only flips `IsRetired`; a retired player is not removed from the league, because their record and career history need to stay exactly as reachable as an active player's. `PlayerBiography` (birthplace, prior programme, draft season/round/selection, all nullable) is the field set `docs/competitive-feature-review.md` §2 names as the seed for relationship affinity — the fields only, not the graph, which is Milestone 13's. `Player.Create` takes both as optional trailing parameters defaulting to `PlayerBiography.Unknown` and not retired, so no existing caller needed to change.

**Not yet wired to a season boundary.** `PlayerDevelopmentModel` and `RetirementModel` are callable and tested in isolation, exactly like the draft-class slice before them, but nothing calls them from `SeasonConclusion` or `LeagueSession` yet. Wiring ageing into the season boundary needs `DevelopmentRules`/`RetirementRules` threaded through `ISeasonEngine`/`SeasonContext` the way the playoff eligibility cutoff was threaded onto `SigningContext` — real plumbing, deliberately left for when the rest of Milestone 8 (records/history, awards) is ready to land alongside it rather than done twice.

## Two assumptions Milestone 13 will break

`docs/competitive-feature-review.md` defines Milestone 13 (league life and locker room). It is recorded here, well ahead of the work, because every item in it invalidates one of two assumptions the current codebase quietly relies on — and knowing which assumptions those are should shape what gets built between now and then.

1. **The ruleset is loaded once and is then fixed.** `LeagueRulesetSerializer.Deserialize` runs at load, and every rules service takes a `LeagueRuleset` as an immutable dependency. In-save rule changes make the ruleset a **versioned timeline** — "the rules as of season N" — which means a rules service can no longer capture one instance for the lifetime of a session, and a historical transaction must be explainable against the rules that were in force when it happened, not today's. The cheap thing to do now is to keep passing the ruleset in per operation rather than caching it in constructors.
2. **League membership is fixed at creation.** Expansion and relocation make membership an event. `League` already references teams by `TeamId` rather than embedding them, and `Franchise` is already separate from `Team`, so the aggregate shape survives; what does not survive is any code that treats the team set as a constant, or any read model that caches a franchise's name or city.

Both are save-schema and cross-project public-API changes, which `CLAUDE.md` puts under change control. Neither is to be started incrementally as a side effect of a smaller task — but neither should be made *more* expensive by new code that assumes the opposite.

The same document's content-neutrality position (`docs/vision.md` → Moddability) has one direct architectural consequence: a league the format cannot express is a code change, and a code change is a failure of the moddability pillar. The uncapped-league and no-draft gaps recorded in `docs/negotiation-mechanisms.md` → "Ruleset genericity" are the first two measured instances of that; both are closed at schema version 4, and `tests/BallGM.Integration.Tests/RulesetConformanceTests.cs` is where the measurement lives — it now asserts the fixed behaviour rather than pinning the broken behaviour.

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
