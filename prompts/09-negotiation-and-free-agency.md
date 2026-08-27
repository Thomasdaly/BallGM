# Milestone 6 — contract negotiation and free agency

**Status: 6a is shipped. This brief now governs 6b only.** The original version of this file
predated the schema-v4 prerequisite and asked for the whole of Milestone 6 in one go; it was
superseded rather than amended, because a brief that no longer matches the code is worse than none.
What 6a actually built, and the three decisions it forced, are recorded in
`docs/negotiation-mechanisms.md` and summarised in `CLAUDE.md` → "Current state". Read those first
and do not re-derive them.

## What 6a shipped, so 6b does not rebuild it

- `BallGM.Domain.Negotiations.Offer` — immutable, carrying the same `ContractSeasonTerm`s a contract
  carries, validated by the shared `ContractTerms.Normalize`. An offer is superseded, never amended.
- `BallGM.Rules.Signings.OfferLegality` — term, escalation and de-escalation, compensation ceiling,
  compensation floor, each skippable by configuration and each reporting its own skip as a note.
- `BallGM.Rules.Signings.SigningRouteTable` — four routes (unrestricted, minimum salary, cap room,
  standard over-cap allowance), each reporting permitted / refused-with-the-figure / not-a-rule-here.
- `SigningValidator` (never mutates) and `SigningExecutor` (re-validates, undo stack, ledger last),
  reached through `ISigningEngine` / `RulesSigningEngine`, with `LeagueSession.AssessOffer` and
  `SubmitOffer` on top.
- `BallGM.Rules.Cap.RosterSlotHoldProjection`, behind `ICapLedger`, so the room a cap sheet reports
  is room a team can actually spend.
- `NegotiationRules` in the ruleset at schema version 5, including `marketResolution` and
  `offerExpiryDays` — **already read into the runtime type and unused**, deliberately, so that 6b
  adds no second schema change.
- A `FreeAgencyView` offer screen, and a fixture market: a star, a would-take-less veteran, a
  term-wanter, a role-wanter, a rookie at the floor, an injured veteran; a team with real room, a
  team with only its allowance, and a team past the apron with nothing to offer but a pitch.

## Goal for 6b

Open the client during free agency, offer a contract to a player another team also wants, and either
sign them or watch them sign elsewhere — with the player's reasons stated in terms you could have
planned against beforehand. A market whose outcomes cannot be explained is a market a player will
accuse of cheating, and they will usually be right.

6a can tell a GM what they are *allowed* to offer. It cannot tell them what it would *take*. That
gap is this milestone.

## Scope

- **`Negotiation` aggregate**: an explicit state machine (open, offer made, countered, accepted,
  declined, withdrawn, expired), advanced only through its own methods returning
  `DomainOperationResult`. It keeps the history; there is no mutable "current offer" field, because
  the sequence of what was offered and refused *is* the negotiation, and Milestone 9's AI will need
  to read it back. `LatestOfferFrom(teamId)` is a query over that history, not a field.
- **`PlayerPreference` as a decomposed value model, never a single score.** A player weighs at least
  compensation, contract length, guarantee, role and playing time, team quality, and market appeal,
  weighted by age via `Player.AgeOn(...)` — and evaluating an offer returns the per-factor
  contributions, not just the total. Build it decomposed from the first commit: a signing you can
  rank but cannot explain is the exact failure Milestone 9 then has to unpick. Compensation's
  contribution must be **monotone by construction**, so the monotonicity test asserts a property
  rather than a hope.
- **Deterministic stochastic choice.** Preference produces an ordering; the injected `IRandomSource`
  (now `BallGM.Domain.Randomness`, alongside `SeededRandomSource`) decides only where the model is
  genuinely indifferent — inside a stated indifference band. Same seed, same offers, same league,
  same signings, and a test that proves it by running the market twice.
- **Consume `NegotiationRules.MarketResolution` rather than merely reading it.** `ResolutionPoint`
  is the default and the position `docs/negotiation-mechanisms.md` argues for. Two orderings have to
  be chosen, commented in code, and written into `docs/architecture.md`: how competing offers for one
  player are ranked (preference total, then the seeded tie-break), and how players are ordered within
  one resolution point (a signing consumes room, so the order matters). `Immediate` must still work,
  because it is a configured value and not a branch nobody takes.
- **Offer expiry.** `OfferExpiryDays` is configured and unused. Give it meaning, or delete it and say
  why in the mechanism inventory — a field that is read and ignored is the failure the version gate
  exists to prevent, arriving from the inside.
- **Counteroffers.** A player countering is a new immutable offer from the other party, not a
  mutation of the first. The state machine has the transition; nothing produces one yet.
- **Read model plus UI**: the free-agency board, columned **by position against the team's own
  depth** (`docs/roadmap.md` M6 and `docs/competitive-feature-review.md` both call for this), showing
  who is available, what they are asking, and who is chasing them; and an offer screen that shows the
  player's stated priorities *before* you offer and their per-factor reasoning *after* they decide.
  The existing `FreeAgencyView` is the place for the second half — it currently has a visible hole
  where "what would it take" goes. Application read models only; `ArchitectureBoundaryTests` still
  governs; `LeagueSession` still owns the loaded league.

## Constraints

- Fictional throughout. Generic rule names, configurable amounts, no real-league branding.
- The player's own preference model lives here; what a *team* is willing to pay is Milestone 9. Keep
  the boundary clean so that milestone does not have to unpick this one.
- Negotiations and offers are save/mod surface: version the DTOs from the first commit, keep runtime
  types separate from serialized shapes, and fail structurally on content this build cannot read. An
  in-flight negotiation must survive a round trip.
- Determinism: no `DateTime.Now`, no ungenerated identifiers, no ambient randomness. Time,
  identifiers, and the random source all arrive injected.
- Do not reopen the signing-route zoo. Every deferred route in the mechanism inventory is dated;
  adding one is a row in the route table, not a branch, and not this milestone.

## Tests, unhappy paths weighted as heavily as happy ones

A player accepting the best offer; the same player accepting a *worse* offer for a stated non-money
reason; monotonicity (more money, all else equal, never scores worse); a counteroffer accepted and
one refused; an expired negotiation; two teams chasing one player resolved identically across runs
from the same seed; the resolution-point ordering producing the same result whatever order the offers
were submitted in; and a save round trip of a negotiation mid-counteroffer.

The 6a suites already cover an offer rejected for no cap room, an offer rejected for a full roster,
and acceptance rolled back leaving nothing behind — extend them rather than duplicating them.

## Finishing

Run `./tools/verify-dotnet.sh`. Then run the market once in the shipped league and once in
`data/rulesets/conformance/uncapped-open-league.json`, and report what a GM is shown in each —
including what they are *not* shown. Launch the client, chase the star with the team that has room
and again with the team that has only its allowance, and lose one.

Update `docs/architecture.md` (the resolution ordering, once chosen) and `docs/domain-language.md`
(Negotiation, Preference factor, Counteroffer) where behaviour actually changes, and mark the
mechanism inventory's 6b list as shipped. Then supersede this file again rather than leaving it
describing work that is done.

## Before implementation

Propose the `Negotiation` state machine's transitions, the preference factors and how their
contributions are reported, both resolution orderings, the read-model additions, and what expiry
means. Explain what is deliberately deferred and why.
