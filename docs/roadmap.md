# Development roadmap

Each milestone should finish with a buildable repository, passing tests, a reviewed diff, and a Git checkpoint.

## Milestone 0 — Repository and architecture proof

- Scaffold the solution.
- Add CI.
- Add formatting and test conventions.
- Add a minimal Godot client shell.
- Demonstrate dependency boundaries.
- Add one deterministic simulation smoke test.
- Add one save round-trip smoke test.

## Milestone 1 — League and roster foundation

- League, season, franchise, team, person, player, roster, position, rating, injury
- Stable identifiers
- Basic roster invariants
- Fictional fixture data

## Milestone 2 — Contracts and cap ledger

- Contract terms
- Salary by season
- guarantees/options
- cap charges
- team cap sheet
- configurable thresholds
- transaction ledger

## Milestone 3 — Draft assets

- Picks by league/season/round/original team
- Current owner
- protections
- conveyance
- rollover
- swaps
- ownership validation
- asset history

## Milestone 4 — Trade engine

- Two-team trades
- Multi-team trades
- player and pick movement
- salary matching
- roster constraints
- cap/apron restrictions
- injured-player eligibility
- atomic execution
- explainable failures

## Milestone 5 — Contract negotiation and free agency

- Player preferences
- team fit
- market demand
- offers and counteroffers
- options and guarantees
- seeded stochastic choices
- explainable outcomes

## Milestone 6 — Calendar and game simulation

- Schedule
- team strength
- line-ups/minutes
- game outcomes
- box-score statistics
- fatigue and injuries
- standings
- postseason

## Milestone 7 — Player lifecycle

- draft classes
- scouting uncertainty
- development
- ageing
- retirement
- records and history

## Milestone 8 — AI front offices

- organisational direction
- asset valuation
- roster needs
- trade targeting
- free-agent targeting
- draft decisions
- explainable decisions and diagnostics

## Milestone 9 — Mod platform

- versioned schemas
- manifests
- validation CLI
- safe asset loading
- sample data pack
- compatibility errors
- documentation

## Milestone 10 — Godot product vertical slice

- dashboards
- roster and cap screens
- trade centre
- free agency
- draft
- calendar
- inbox/news
- history
- scalable desktop UI
- keyboard navigation

## Milestone 11 — Production readiness

- save migrations
- performance profiling
- crash diagnostics
- localisation readiness
- accessibility
- packaging for three desktop OS targets
- Steam adapter
- achievements/cloud-save decisions
- release pipelines
