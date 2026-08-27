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

Ships in two halves with a checkpoint between them, because offer legality and the market have very
different risk profiles. **6a** — offer legality, the signing routes, roster-slot holds, and an offer
screen — is bounded and mechanical against machinery that already exists three times over. **6b** —
the market — holds both genuinely open design questions (how a preference decomposes, how
simultaneous offers are ordered) and all the new UI. Merged, the half most likely to balloon has
nothing to balloon against.

**6a (done):**

- Offer legality: term, escalation, compensation ceiling and floor, all keyed on seasons of service
- Four signing routes, each reporting permitted / refused-by-this-much / not-a-rule-here
- Roster-slot holds, so the room a cap sheet reports is room a team can spend
- Atomic, auditable acceptance in the trade executor's undo-stack shape

**6b (next):**

- Player preferences, decomposed per factor and never a single score
- team fit
- market demand
- offers and counteroffers
- seeded stochastic choices
- explainable outcomes

Scope for this milestone is fixed by `docs/negotiation-mechanisms.md`, which inventories every signing mechanism and marks each one built, deferred to a named milestone, or declined. Three signing routes ship here — cap room, minimum salary, and one standard over-cap allowance — and the rest are dated, not half-built. That document also lists four prerequisites this milestone cannot defer, the largest being that `Player` currently carries no service time or age.

Two additions from `docs/competitive-feature-review.md`, both inside the work this milestone already owns: the free-agency board is columned **by position against the team's own depth** (a market you cannot read is a market you cannot play), and the market-resolution model is written into `docs/architecture.md` when chosen, not left implicit.

UI: an offer screen with every signing route's verdict (6a, done), then the free-agency board — positional columns, best available per slot — and counteroffers (6b).

## Milestone 7 — Calendar and game simulation

- Schedule
- team strength
- line-ups/minutes
- game outcomes
- box-score statistics
- fatigue and injuries
- standings
- postseason

From `docs/competitive-feature-review.md` §4 and §7, all of them ruleset data rather than code, and all cheaper now than retrofitted:

- **Postseason format** — series length and home-court sequence configured, not fixed.
- **Tie-break sequence** — an ordered list in the ruleset, with its own tests. A standings tie resolved by a rule the league never stated is the classic silent-wrong-answer bug.
- **Bounded model terms** — every input to an outcome probability carries a named, tested bound. No single term may dominate a result.
- **Positional depth chart** — needed for minutes allocation anyway, and reused by the M6 free-agency board.
- **Short-term contracts** and the **in-season signing window**, which only become expressible once a calendar exists.

UI: calendar/advance-date controls, box scores, and a standings view.

## Milestone 8 — Player lifecycle

- draft classes
- scouting uncertainty
- development
- ageing
- retirement
- records and history

The lottery lands here, and it lands **configurable** — a weighting table in the ruleset, not an algorithm in code, even for the first version. Same for the **award set**: which awards exist and how they are voted is data, because a modded league that has no defensive award should not need a code change. Player biography (birthplace, prior programme, draft class) is added with career history, since `docs/competitive-feature-review.md` §2 seeds relationships from it.

UI: draft-class scouting view and a draft-day screen.

## Milestone 9 — AI front offices

- organisational direction
- asset valuation
- roster needs
- trade targeting
- free-agent targeting
- draft decisions
- explainable decisions and diagnostics

Also here, from `docs/competitive-feature-review.md` §1 and §3, because each needs an AI counterparty to mean anything:

- **Cash as a tradeable asset** — a fourth `TradeAssetMovement` kind with a per-season allowance in `TradeRules`. This is how a team buys a pick, and its absence is why AI trade markets feel thin.
- **Configurable participant and asset caps** on a trade — stated as bounds on validation cost, not dressed up as a league rule.
- **Contract buyouts** and the **post-buyout market** — a late-season pool of bought-out players, interacting with the postseason eligibility cutoff from M7.
- **Player signing demands** — a free agent conditioning a signing on a named teammate, or refusing a named rival. Needs M13's relationship graph to be interesting; the plumbing goes here.
- **League power rankings** and the **daily offseason digest** — both derived read models on the inbox surface. The power ranking must be computed from the same team-strength function the simulation uses, never a second opinion.

UI: an inbox/news feed surfacing AI-driven trades and signings with their explanations, plus a diagnostics view for the explainability data already flowing through `DomainOperationResult`/rule-violation types since Milestone 0.

## Milestone 10 — Mod platform

- versioned schemas
- manifests
- validation CLI
- safe asset loading
- sample data pack
- compatibility errors
- documentation

This milestone carries the weight of the content-neutrality position in `docs/vision.md` → Moddability: people will model leagues we never anticipated, so the format has to be expressive enough that they do not need us. Concretely, from `docs/competitive-feature-review.md` §4 and §5:

- **Per-rule prose** — an optional `description` on each configurable rule, explaining what it does and why a league might adopt it. Required for published packs by the validator.
- **Per-rule editing in the UI** — sliders and fields over the ruleset directly, not just preset selection.
- **Pre-authored draft-class playlists** — an ordered list of hand-authored classes consumed one per season, with loop, shuffle, and reverse. The strongest single moddability story on the list: a community can ship twenty years of classes as data.
- **Uncapped and no-draft leagues** — the `schemaVersion` 4 fix already specified in `docs/negotiation-mechanisms.md` → "Ruleset genericity". It must land **before** the tax bill is built on top of the assumption that a tax line exists.
- **Signing bonus amortisation** and **performance escalators** with likelihood classification — both are "buy cap relief now against a risk", and both belong with the tax work.

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

Long-career performance belongs here specifically: a multi-decade save is the honest stress test for save size, ledger growth, and simulation throughput, and it is a profiling target rather than a feature.

## Milestone 13 — league life and locker room (post-MVP)

Added by `docs/competitive-feature-review.md`, which holds the reasoning and the per-item verdicts. Deliberately one milestone rather than scope smuggled into M6–M12, because every item here breaks one of two assumptions the codebase currently rests on: **the ruleset is loaded once and fixed**, and **league membership is fixed at creation**.

Locker room:

- player-to-player affinity as a directed graph, seeded from shared history (birthplace, prior team, prior amateur programme, draft class) and moved by competition — repeated postseason elimination by the same opponent breeds a rivalry;
- personality traits as ruleset vocabulary, not a Domain enum, with compatibility driving locker-room friction;
- team chemistry as a continuous term feeding team strength — explicitly not discrete named bonuses for star pairings;
- **promises** as first-class objects: an assurance given during negotiation (playing time, a re-signing, no trade) that is later kept or broken. This is the clause concept `Contract` is missing, generalised past the contract;
- a **general-manager trust rating** moved by kept and broken promises, gating how much a negotiating player believes an assurance. Treated as an explainability feature: it turns "the AI ignored my pitch" into a visible number;
- descendant players — a retired player's child in a later draft class with correlated attributes.

League life:

- rule changes proposed and adopted **during** a save, making `LeagueRuleset` a versioned timeline rather than a load-time constant;
- scheduled expansion, including an expansion draft, moving league membership from fixed-at-creation to an event;
- franchise relocation and rebranding — the `Franchise`/`Team` split already models the identity this needs;
- an in-season secondary competition, described in the ruleset schedule;
- a master toggle disabling all automatic league evolution, because a simulation that reshapes the league without consent is a bug to some players.

Both halves are save-schema and cross-project public-API changes, which `CLAUDE.md` puts under change control. Not to be started incrementally as a side effect of a smaller task.

UI: a relationship/morale view on the roster screen, a trust and promises panel, and a league-rules screen showing what changed and when.
