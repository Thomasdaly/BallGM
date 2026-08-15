# Development roadmap

Each milestone should finish with a buildable repository, passing tests, a reviewed diff, and a Git checkpoint.

## Sequencing principle

A thin, ugly Avalonia UI slice ships at Milestone 2 — right after the first real domain model exists — and every later milestone extends that same running client instead of building UI for the first time at the end. Rules and simulation work are only validated by tests until a human plays the decision they produce; pushing all UI to the final milestone would mean the core "is this fun to decide" question goes untested until the project is nearly finished. Each milestone below lists its UI deliverable explicitly so that discipline stays visible.

## Milestone 0 — Repository and architecture proof

- Scaffold the solution.
- Add CI.
- Add formatting and test conventions.
- Add a minimal Avalonia client shell.
- Demonstrate dependency boundaries.
- Add one deterministic simulation smoke test.
- Add one save round-trip smoke test.

## Milestone 1 — League and roster foundation

- League, season, franchise, team, person, player, roster, position, rating, injury
- Stable identifiers
- Basic roster invariants
- Fictional fixture data

UI: none yet — this milestone establishes the domain model the Milestone 2 screens read from.

## Milestone 2 — Thin playable UI slice

Pulled forward from the end of the roadmap on purpose. The goal is a human making a real roster decision against real domain data as early as possible, not a polished screen.

- Roster grid backed by a real `BallGM.Application` query against Milestone 1 data.
- Placeholder cap sheet and trade-proposal form — wired to stub/mock data if the cap ledger and trade engine (Milestones 3 and 5) don't exist yet.
- Bare navigation shell only; no theming, accessibility, or localization work yet.
- Deliberately reviewed as a playtest, not a build: does managing a fictional roster in this shape feel like the intended game loop?

UI: first playable slice — roster grid, stub cap sheet, stub trade form.

## Milestone 3 — Contracts and cap ledger

- Contract terms
- Salary by season
- guarantees/options
- cap charges
- team cap sheet
- configurable thresholds
- transaction ledger

The cap thresholds and roster limits this milestone enforces at the transaction level are already configuration, not code — `LeagueRuleset.CapThresholds` and `LeagueRuleset.RosterLimits` (Milestone 1). This milestone wires the trade/signing engine to *read* those values and enforce them; it does not reinvent them.

UI: wire the Milestone 2 cap sheet screen to real cap-ledger data, replacing the stub.

## Milestone 4 — Draft assets

- Picks by league/season/round/original team
- Current owner
- protections
- conveyance
- rollover
- swaps
- ownership validation
- asset history

Pick identity and pick ownership are separate types from the first commit, and protections are a value-object vocabulary rather than a string — see `docs/architecture.md` → "Draft assets: identity and ownership are separate types" for the reasoning, the deferred protection forms, and the swaps-before-protections resolution order. Draft order is injected rather than generated: the lottery is Milestone 8.

UI: add a pick-ownership board (who owns which future picks, protections visible).

## Milestone 5 — Trade engine

- Two-team trades
- Multi-team trades
- player and pick movement
- salary matching
- roster constraints
- cap/apron restrictions
- injured-player eligibility
- atomic execution
- explainable failures

Assessment and execution are separate operations, and only execution touches the league — see `docs/architecture.md` → "The trade engine: assessment and execution are different operations" for the split, the undo-stack atomicity, the ledger-length staleness token, and what is deferred. This milestone also introduces `LeagueSession`, because a trade is the first thing in the game that changes the league a screen is looking at.

UI: wire the Milestone 2 trade-proposal form to real validation and execution, surfacing structured rule-violation explanations directly in the UI.

## Milestone 6 — Contract negotiation and free agency

- Player preferences
- team fit
- market demand
- offers and counteroffers
- options and guarantees
- seeded stochastic choices
- explainable outcomes

UI: free-agency board and an offer/counteroffer screen.

## Milestone 7 — Calendar and game simulation

- Schedule
- team strength
- line-ups/minutes
- game outcomes
- box-score statistics
- fatigue and injuries
- standings
- postseason

UI: calendar/advance-date controls, box scores, and a standings view.

## Milestone 8 — Player lifecycle

- draft classes
- scouting uncertainty
- development
- ageing
- retirement
- records and history

UI: draft-class scouting view and a draft-day screen.

## Milestone 9 — AI front offices

- organisational direction
- asset valuation
- roster needs
- trade targeting
- free-agent targeting
- draft decisions
- explainable decisions and diagnostics

UI: an inbox/news feed surfacing AI-driven trades and signings with their explanations, plus a diagnostics view for the explainability data already flowing through `DomainOperationResult`/rule-violation types since Milestone 0.

## Milestone 10 — Mod platform

- versioned schemas
- manifests
- validation CLI
- safe asset loading
- sample data pack
- compatibility errors
- documentation

UI: a mod-manager panel (load, validate, enable/disable data packs; surface validation errors from `BallGM.DataValidator`).

## Milestone 11 — UI vertical-slice hardening

By this point every screen exists in rough form from Milestones 2–10. This milestone polishes the client that's already been played, rather than building it for the first time.

- dashboards
- full navigation
- keyboard navigation
- scalable desktop layout
- visual and interaction polish across all existing screens

## Milestone 12 — Production readiness

- save migrations
- performance profiling
- crash diagnostics
- localisation readiness
- accessibility
- packaging for three desktop OS targets
- Steam adapter
- achievements/cloud-save decisions
- release pipelines
